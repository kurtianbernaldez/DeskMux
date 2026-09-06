namespace DeskMux.Core;

/// <summary>Owns window membership and transitions. All calls and Changed notifications run on the UI thread.</summary>
public sealed partial class SessionManager
{
    private readonly IWindowSystem _windows;
    private readonly IStateStore _store;
    private readonly ILog _log;
    private bool _shuttingDown;
    private readonly Dictionary<long, DateTime> _lastRehideAttempt = [];
    public WorkspaceState State { get; }
    public WorkspaceSession? ActiveSession => State.Sessions.FirstOrDefault(s => s.Id == State.ActiveSessionId);
    public bool HidingPaused { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public string? LastError { get; private set; }
    public event Action? Changed;

    public SessionManager(WorkspaceState state, IWindowSystem windows, IStateStore store, ILog log)
    {
        State = state;
        _windows = windows;
        _store = store;
        _log = log;
        StateSanitizer.Normalize(state);
    }

    public WorkspaceSession CreateSession(string name)
    {
        var session = new WorkspaceSession { Name = NormalizeName(name) };
        Change(() =>
        {
            State.Sessions.Add(session);
            if (State.Sessions.Count == 1) State.ActiveSessionId = session.Id;
            _log.Write("session.created", session.Name, new { session.Id });
        });
        return session;
    }

    public void RenameSession(Guid id, string name) => Change(() =>
    {
        if (FindSession(id) is { } session) session.Name = NormalizeName(name);
    });

    public void ReorderSession(Guid id, int offset) => Change(() =>
    {
        var current = State.Sessions.FindIndex(s => s.Id == id);
        if (current < 0) return;
        var target = (int)Math.Clamp((long)current + offset, 0, State.Sessions.Count - 1);
        var session = State.Sessions[current];
        State.Sessions.RemoveAt(current);
        State.Sessions.Insert(target, session);
    });

    public void DeleteSession(Guid id, Guid? moveToSessionId) => Change(() =>
    {
        var source = FindSession(id);
        if (source is null) return;
        var target = moveToSessionId is { } targetId ? FindSession(targetId) : null;
        if (moveToSessionId is not null && (target is null || target == source))
        { Error("Choose another existing session to receive these windows."); return; }
        if (target is not null)
        {
            target.Windows.AddRange(source.Windows);
            source.Windows.Clear();
        }
        else
        {
            foreach (var entry in source.Windows.ToArray())
            {
                // Keep membership if a hidden window cannot be restored; it remains recoverable in the UI and journal.
                if (!Show(entry)) continue;
                source.Windows.Remove(entry);
            }
            if (source.Windows.Count > 0)
            { RepairPaneTrees(); Error("Some windows could not be restored. The session was retained so you can retry Show All Managed Windows."); return; }
        }
        var oldIndex = State.Sessions.IndexOf(source);
        State.Sessions.Remove(source);
        if (State.ActiveSessionId == id)
            State.ActiveSessionId = target?.Id ?? State.Sessions.ElementAtOrDefault(Math.Min(oldIndex, State.Sessions.Count - 1))?.Id;
        if (State.PreviousSessionId == id) State.PreviousSessionId = null;
        ApplyVisibility();
        RaiseActiveWindows();
        FocusActive();
        _log.Write("session.deleted", source.Name, new { id, moveToSessionId });
    });

    public void SwitchTo(Guid id) => Change(() => SwitchCore(id));

    public void SwitchRelative(int offset) => Change(() =>
    {
        if (State.Sessions.Count == 0) return;
        var current = State.Sessions.FindIndex(s => s.Id == State.ActiveSessionId);
        if (current < 0) current = offset < 0 ? 0 : -1;
        var target = (int)(((long)current + offset) % State.Sessions.Count);
        if (target < 0) target += State.Sessions.Count;
        SwitchCore(State.Sessions[target].Id);
    });

    public void SwitchPrevious() => Change(() =>
    {
        if (State.PreviousSessionId is { } previous && FindSession(previous) is not null) SwitchCore(previous);
    });

    public void Detach() => Change(() =>
    {
        if (ActiveSession is not { } active) return;
        if (State.PreviousSessionId is { } previous && previous != active.Id && FindSession(previous) is not null)
        { SwitchCore(previous); return; }
        CaptureVisible(active.Windows);
        State.PreviousSessionId = active.Id;
        State.ActiveSessionId = null;
        ApplyVisibility();
    });

    public bool AddWindow(long handle, Guid sessionId)
    {
        var added = false;
        Change(() =>
        {
            var target = FindSession(sessionId);
            if (target is null) { Error("Choose a session before adding a window."); return; }
            var snapshot = Inspect(handle);
            if (snapshot is null) { Error("This window is no longer available or cannot be managed."); return; }
            if (IsExcluded(snapshot.Fingerprint)) { Error("This application is excluded from DeskMux."); return; }
            var existing = AllEntries().FirstOrDefault(w => w.Handle == handle && !w.IsMissing && SameWindow(w));
            if (existing is not null)
            {
                if (target.Windows.Contains(existing)) { added = true; return; }
                Error("This window already belongs to another session. Use Move to transfer it.");
                return;
            }
            if (!snapshot.IsVisible)
            { Error("Only visible application windows can be added. Show the window first."); return; }
            var entry = new ManagedWindow
            {
                Handle = handle, Fingerprint = Clone(snapshot.Fingerprint), Layout = MonitorMapper.Clone(snapshot.Layout),
                LastFocusedUtc = _windows.ForegroundWindow == handle ? DateTime.UtcNow : DateTime.MinValue
            };
            target.Windows.Add(entry);
            added = true;
            if (target.Id != State.ActiveSessionId && !HidingPaused) Hide(entry);
            _log.Write("window.captured", entry.DisplayTitle, new { entry.Id, handle, sessionId });
        });
        return added;
    }

    public void SetLaunchProfile(Guid entryId, Guid profileId) => Change(() =>
    {
        var entry = AllEntries().FirstOrDefault(w => w.Id == entryId);
        if (entry is not null && State.Settings.LaunchProfiles.Any(p => p.Id == profileId))
            entry.LaunchProfileId = profileId;
    });

    /// <summary>Reconnects a newly launched native window to an existing missing membership entry.</summary>
    public bool RestoreMissingWindow(Guid entryId, WindowSnapshot candidate)
    {
        var restored = false;
        Change(() =>
        {
            var owner = State.Sessions.FirstOrDefault(s => s.Windows.Any(w => w.Id == entryId));
            var entry = owner?.Windows.FirstOrDefault(w => w.Id == entryId);
            if (owner is null || entry is null) { Error("This saved application is no longer in a session."); return; }
            if (!entry.IsMissing) { restored = true; return; }
            var current = Inspect(candidate.Handle);
            if (current is null || !current.IsVisible || IsExcluded(current.Fingerprint))
            { Error("The restored application window is unavailable or excluded."); return; }
            if (AllEntries().Any(w => w.Id != entry.Id && !w.IsMissing && w.Handle == current.Handle && SameWindow(w)))
            { Error("That application window already belongs to another session."); return; }
            var profile = entry.LaunchProfileId is { } profileId
                ? State.Settings.LaunchProfiles.FirstOrDefault(p => p.Id == profileId) : null;
            if (!WindowMatcher.SameApplication(entry.Fingerprint, current.Fingerprint) &&
                (profile is null || !LaunchCandidateClassifier.MatchesProfile(profile, current.Fingerprint)))
            { Error("The opened window does not match the saved application."); return; }

            var savedLayout = MonitorMapper.Clone(entry.Layout);
            Associate(entry, current, reassociated: true);
            entry.Layout = savedLayout;
            if (owner == ActiveSession || HidingPaused)
            {
                if (CanvasFor(owner, entry.Id) is { } canvas)
                    restored = ReapplyVisiblePanes(new HashSet<Guid> { canvas.Id });
                else
                {
                    var result = Operate(() => _windows.ApplyLayout(entry, MonitorMapper.Map(savedLayout, _windows.GetMonitors())));
                    restored = result.Success;
                    if (result.Success) entry.Status = ""; else WindowError(entry, "restore layout for", result.Error);
                }
            }
            else
            {
                restored = Hide(entry);
                entry.Layout = savedLayout;
            }
            if (!restored && !entry.IsMissing) entry.Status = LastError ?? "The application reopened, but its saved placement could not be restored.";
        });
        return restored;
    }

    public void RemoveWindow(Guid entryId) => Change(() =>
    {
        var missingOwner = State.Sessions.FirstOrDefault(s => s.Windows.Any(w => w.Id == entryId && w.IsMissing));
        if (missingOwner is not null)
        {
            RemoveLeaf(missingOwner, entryId);
            missingOwner.Windows.RemoveAll(w => w.Id == entryId);
            _log.Write("window.removed", "Missing application record removed.", new { entryId });
            return;
        }
        if (RemovePaneMember(entryId)) return;
        var source = State.Sessions.FirstOrDefault(s => s.Windows.Any(w => w.Id == entryId));
        var entry = source?.Windows.FirstOrDefault(w => w.Id == entryId);
        if (source is null || entry is null) return;
        if (!Show(entry))
        { Error("This window could not be restored. Its session membership was kept so you can retry."); return; }
        source.Windows.Remove(entry);
        _log.Write("window.removed", entry.DisplayTitle, new { entryId });
    });

    public void MoveWindow(Guid entryId, Guid targetSessionId) => Change(() =>
    {
        MovePaneMember(entryId, targetSessionId);
    });

    public void Reconcile() => Change(() =>
    {
        var candidates = _windows.EnumerateWindows(includeHidden: true).GroupBy(w => w.Handle).Select(g => g.First()).ToArray();
        var entries = AllEntries().ToArray();
        var claimed = new HashSet<long>();
        var resolved = new HashSet<Guid>();
        // First reserve exact process-instance + HWND identities, so a similar title cannot steal them.
        foreach (var entry in entries)
        {
            var exact = candidates.FirstOrDefault(c => !claimed.Contains(c.Handle) && WindowMatcher.IsExactIdentity(entry, c));
            if (exact is null) continue;
            Associate(entry, exact, reassociated: false);
            claimed.Add(exact.Handle);
            resolved.Add(entry.Id);
        }
        var unmatched = entries.Where(e => !resolved.Contains(e.Id)).ToArray();
        var remaining = candidates.Where(c => !claimed.Contains(c.Handle) && !IsExcluded(c.Fingerprint)).ToArray();
        var proposals = unmatched.ToDictionary(e => e.Id,
            e => remaining.Where(c => WindowMatcher.IsConfidentMatch(e.Fingerprint, c.Fingerprint)).ToArray());
        foreach (var entry in unmatched)
        {
            var matches = proposals[entry.Id];
            if (matches.Length == 1 && proposals.Values.Count(m => m.Any(c => c.Handle == matches[0].Handle)) == 1)
                Associate(entry, matches[0], reassociated: true);
            else
            {
                var missingOwner = State.Sessions.First(s => s.Windows.Contains(entry));
                if (CanvasFor(missingOwner, entry.Id) is { } canvas) _pendingPaneCanvases.Add(canvas.Id);
                MarkMissing(entry, matches.Length > 0 ? "Matching windows are ambiguous. Add the intended window manually." : "Window unavailable. Its saved layout and membership are retained.");
            }
        }
        RepairPaneTrees();
    });

    public void TrackWindow(long handle, bool foreground)
    {
        if (_shuttingDown || handle == 0) return;
        var entry = AllEntries().FirstOrDefault(w => w.Handle == handle && !w.IsMissing);
        if (entry is null) return;
        try
        {
            if (!SameWindow(entry))
            {
                var missingOwner = State.Sessions.First(s => s.Windows.Contains(entry));
                if (CanvasFor(missingOwner, entry.Id) is { } canvas) _pendingPaneCanvases.Add(canvas.Id);
                MarkMissing(entry); RepairPaneTrees(); Dirty(); return;
            }
            var snapshot = Inspect(handle);
            if (snapshot is null) return;
            var owner = State.Sessions.First(s => s.Windows.Contains(entry));
            if (TrackPaneWindow(owner, entry, snapshot, foreground)) return;
            if (foreground && owner.Id == State.ActiveSessionId) entry.LastFocusedUtc = DateTime.UtcNow;
            var updated = foreground;
            if (snapshot.IsVisible && (owner.Id == State.ActiveSessionId || HidingPaused || IsExcluded(entry.Fingerprint)))
            {
                updated |= UpdateSnapshot(entry, snapshot);
                if (entry.HiddenByDeskMux) { entry.HiddenByDeskMux = false; updated = true; }
            }
            else if (snapshot.IsVisible && !HidingPaused)
            {
                // A managed app can show itself. Reapply inactive membership, without event feedback loops.
                if (!_lastRehideAttempt.TryGetValue(handle, out var last) || DateTime.UtcNow - last > TimeSpan.FromMilliseconds(750))
                {
                    _lastRehideAttempt[handle] = DateTime.UtcNow;
                    entry.HiddenByDeskMux = false;
                    Hide(entry);
                    updated = true;
                }
            }
            if (updated) Dirty();
        }
        catch (Exception ex) { Error("Could not track this window: " + ex.Message); Dirty(); }
    }

    public void ShowAll() => Change(() =>
    {
        HidingPaused = true;
        foreach (var canvas in State.Sessions.SelectMany(s => s.PaneCanvases)) canvas.ZoomedLeafId = null;
        CaptureVisible(AllEntries());
        foreach (var canvas in State.Sessions.SelectMany(s => s.PaneCanvases))
        {
            try
            {
                foreach (var (id, layout) in PaneTree.Calculate(canvas))
                    if (AllEntries().FirstOrDefault(w => w.Id == id) is { } pane) pane.Layout = layout;
            }
            catch (PaneLayoutException) { /* Recovery still shows every surviving application. */ }
        }
        foreach (var entry in AllEntries()) Show(entry);
        _log.Write("recovery.show_all", "All managed windows were requested visible; session hiding is paused.");
    });

    public void ResumeHiding() => Change(() =>
    {
        HidingPaused = false;
        ApplyVisibility();
        ReapplyVisiblePanes();
        RaiseActiveWindows();
        FocusActive();
    });

    public void Save()
    {
        try { _store.Save(State); HasUnsavedChanges = false; }
        catch (Exception ex)
        {
            HasUnsavedChanges = true;
            var message = "Could not save session data: " + ex.Message;
            var changed = LastError != message;
            Error(message);
            if (changed) Changed?.Invoke();
        }
    }

    public void Shutdown()
    {
        if (_shuttingDown) return;
        ShowAll();
        _shuttingDown = true;
        Save();
    }

    private void SwitchCore(Guid id)
    {
        if (FindSession(id) is null) { Error("This session no longer exists."); return; }
        if (ActiveSession is { } outgoing) CaptureVisible(outgoing.Windows);
        var old = State.ActiveSessionId;
        if (old != id)
        {
            State.PreviousSessionId = old ?? State.PreviousSessionId;
            State.ActiveSessionId = id;
        }
        ApplyVisibility(restoreMinimized: State.Settings.RestoreMinimizedOnSessionSwitch);
        ReapplyVisiblePanes();
        RaiseActiveWindows();
        FocusActive();
        _log.Write("session.switched", FindSession(id)!.Name, new { previous = old, current = id, HidingPaused });
    }

    private void ApplyVisibility(bool restoreMinimized = false)
    {
        foreach (var session in State.Sessions)
            foreach (var entry in session.Windows)
            {
                if (HidingPaused || (session.Id == State.ActiveSessionId && !IsZoomHidden(session, entry.Id)) || IsExcluded(entry.Fingerprint))
                    Show(entry, restoreMinimized && session.Id == State.ActiveSessionId && !IsExcluded(entry.Fingerprint));
                else Hide(entry);
            }
    }

    private bool Hide(ManagedWindow entry)
    {
        if (entry.IsMissing || HidingPaused || IsExcluded(entry.Fingerprint)) return false;
        if (!SameWindow(entry)) { MarkMissing(entry); return false; }
        var snapshot = Inspect(entry.Handle);
        if (snapshot is null) { MarkMissing(entry); return false; }
        if (!snapshot.IsVisible) return entry.HiddenByDeskMux;
        UpdateSnapshot(entry, snapshot);
        var result = Operate(() => _windows.Hide(entry));
        if (result.Success) { entry.HiddenByDeskMux = true; entry.Status = ""; }
        else WindowError(entry, "hide", result.Error);
        return result.Success;
    }

    private bool Show(ManagedWindow entry, bool restoreMinimized = false)
    {
        if (entry.IsMissing) return true;
        if (!SameWindow(entry)) { MarkMissing(entry); return true; }
        var savedLayout = entry.Layout;
        if (restoreMinimized && savedLayout.ShowState == WindowShowState.Minimized)
        {
            entry.Layout = MonitorMapper.Clone(savedLayout);
            entry.Layout.ShowState = savedLayout.RestoreToMaximized ? WindowShowState.Maximized : WindowShowState.Normal;
            entry.Layout.RestoreToMaximized = false;
        }
        var result = Operate(() => _windows.Show(entry));
        if (result.Success) { entry.HiddenByDeskMux = false; entry.Status = ""; }
        else { entry.Layout = savedLayout; WindowError(entry, "show", result.Error); }
        return result.Success;
    }

    public void RestoreActiveFocus() => FocusActive();

    private void FocusActive()
    {
        var entry = ActiveSession?.Windows.Where(w => !w.IsMissing && !w.HiddenByDeskMux && w.Layout.ShowState != WindowShowState.Minimized)
            .OrderByDescending(w => w.LastFocusedUtc).FirstOrDefault();
        if (entry is null || !SameWindow(entry)) return;
        var result = Operate(() => _windows.Focus(entry));
        if (!result.Success) WindowError(entry, "focus", result.Error);
    }

    private void RaiseActiveWindows()
    {
        if (HidingPaused || ActiveSession is not { } active) return;
        var entries = active.Windows.Where(w => !w.IsMissing && !w.HiddenByDeskMux && !IsExcluded(w.Fingerprint) &&
            w.Layout.ShowState != WindowShowState.Minimized && SameWindow(w)).OrderBy(w => w.LastFocusedUtc).ToArray();
        if (entries.Length == 0) return;
        IReadOnlyList<WindowOperationResult> results;
        try { results = _windows.BringToFront(entries); }
        catch (Exception ex) { Error("Could not bring this session forward: " + ex.Message); return; }
        for (var i = 0; i < entries.Length; i++)
        {
            var result = i < results.Count ? results[i] : WindowOperationResult.Fail("Windows returned an incomplete z-order result.");
            if (!result.Success) WindowError(entries[i], "bring forward", result.Error);
        }
    }

    private void CaptureVisible(IEnumerable<ManagedWindow> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsMissing || entry.HiddenByDeskMux) continue;
            if (!SameWindow(entry)) { MarkMissing(entry); continue; }
            var snapshot = Inspect(entry.Handle);
            if (snapshot?.IsVisible == true) UpdateSnapshot(entry, snapshot);
            if (_windows.ForegroundWindow == entry.Handle) entry.LastFocusedUtc = DateTime.UtcNow;
        }
    }

    private static bool UpdateSnapshot(ManagedWindow entry, WindowSnapshot snapshot)
    {
        var changed = entry.Layout.Bounds != snapshot.Layout.Bounds || entry.Layout.ShowState != snapshot.Layout.ShowState ||
            entry.Layout.MonitorDevice != snapshot.Layout.MonitorDevice || entry.Layout.MonitorId != snapshot.Layout.MonitorId || entry.Layout.MonitorWorkArea != snapshot.Layout.MonitorWorkArea ||
            entry.Layout.Dpi != snapshot.Layout.Dpi || entry.Layout.RestoreToMaximized != snapshot.Layout.RestoreToMaximized ||
            entry.Fingerprint.Title != snapshot.Fingerprint.Title;
        entry.Layout = MonitorMapper.Clone(snapshot.Layout);
        entry.Fingerprint = Clone(snapshot.Fingerprint);
        return changed;
    }

    private void Associate(ManagedWindow entry, WindowSnapshot snapshot, bool reassociated)
    {
        var sameHiddenWindow = !reassociated && entry.HiddenByDeskMux && !snapshot.IsVisible;
        entry.Handle = snapshot.Handle;
        entry.Fingerprint = Clone(snapshot.Fingerprint);
        entry.IsMissing = false;
        entry.HiddenByDeskMux = sameHiddenWindow;
        entry.Status = IsExcluded(entry.Fingerprint) ? "Excluded application; session hiding is disabled for this window." : "";
        if (reassociated) _log.Write("window.reassociated", entry.DisplayTitle, new { entry.Id, entry.Handle });
    }

    private static void MarkMissing(ManagedWindow entry, string? status = null)
    {
        entry.IsMissing = true;
        entry.Handle = 0;
        entry.HiddenByDeskMux = false;
        entry.Status = status ?? "Window unavailable. Its saved layout and membership are retained.";
    }

    private bool IsExcluded(WindowFingerprint fingerprint) => State.Settings.ExcludedProcesses.Any(exclusion =>
        string.Equals(WindowMatcher.NormalizeProcess(exclusion), WindowMatcher.NormalizeProcess(fingerprint.ProcessName), StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(fingerprint.ExecutablePath) && string.Equals(exclusion.Trim().Trim('"'), fingerprint.ExecutablePath, StringComparison.OrdinalIgnoreCase)));

    private bool SameWindow(ManagedWindow entry)
    {
        try { return _windows.IsSameWindow(entry); }
        catch (Exception ex) { _log.Write("window.validate_failed", ex.Message, new { entry.Handle }); return false; }
    }

    private WindowSnapshot? Inspect(long handle)
    {
        try { return _windows.Inspect(handle); }
        catch (Exception ex) { _log.Write("window.inspect_failed", ex.Message, new { handle }); return null; }
    }

    private static WindowOperationResult Operate(Func<WindowOperationResult> action)
    {
        try { return action(); }
        catch (Exception ex) { return WindowOperationResult.Fail(ex.Message); }
    }

    private void WindowError(ManagedWindow entry, string operation, string? error)
    {
        var message = $"Could not {operation} {entry.DisplayTitle}: {error ?? "Windows rejected the request."}";
        if (entry.Status != message) _log.Write($"window.{operation}_failed", message, new { entry.Id, entry.Handle });
        entry.Status = message;
        LastError = message;
    }

    private void Change(Action change)
    {
        LastError = null;
        try { change(); }
        catch (Exception ex) { Error("DeskMux could not complete this action: " + ex.Message); }
        HasUnsavedChanges = true;
        Save();
        Changed?.Invoke();
    }

    private void Dirty() { HasUnsavedChanges = true; Changed?.Invoke(); }
    private void Error(string message) { LastError = message; _log.Write("operation.failed", message); }
    private IEnumerable<ManagedWindow> AllEntries() => State.Sessions.SelectMany(s => s.Windows);
    private WorkspaceSession? FindSession(Guid id) => State.Sessions.FirstOrDefault(s => s.Id == id);
    private static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? "New session" : name.Trim()[..Math.Min(100, name.Trim().Length)];
    private static WindowFingerprint Clone(WindowFingerprint source) => new()
    {
        ProcessId = source.ProcessId, ProcessStartTimeUtcTicks = source.ProcessStartTimeUtcTicks,
        ExecutablePath = source.ExecutablePath, ProcessName = source.ProcessName, WindowClass = source.WindowClass, Title = source.Title
    };
}
