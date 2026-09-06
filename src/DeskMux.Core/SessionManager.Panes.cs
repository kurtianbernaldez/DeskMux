namespace DeskMux.Core;

public sealed partial class SessionManager
{
    private bool _applyingPanes;
    private readonly HashSet<Guid> _pendingPaneCanvases = [];
    private HashSet<Guid>? _paneTouched;

    /// <summary>Commits selection only after a picker has resolved a specific native window.</summary>
    public bool OpenPane(long targetHandle, long sourceHandle, PaneOrientation orientation, bool allowMove = false)
    {
        var success = false;
        Change(() =>
        {
            var active = ActiveSession;
            if (active is null) { Error("Create or select a session before opening a pane."); return; }
            var snapshot = Inspect(targetHandle);
            if (snapshot is null || IsExcluded(snapshot.Fingerprint)) { Error("This application is unavailable or excluded."); return; }
            var entry = AllEntries().FirstOrDefault(w => !w.IsMissing && w.Handle == targetHandle && SameWindow(w));
            var owner = entry is null ? null : State.Sessions.First(s => s.Windows.Contains(entry));
            if (owner != null && owner != active && !allowMove) { Error("Confirm moving this window from its other session first."); return; }
            if (entry != null && owner == active && CanvasFor(active, entry.Id) != null)
            {
                success = FocusPaneCore(entry.Id);
                return;
            }
            if (entry == null && !snapshot.IsVisible) { Error("Show this application before adding it as a pane."); return; }
            entry ??= new ManagedWindow { Handle = targetHandle, Fingerprint = Clone(snapshot.Fingerprint), Layout = MonitorMapper.Clone(snapshot.Layout) };
            var selected = entry;
            var source = active.Windows.FirstOrDefault(w => !w.IsMissing && w.Handle == sourceHandle && SameWindow(w));
            success = PaneTransaction(() =>
            {
                if (owner != null && owner != active)
                {
                    RemoveLeaf(owner, selected.Id);
                    owner.Windows.Remove(selected);
                }
                if (!active.Windows.Contains(selected)) active.Windows.Add(selected);
                var canvas = source == null ? null : CanvasFor(active, source.Id);
                if (canvas == null)
                {
                    var mapped = MonitorMapper.Map(source?.Layout ?? snapshot.Layout, _windows.GetMonitors());
                    canvas = active.PaneCanvases.FirstOrDefault(c => SameMonitor(c, mapped));
                    if (canvas != null)
                    {
                        // An existing canvas owns this monitor. Split its most recently used pane.
                        source = active.Windows.Where(w => PaneTree.FindLeaf(canvas, w.Id) != null)
                            .OrderByDescending(w => w.LastFocusedUtc).FirstOrDefault();
                    }
                    else
                    {
                        canvas = new PaneCanvas { MonitorDevice = mapped.MonitorDevice, MonitorId = mapped.MonitorId,
                            MonitorWorkArea = mapped.MonitorWorkArea, Dpi = mapped.Dpi };
                        active.PaneCanvases.Add(canvas);
                    }
                }
                canvas.ZoomedLeafId = null;
                PaneTree.Split(canvas, source?.Id == selected.Id ? null : source?.Id, selected.Id, orientation);
            }, [selected]);
            if (success) FocusPaneCore(selected.Id);
        });
        return success;
    }

    /// <summary>Adds a focused window normally, but joins the pane canvas when one already occupies its monitor.</summary>
    public bool AddWindowToActiveLayout(long handle)
    {
        var active = ActiveSession;
        if (active is null) { Error("Choose a session before adding a window."); return false; }
        var snapshot = Inspect(handle);
        if (snapshot is null) { Error("This window is no longer available or cannot be managed."); return false; }
        var mapped = MonitorMapper.Map(snapshot.Layout, _windows.GetMonitors());
        var canvas = active.PaneCanvases.FirstOrDefault(c => SameMonitor(c, mapped));
        if (canvas is null) return AddWindow(handle, active.Id);
        var orientation = canvas.MonitorWorkArea.Width >= canvas.MonitorWorkArea.Height
            ? PaneOrientation.Vertical : PaneOrientation.Horizontal;
        return OpenPane(handle, 0, orientation);
    }

    public bool FocusPane(Guid entryId)
    {
        var result = false;
        Change(() =>
        {
            var owner = State.Sessions.FirstOrDefault(s => s.Windows.Any(w => w.Id == entryId));
            if (owner != null && owner != ActiveSession) SwitchCore(owner.Id);
            result = FocusPaneCore(entryId);
        });
        return result;
    }

    private bool FocusPaneCore(Guid entryId)
    {
        var entry = ActiveSession?.Windows.FirstOrDefault(w => w.Id == entryId && !w.IsMissing);
        if (entry == null || !SameWindow(entry)) { Error("This pane is no longer available in the active session."); return false; }
        var canvas = CanvasFor(ActiveSession!, entryId);
        if (canvas?.ZoomedLeafId is { } zoom && PaneTree.FindLeaf(canvas, entryId)?.Id != zoom)
            if (!PaneTransaction(() => canvas.ZoomedLeafId = null)) return false;
        var result = Operate(() => _windows.Focus(entry));
        if (!result.Success) { WindowError(entry, "focus", result.Error); return false; }
        entry.LastFocusedUtc = DateTime.UtcNow;
        return true;
    }

    public void NavigatePane(long handle, PaneDirection direction) => Change(() =>
    {
        if (!FindFocusedPane(handle, out var entry, out var canvas)) return;
        if (canvas.ZoomedLeafId != null && !PaneTransaction(() => canvas.ZoomedLeafId = null)) return;
        var next = PaneTree.Neighbor(canvas, entry.Id, direction, ActiveSession!.Windows.ToDictionary(w => w.Id, w => w.LastFocusedUtc));
        if (next is { } id) FocusPaneCore(id); else Error("No pane in that direction.");
    });

    public void ResizePane(long handle, PaneDirection direction) => Change(() =>
    {
        if (!FindFocusedPane(handle, out var entry, out var canvas)) return;
        PaneTransaction(() =>
        {
            canvas.ZoomedLeafId = null;
            if (!PaneTree.Resize(canvas, entry.Id, direction)) throw new InvalidOperationException("No matching split can be resized in that direction.");
        });
    });

    public void SwapPane(long handle, int offset) => Change(() =>
    {
        if (!FindFocusedPane(handle, out var entry, out var canvas)) return;
        if (PaneTransaction(() =>
        {
            canvas.ZoomedLeafId = null;
            if (!PaneTree.Swap(canvas, entry.Id, offset)) throw new InvalidOperationException("There is no other pane to swap with.");
        })) FocusPaneCore(entry.Id);
    });

    public void TogglePaneZoom(long handle) => Change(() =>
    {
        if (!FindFocusedPane(handle, out var entry, out var canvas)) return;
        if (HidingPaused) { Error("Resume sessions before zooming a pane."); return; }
        if (PaneTransaction(() => canvas.ZoomedLeafId = canvas.ZoomedLeafId == null ? PaneTree.FindLeaf(canvas, entry.Id)!.Id : null))
            FocusPaneCore(entry.Id);
    });

    public void ReleasePane(Guid entryId) => Change(() =>
    {
        var owner = State.Sessions.FirstOrDefault(s => CanvasFor(s, entryId) != null);
        if (owner == null) return;
        var entry = owner.Windows.First(w => w.Id == entryId);
        PaneTransaction(() =>
        {
            if ((owner == ActiveSession || HidingPaused) && !PaneShow(entry)) throw new InvalidOperationException(LastError ?? "Could not show the pane.");
            RemoveLeaf(owner, entryId);
        });
    });

    /// <summary>A native move/resize gesture turns a pane back into a movable floating member.</summary>
    public bool ReleasePaneForManualMove(long handle)
    {
        var active = ActiveSession;
        var entry = active?.Windows.FirstOrDefault(w => !w.IsMissing && w.Handle == handle);
        if (active is null || entry is null || CanvasFor(active, entry.Id) is null) return false;
        ReleasePane(entry.Id);
        return CanvasFor(active, entry.Id) is null;
    }

    public void ReapplyPaneLayouts() => Change(() => ReapplyVisiblePanes());

    public void HandleDisplayChange() => Change(() =>
    {
        MapPaneMonitors();
        ReapplyVisiblePanes();
    });

    public void FlushPaneLayoutChanges()
    {
        if (_applyingPanes || _shuttingDown || _pendingPaneCanvases.Count == 0) return;
        var pending = _pendingPaneCanvases.ToHashSet();
        _pendingPaneCanvases.Clear();
        ReapplyVisiblePanes(pending);
        Dirty();
    }

    public string PaneStatus(Guid entryId)
    {
        var owner = State.Sessions.FirstOrDefault(s => s.Windows.Any(w => w.Id == entryId));
        var canvas = owner == null ? null : CanvasFor(owner, entryId);
        if (canvas == null) return "Floating";
        var leaf = PaneTree.FindLeaf(canvas, entryId)!;
        var ancestry = new List<string>();
        var current = leaf.Id;
        var visited = new HashSet<Guid>();
        while (visited.Add(current))
        {
            var parent = canvas.Nodes.FirstOrDefault(n => n.FirstChildId == current || n.SecondChildId == current);
            if (parent == null) break;
            ancestry.Insert(0, parent.Orientation == PaneOrientation.Vertical ? "Left/right" : "Top/bottom");
            current = parent.Id;
        }
        try
        {
            var bounds = canvas.ZoomedLeafId == leaf.Id ? canvas.MonitorWorkArea : PaneTree.Calculate(canvas)[entryId].Bounds;
            return $"Pane {PaneTree.Leaves(canvas).ToList().FindIndex(n => n.Id == leaf.Id) + 1} · {canvas.MonitorDevice} · " +
                (canvas.ZoomedLeafId == leaf.Id ? "Zoomed · " : "") +
                $"{bounds.X}, {bounds.Y} · {bounds.Width} × {bounds.Height}" + (ancestry.Count > 0 ? " · " + string.Join(" → ", ancestry) : "");
        }
        catch (Exception) { return "Pane · layout needs attention"; }
    }

    private bool FindFocusedPane(long handle, out ManagedWindow entry, out PaneCanvas canvas)
    {
        entry = ActiveSession?.Windows.FirstOrDefault(w => !w.IsMissing && w.Handle == handle)!;
        canvas = entry == null ? null! : CanvasFor(ActiveSession!, entry.Id)!;
        if (canvas != null && entry != null && SameWindow(entry)) return true;
        Error("Focus a pane first. Open a split to create a pane layout.");
        return false;
    }

    private static PaneCanvas? CanvasFor(WorkspaceSession session, Guid entryId) => session.PaneCanvases.FirstOrDefault(c => PaneTree.FindLeaf(c, entryId) != null);
    private static bool SameMonitor(PaneCanvas canvas, WindowLayout layout) =>
        !string.IsNullOrEmpty(canvas.MonitorId) && !string.IsNullOrEmpty(layout.MonitorId)
            ? string.Equals(canvas.MonitorId, layout.MonitorId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(canvas.MonitorDevice, layout.MonitorDevice, StringComparison.OrdinalIgnoreCase);
    private static bool IsZoomHidden(WorkspaceSession session, Guid entryId)
    {
        var canvas = CanvasFor(session, entryId);
        if (canvas?.ZoomedLeafId is not { } zoom) return false;
        var zoomedWindowId = canvas.Nodes.FirstOrDefault(n => n.Id == zoom)?.WindowId;
        if (zoomedWindowId is null || session.Windows.FirstOrDefault(w => w.Id == zoomedWindowId)?.IsMissing != false) return false;
        return PaneTree.FindLeaf(canvas, entryId)?.Id != zoom;
    }

    private void RemoveLeaf(WorkspaceSession owner, Guid entryId)
    {
        var canvas = CanvasFor(owner, entryId);
        if (canvas == null) return;
        canvas.ZoomedLeafId = null;
        PaneTree.Remove(canvas, entryId);
        // A single surviving native window no longer needs a tiling canvas. Keep its
        // current rectangle and release it so the user can move it normally.
        if (canvas.RootNodeId == null || PaneTree.Leaves(canvas).Take(2).Count() < 2)
        {
            if (owner == ActiveSession || HidingPaused)
                foreach (var survivor in owner.Windows.Where(w => PaneTree.FindLeaf(canvas, w.Id) != null && w.HiddenByDeskMux))
                    if (!PaneShow(survivor)) throw new InvalidOperationException(LastError ?? "Could not release the surviving pane.");
            owner.PaneCanvases.Remove(canvas);
        }
    }

    private bool RemovePaneMember(Guid entryId)
    {
        var owner = State.Sessions.FirstOrDefault(s => CanvasFor(s, entryId) != null);
        if (owner == null) return false;
        var entry = owner.Windows.First(w => w.Id == entryId);
        var canvas = CanvasFor(owner, entryId)!;
        var neighbor = new[] { PaneDirection.Right, PaneDirection.Left, PaneDirection.Down, PaneDirection.Up }
            .Select(d => PaneTree.Neighbor(canvas, entryId, d)).FirstOrDefault(id => id != null);
        if (PaneTransaction(() =>
        {
            if (!PaneShow(entry)) throw new InvalidOperationException(LastError ?? "Could not restore this application; membership was kept.");
            RemoveLeaf(owner, entryId);
            owner.Windows.Remove(entry);
        }) && owner == ActiveSession && neighbor is { } next) FocusPaneCore(next);
        return true;
    }

    private void MovePaneMember(Guid entryId, Guid targetSessionId)
    {
        var target = FindSession(targetSessionId);
        var source = State.Sessions.FirstOrDefault(s => s.Windows.Any(w => w.Id == entryId));
        if (source == null || target == null || source == target) return;
        var entry = source.Windows.First(w => w.Id == entryId);
        PaneTransaction(() =>
        {
            RemoveLeaf(source, entryId);
            source.Windows.Remove(entry);
            target.Windows.Add(entry);
            var visible = target == ActiveSession || HidingPaused || IsExcluded(entry.Fingerprint);
            if (!(visible ? PaneShow(entry) : PaneHide(entry)) && !entry.IsMissing)
                throw new InvalidOperationException(LastError ?? "Could not move this window; membership was restored.");
        });
    }

    private bool TrackPaneWindow(WorkspaceSession owner, ManagedWindow entry, WindowSnapshot snapshot, bool foreground)
    {
        var canvas = CanvasFor(owner, entry.Id);
        if (canvas == null || owner != ActiveSession || HidingPaused) return false;
        entry.Fingerprint.Title = snapshot.Fingerprint.Title;
        if (foreground && !IsZoomHidden(owner, entry.Id)) entry.LastFocusedUtc = DateTime.UtcNow;
        if (!_applyingPanes)
        {
            var desiredHidden = IsZoomHidden(owner, entry.Id);
            if (desiredHidden && snapshot.IsVisible) entry.HiddenByDeskMux = false;
            if (snapshot.IsVisible == desiredHidden || (!desiredHidden &&
                (snapshot.Layout.Bounds != entry.Layout.Bounds || snapshot.Layout.ShowState != WindowShowState.Normal)))
            {
                // Events are inspected after dispatch, and exact successful placements compare
                // equal. A single debounced reflow handles user edits without swallowing rapid edits.
                _pendingPaneCanvases.Add(canvas.Id);
            }
            if (foreground || _pendingPaneCanvases.Count > 0) Dirty();
        }
        return true;
    }

    private void RepairPaneTrees()
    {
        foreach (var session in State.Sessions)
        {
            var valid = session.Windows.Where(w => !IsExcluded(w.Fingerprint)).Select(w => w.Id).ToHashSet();
            var used = new HashSet<Guid>();
            foreach (var canvas in session.PaneCanvases.ToArray())
            {
                if (PaneTree.Validate(canvas, valid, used)) _pendingPaneCanvases.Add(canvas.Id);
                if (canvas.RootNodeId == null) session.PaneCanvases.Remove(canvas);
            }
        }
    }

    private void MapPaneMonitors()
    {
        var monitors = _windows.GetMonitors();
        foreach (var session in State.Sessions)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Keep trees whose physical monitor still exists before mapping disconnected trees.
            foreach (var canvas in session.PaneCanvases.OrderByDescending(c => monitors.Any(m =>
                !string.IsNullOrEmpty(c.MonitorId) ? m.StableId == c.MonitorId : m.DeviceName == c.MonitorDevice)).ToArray())
            {
                var mapped = MonitorMapper.Map(new WindowLayout { Bounds = canvas.MonitorWorkArea, MonitorDevice = canvas.MonitorDevice,
                    MonitorId = canvas.MonitorId, MonitorWorkArea = canvas.MonitorWorkArea, Dpi = canvas.Dpi }, monitors);
                var key = string.IsNullOrEmpty(mapped.MonitorId) ? mapped.MonitorDevice : mapped.MonitorId;
                var safe = monitors.Count > 0 && used.Add(key);
                canvas.MonitorDevice = mapped.MonitorDevice; canvas.MonitorId = mapped.MonitorId;
                canvas.MonitorWorkArea = mapped.MonitorWorkArea; canvas.Dpi = mapped.Dpi;
                if (safe)
                {
                    try { PaneTree.Calculate(canvas); } catch (PaneLayoutException) { safe = false; }
                }
                if (safe) continue;
                // Never stack multiple full trees on a remaining monitor. Release the displaced
                // tree and retain its members at individually mapped, reachable rectangles.
                foreach (var leaf in PaneTree.Leaves(canvas))
                {
                    var entry = session.Windows.First(w => w.Id == leaf.WindowId);
                    var previous = entry.Layout;
                    var floating = MonitorMapper.Map(previous, monitors);
                    if (!entry.IsMissing && (session == ActiveSession || HidingPaused))
                    {
                        var result = Operate(() => _windows.ApplyLayout(entry, floating));
                        if (result.Success) entry.Layout = floating;
                        else { WindowError(entry, "release after display change", result.Error); Show(entry); }
                        if (entry.HiddenByDeskMux) Show(entry);
                    }
                    else entry.Layout = floating;
                }
                session.PaneCanvases.Remove(canvas);
                Error("A pane canvas no longer fits an available monitor. Its windows were released to reachable floating layouts.");
            }
        }
    }

    private bool ReapplyVisiblePanes(ISet<Guid>? onlyCanvases = null)
    {
        if (_applyingPanes || ActiveSession == null) return true;
        RepairPaneTrees();
        MapPaneMonitors();
        return PaneTransaction(() => { }, forceReapply: true, onlyCanvases: onlyCanvases);
    }

    private sealed record PaneWindowBackup(ManagedWindow Entry, WindowLayout Layout, bool Visible, bool Hidden);
    private bool PaneTransaction(Action mutation, IEnumerable<ManagedWindow>? extra = null, bool forceReapply = false, ISet<Guid>? onlyCanvases = null)
    {
        if (_applyingPanes) return false;
        var trees = State.Sessions.ToDictionary(s => s.Id, s => s.PaneCanvases.Select(PaneTree.Clone).ToList());
        var memberships = State.Sessions.ToDictionary(s => s.Id, s => s.Windows.ToList());
        var backups = AllEntries().Concat(extra ?? []).DistinctBy(w => w.Id).Select(w =>
        {
            var live = !w.IsMissing && SameWindow(w) ? Inspect(w.Handle) : null;
            return new PaneWindowBackup(w, MonitorMapper.Clone(live?.IsVisible == true ? live.Layout : w.Layout), live?.IsVisible == true, w.HiddenByDeskMux);
        }).ToArray();
        _applyingPanes = true;
        _paneTouched = [];
        try
        {
            mutation();
            RepairPaneTrees();
            var plans = (ActiveSession?.PaneCanvases ?? [])
                .Where(c => onlyCanvases == null || onlyCanvases.Contains(c.Id))
                .Where(c => forceReapply || !SameCanvasState(c, trees[ActiveSession!.Id].FirstOrDefault(old => old.Id == c.Id)))
                .Select(c => (Canvas: c, Live: LiveCanvas(ActiveSession!, c)))
                .Where(p => p.Live.RootNodeId != null)
                .Select(p => (p.Canvas, Layouts: PaneTree.Calculate(p.Live))).ToArray();
            var placements = new List<(ManagedWindow Window, WindowLayout Layout)>();
            foreach (var (canvas, layouts) in plans)
            {
                foreach (var (id, layout) in layouts)
                {
                    var entry = ActiveSession!.Windows.First(w => w.Id == id);
                    if (!SameWindow(entry)) { MarkMissing(entry); throw new InvalidOperationException("A pane closed during layout. Its remaining layout was restored."); }
                    if (!HidingPaused && IsZoomHidden(ActiveSession, id))
                    {
                        // Persist the unzoomed rectangle for crash recovery before the journaled hide.
                        if (!entry.HiddenByDeskMux && !PaneHide(entry)) throw new InvalidOperationException(LastError ?? "Could not hide a zoom sibling.");
                        continue;
                    }
                    var desired = MonitorMapper.Clone(layout);
                    if (canvas.ZoomedLeafId == PaneTree.FindLeaf(canvas, id)?.Id) desired.Bounds = canvas.MonitorWorkArea;
                    _paneTouched.Add(id);
                    if (entry.HiddenByDeskMux && !PaneShow(entry)) throw new InvalidOperationException(LastError ?? "Could not restore a pane.");
                    placements.Add((entry, desired));
                }
            }
            var results = _windows.ApplyLayouts(placements);
            if (results.Count != placements.Count) throw new InvalidOperationException("Windows returned an incomplete placement result.");
            var failures = new List<string>();
            for (var i = 0; i < placements.Count; i++)
            {
                var (entry, desired) = placements[i];
                if (!results[i].Success) failures.Add($"Could not position {entry.DisplayTitle}: {results[i].Error}");
                else { entry.Layout = desired; entry.Status = ""; }
            }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            return true;
        }
        catch (Exception ex)
        {
            foreach (var session in State.Sessions) { session.PaneCanvases = trees[session.Id]; session.Windows = memberships[session.Id]; }
            var rollbackErrors = new List<string>();
            foreach (var backup in backups.Where(b => _paneTouched.Contains(b.Entry.Id)).Reverse())
            {
                var entry = backup.Entry;
                if (!SameWindow(entry)) { MarkMissing(entry); continue; }
                var result = Operate(() => _windows.ApplyLayout(entry, backup.Layout));
                if (result.Success) entry.Layout = MonitorMapper.Clone(backup.Layout);
                else rollbackErrors.Add(entry.DisplayTitle + ": " + result.Error);
                // Reachability takes precedence if placement rollback is incomplete.
                var shown = Show(entry); // Restore also honors the original minimized/maximized state.
                if (!shown) rollbackErrors.Add(entry.DisplayTitle + ": could not restore visibility");
                if (!backup.Visible && backup.Hidden && result.Success && shown && !Hide(entry))
                    rollbackErrors.Add(entry.DisplayTitle + ": could not restore hidden state");
            }
            RepairPaneTrees();
            if (rollbackErrors.Count > 0)
            {
                HidingPaused = true;
                foreach (var canvas in State.Sessions.SelectMany(s => s.PaneCanvases)) canvas.ZoomedLeafId = null;
                foreach (var entry in AllEntries()) Show(entry);
            }
            Error(ex.Message + (rollbackErrors.Count > 0 ? " Rollback was incomplete; hiding is paused. " + string.Join("; ", rollbackErrors) : " Previous layout and membership restored."));
            return false;
        }
        finally { _applyingPanes = false; _paneTouched = null; }
    }

    private PaneCanvas LiveCanvas(WorkspaceSession session, PaneCanvas canvas)
    {
        var live = PaneTree.Clone(canvas);
        var valid = session.Windows.Where(w => !w.IsMissing && !IsExcluded(w.Fingerprint)).Select(w => w.Id).ToHashSet();
        PaneTree.Validate(live, valid, new HashSet<Guid>());
        return live;
    }

    private bool PaneShow(ManagedWindow entry) { _paneTouched?.Add(entry.Id); return Show(entry); }
    private bool PaneHide(ManagedWindow entry) { _paneTouched?.Add(entry.Id); return Hide(entry); }

    private static bool SameCanvasState(PaneCanvas current, PaneCanvas? previous)
    {
        if (previous == null || current.RootNodeId != previous.RootNodeId || current.ZoomedLeafId != previous.ZoomedLeafId ||
            current.MonitorDevice != previous.MonitorDevice || current.MonitorId != previous.MonitorId ||
            current.MonitorWorkArea != previous.MonitorWorkArea || current.Dpi != previous.Dpi || current.Nodes.Count != previous.Nodes.Count) return false;
        var oldNodes = previous.Nodes.ToDictionary(n => n.Id);
        return current.Nodes.All(n => oldNodes.TryGetValue(n.Id, out var old) && n.WindowId == old.WindowId &&
            n.Orientation == old.Orientation && n.Ratio == old.Ratio && n.FirstChildId == old.FirstChildId && n.SecondChildId == old.SecondChildId);
    }
}
