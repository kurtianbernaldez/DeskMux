namespace DeskMux.Core;

public enum PaneOrientation { Vertical, Horizontal }
public enum PaneDirection { Left, Right, Up, Down }

/// <summary>A persisted physical-pixel canvas. Nodes use IDs so malformed JSON cannot create object cycles.</summary>
public sealed class PaneCanvas
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<Guid, PaneMinimumSize> MinimumSizes { get; set; } = [];
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MonitorDevice { get; set; } = "";
    public string MonitorId { get; set; } = "";
    public PixelRect MonitorWorkArea { get; set; } = new(0, 0, 1920, 1080);
    public uint Dpi { get; set; } = 96;
    public Guid? RootNodeId { get; set; }
    public List<PaneNode> Nodes { get; set; } = [];
    public Guid? ZoomedLeafId { get; set; }
}

public sealed class PaneNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WindowId { get; set; }
    public PaneOrientation Orientation { get; set; }
    public double Ratio { get; set; } = .5;
    public Guid? FirstChildId { get; set; }
    public Guid? SecondChildId { get; set; }
    public bool IsLeaf => WindowId.HasValue;
}

public sealed class PaneLayoutException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>Deterministic tree editing and geometry, independent from native windows and UI.</summary>
public static class PaneTree
{
    public const int MinimumWidth = 160;
    public const int MinimumHeight = 100;
    private const int MaximumDepth = 256;

    public static PaneCanvas Clone(PaneCanvas canvas) => new()
    {
        Id = canvas.Id, MonitorDevice = canvas.MonitorDevice, MonitorId = canvas.MonitorId,
        MonitorWorkArea = canvas.MonitorWorkArea with { }, Dpi = canvas.Dpi,
        RootNodeId = canvas.RootNodeId, ZoomedLeafId = canvas.ZoomedLeafId,
        Nodes = canvas.Nodes.Select(CloneNode).ToList(), MinimumSizes = new(canvas.MinimumSizes)
    };

    private static PaneNode CloneNode(PaneNode node) => new()
    {
        Id = node.Id, WindowId = node.WindowId, Orientation = node.Orientation, Ratio = node.Ratio,
        FirstChildId = node.FirstChildId, SecondChildId = node.SecondChildId
    };

    /// <summary>Repairs malformed trees in depth-first order; the first valid occurrence of a window wins.</summary>
    public static bool Validate(PaneCanvas canvas, ISet<Guid> validWindowIds, ISet<Guid>? usedWindowIds = null)
    {
        var changed = false;
        if (canvas.Nodes is null) { canvas.Nodes = []; changed = true; }
        if (canvas.MonitorWorkArea is null) { canvas.MonitorWorkArea = new(0, 0, 1920, 1080); changed = true; }
        canvas.MonitorDevice ??= "";
        canvas.MonitorId ??= "";
        if (canvas.Dpi == 0) { canvas.Dpi = 96; changed = true; }
        var map = new Dictionary<Guid, PaneNode>();
        foreach (var node in canvas.Nodes)
        {
            if (node is null || node.Id == Guid.Empty || !map.TryAdd(node.Id, node)) changed = true;
        }
        var seen = new HashSet<Guid>();
        var used = usedWindowIds ?? new HashSet<Guid>();
        var kept = new List<PaneNode>();
        Guid? Visit(Guid? id, int depth)
        {
            if (id is not { } key) return null;
            if (depth > MaximumDepth || !map.TryGetValue(key, out var node) || !seen.Add(key)) { changed = true; return null; }
            if (node.WindowId is { } windowId)
            {
                if (windowId == Guid.Empty || !validWindowIds.Contains(windowId) || !used.Add(windowId)) { changed = true; return null; }
                if (node.FirstChildId is not null || node.SecondChildId is not null) changed = true;
                node.FirstChildId = node.SecondChildId = null;
                if (!double.IsFinite(node.Ratio) || node.Ratio != .5) { node.Ratio = .5; changed = true; }
                if (!Enum.IsDefined(node.Orientation)) { node.Orientation = PaneOrientation.Vertical; changed = true; }
                kept.Add(node);
                return key;
            }
            var first = Visit(node.FirstChildId, depth + 1);
            var second = Visit(node.SecondChildId, depth + 1);
            if (first is null || second is null) { changed = true; return first ?? second; }
            if (first != node.FirstChildId || second != node.SecondChildId) changed = true;
            node.FirstChildId = first; node.SecondChildId = second;
            var ratio = ClampRatio(node.Ratio);
            if (node.Ratio != ratio) { node.Ratio = ratio; changed = true; }
            if (!Enum.IsDefined(node.Orientation)) { node.Orientation = PaneOrientation.Vertical; changed = true; }
            kept.Add(node);
            return key;
        }
        var root = Visit(canvas.RootNodeId, 0);
        if (root != canvas.RootNodeId || kept.Count != canvas.Nodes.Count) changed = true;
        canvas.RootNodeId = root;
        // Retain serialization order when possible; leaf order is always derived from the actual tree.
        var retained = kept.Select(n => n.Id).ToHashSet();
        canvas.Nodes = canvas.Nodes.Where(n => n is not null && retained.Remove(n.Id)).ToList();
        if (canvas.ZoomedLeafId is { } zoom && !canvas.Nodes.Any(n => n.Id == zoom && n.IsLeaf))
        { canvas.ZoomedLeafId = null; changed = true; }
        return changed;
    }

    public static IReadOnlyList<PaneNode> Leaves(PaneCanvas canvas)
    {
        var map = NodeMap(canvas);
        var seen = new HashSet<Guid>();
        var leaves = new List<PaneNode>();
        void Visit(Guid? id, int depth)
        {
            if (id is not { } key) return;
            if (depth > MaximumDepth || !map.TryGetValue(key, out var node) || !seen.Add(key)) throw InvalidTree();
            if (node.IsLeaf) leaves.Add(node);
            else { Visit(node.FirstChildId, depth + 1); Visit(node.SecondChildId, depth + 1); }
        }
        Visit(canvas.RootNodeId, 0);
        return leaves;
    }

    public static PaneNode? FindLeaf(PaneCanvas canvas, Guid windowId) => Leaves(canvas).FirstOrDefault(n => n.WindowId == windowId);

    /// <summary>Adds a root or replaces a source leaf with a split. Rejects an impossible split without changing the canvas.</summary>
    public static PaneNode Split(PaneCanvas canvas, Guid? sourceWindowId, Guid targetWindowId, PaneOrientation orientation, bool entireCanvas = false)
    {
        if (targetWindowId == Guid.Empty || !Enum.IsDefined(orientation)) throw InvalidTree();
        if (FindLeaf(canvas, targetWindowId) is not null) throw new PaneLayoutException("DuplicateWindow", "That window already occupies a pane.");
        var draft = Clone(canvas);
        var source = sourceWindowId is { } sourceId ? FindLeaf(draft, sourceId) : null;
        if (source is null && draft.RootNodeId is not null && !entireCanvas) throw new PaneLayoutException("SourceMissing", "The source pane is no longer available.");
        if (source is null && draft.RootNodeId is null && sourceWindowId is { } floatingId && floatingId != Guid.Empty && floatingId != targetWindowId)
        {
            source = new PaneNode { WindowId = floatingId };
            draft.Nodes.Add(source); draft.RootNodeId = source.Id;
        }
        var target = new PaneNode { WindowId = targetWindowId };
        draft.Nodes.Add(target);
        var splitSource = entireCanvas ? draft.RootNodeId : source?.Id;
        if (splitSource is null) draft.RootNodeId = target.Id;
        else
        {
            var split = new PaneNode { Orientation = orientation, Ratio = .5, FirstChildId = splitSource, SecondChildId = target.Id };
            ReplaceReference(draft, splitSource.Value, split.Id);
            draft.Nodes.Add(split);
        }
        draft.ZoomedLeafId = null;
        Calculate(draft);
        CommitTree(canvas, draft);
        return canvas.Nodes.Single(n => n.Id == target.Id);
    }

    public static bool Remove(PaneCanvas canvas, Guid windowId)
    {
        var leaf = FindLeaf(canvas, windowId);
        if (leaf is null) return false;
        var valid = Leaves(canvas).Where(n => n.WindowId != windowId).Select(n => n.WindowId!.Value).ToHashSet();
        Validate(canvas, valid);
        return true;
    }

    public static bool Swap(PaneCanvas canvas, Guid windowId, int offset)
    {
        var leaves = Leaves(canvas);
        var index = leaves.ToList().FindIndex(n => n.WindowId == windowId);
        if (index < 0 || leaves.Count < 2 || offset == 0) return false;
        var other = (int)(((long)index + offset) % leaves.Count);
        if (other < 0) other += leaves.Count;
        if (other == index) return false;
        (leaves[index].WindowId, leaves[other].WindowId) = (leaves[other].WindowId, leaves[index].WindowId);
        canvas.ZoomedLeafId = null;
        return true;
    }

    /// <summary>Moves the nearest matching divider in the requested direction by five percentage points.</summary>
    public static bool Resize(PaneCanvas canvas, Guid windowId, PaneDirection direction, double step = .05)
    {
        if (!Enum.IsDefined(direction) || !double.IsFinite(step) || step <= 0) return false;
        var leaf = FindLeaf(canvas, windowId);
        if (leaf is null) return false;
        var orientation = direction is PaneDirection.Left or PaneDirection.Right ? PaneOrientation.Vertical : PaneOrientation.Horizontal;
        var path = Ancestors(canvas, leaf.Id);
        var split = path.LastOrDefault(n => n.Orientation == orientation);
        if (split is null) return false;
        var geometry = CalculateNodes(canvas);
        var map = NodeMap(canvas);
        var minima = ComputeMinima(canvas, map);
        var rect = geometry[split.Id];
        var total = orientation == PaneOrientation.Vertical ? rect.Width : rect.Height;
        var a = minima[split.FirstChildId!.Value]; var b = minima[split.SecondChildId!.Value];
        var minimum = orientation == PaneOrientation.Vertical ? a.Width : a.Height;
        var maximum = total - (orientation == PaneOrientation.Vertical ? b.Width : b.Height);
        var effective = Math.Clamp((int)Math.Floor(total * ClampRatio(split.Ratio)), minimum, maximum) / (double)total;
        var sign = direction is PaneDirection.Left or PaneDirection.Up ? -1 : 1;
        var ratio = Math.Clamp(effective + sign * step, minimum / (double)total, maximum / (double)total);
        if (Math.Abs(ratio - effective) < .00000001) return false;
        split.Ratio = ratio;
        canvas.ZoomedLeafId = null;
        return true;
    }

    /// <summary>Moves shared dividers touched by a native edge resize; outer canvas edges stay fixed.</summary>
    public static bool ResizeToBounds(PaneCanvas canvas, Guid windowId, PixelRect requested)
    {
        var leaf = FindLeaf(canvas, windowId);
        if (leaf is null) return false;
        var geometry = CalculateNodes(canvas);
        var original = geometry[leaf.Id];
        if (original.Width == requested.Width && original.Height == requested.Height) return false;
        var path = Ancestors(canvas, leaf.Id).Reverse().ToArray();
        var changed = false;
        foreach (var edge in new[] {
            (PaneOrientation.Vertical, original.X, requested.X),
            (PaneOrientation.Vertical, original.X + original.Width, requested.X + requested.Width),
            (PaneOrientation.Horizontal, original.Y, requested.Y),
            (PaneOrientation.Horizontal, original.Y + original.Height, requested.Y + requested.Height) })
        {
            var (axis, before, after) = edge;
            if (before == after) continue;
            var split = path.FirstOrDefault(n => n.Orientation == axis &&
                (axis == PaneOrientation.Vertical ? geometry[n.SecondChildId!.Value].X : geometry[n.SecondChildId!.Value].Y) == before);
            if (split is null) continue;
            var rect = geometry[split.Id];
            var total = axis == PaneOrientation.Vertical ? rect.Width : rect.Height;
            split.Ratio = Math.Clamp((after - (double)(axis == PaneOrientation.Vertical ? rect.X : rect.Y)) / total, 0, 1);
            changed = true;
        }
        return changed;
    }

    /// <summary>Returns full, unzoomed layouts without mutating the tree or saved ratios.</summary>
    public static IReadOnlyDictionary<Guid, WindowLayout> Calculate(PaneCanvas canvas)
    {
        var geometry = CalculateNodes(canvas);
        var layouts = new Dictionary<Guid, WindowLayout>();
        foreach (var leaf in Leaves(canvas))
        {
            var layout = new WindowLayout
            {
                Bounds = geometry[leaf.Id], ShowState = WindowShowState.Normal, UseVisibleFrameBounds = true,
                MonitorDevice = canvas.MonitorDevice, MonitorId = canvas.MonitorId,
                MonitorWorkArea = canvas.MonitorWorkArea with { }, Dpi = canvas.Dpi > 0 ? canvas.Dpi : 96
            };
            if (!layouts.TryAdd(leaf.WindowId!.Value, layout)) throw new PaneLayoutException("DuplicateWindow", "A window occurs more than once in this pane tree.");
        }
        return layouts;
    }

    public static Guid? Neighbor(PaneCanvas canvas, Guid windowId, PaneDirection direction, IReadOnlyDictionary<Guid, DateTime>? focusHistory = null)
    {
        var layouts = Calculate(canvas);
        if (!Enum.IsDefined(direction) || !layouts.TryGetValue(windowId, out var current)) return null;
        var horizontal = direction is PaneDirection.Left or PaneDirection.Right;
        var forward = direction is PaneDirection.Right or PaneDirection.Down;
        var c = current.Bounds;
        var currentStart = horizontal ? c.X : c.Y;
        var currentEnd = (long)currentStart + (horizontal ? c.Width : c.Height);
        var perpendicularStart = horizontal ? c.Y : c.X;
        var perpendicularEnd = (long)perpendicularStart + (horizontal ? c.Height : c.Width);
        var order = Leaves(canvas).Select((n, index) => (n.WindowId!.Value, index)).ToDictionary(p => p.Value, p => p.index);
        return layouts.Where(p => p.Key != windowId).Select(p =>
        {
            var b = p.Value.Bounds;
            var start = horizontal ? b.X : b.Y;
            var end = (long)start + (horizontal ? b.Width : b.Height);
            var perpendicular = horizontal ? b.Y : b.X;
            var perpendicularLimit = (long)perpendicular + (horizontal ? b.Height : b.Width);
            var isDirection = forward ? start >= currentEnd : end <= currentStart;
            var overlap = Math.Min(perpendicularLimit, perpendicularEnd) - Math.Max(perpendicular, perpendicularStart);
            var distance = forward ? (long)start - currentEnd : currentStart - end;
            var sideDistance = Math.Max(0, Math.Max(perpendicular - perpendicularEnd, perpendicularStart - perpendicularLimit));
            return new { p.Key, isDirection, overlap, distance, sideDistance };
        }).Where(p => p.isDirection)
            .OrderByDescending(p => p.overlap > 0)
            .ThenBy(p => p.distance)
            .ThenBy(p => p.sideDistance)
            .ThenByDescending(p => focusHistory?.GetValueOrDefault(p.Key) ?? DateTime.MinValue)
            .ThenBy(p => order[p.Key])
            .Select(p => (Guid?)p.Key).FirstOrDefault();
    }

    public static IReadOnlyList<PaneNode> Ancestors(PaneCanvas canvas, Guid leafId)
    {
        var map = NodeMap(canvas);
        var path = new List<PaneNode>();
        var seen = new HashSet<Guid>();
        bool Visit(Guid? id, int depth)
        {
            if (id is not { } key) return false;
            if (depth > MaximumDepth || !map.TryGetValue(key, out var node) || !seen.Add(key)) throw InvalidTree();
            if (key == leafId) return true;
            if (node.IsLeaf) return false;
            path.Add(node);
            if (Visit(node.FirstChildId, depth + 1) || Visit(node.SecondChildId, depth + 1)) return true;
            path.RemoveAt(path.Count - 1);
            return false;
        }
        if (!Visit(canvas.RootNodeId, 0)) path.Clear();
        return path;
    }

    public static Dictionary<Guid, PixelRect> CalculateNodes(PaneCanvas canvas)
    {
        var result = new Dictionary<Guid, PixelRect>();
        if (canvas.RootNodeId is null) return result;
        var work = canvas.MonitorWorkArea;
        if (work is null || work.Width <= 0 || work.Height <= 0 || (long)work.X + work.Width > int.MaxValue || (long)work.Y + work.Height > int.MaxValue)
            throw new PaneLayoutException("InvalidCanvas", "The monitor work area is not valid.");
        var map = NodeMap(canvas);
        var minima = ComputeMinima(canvas, map);
        var rootMinimum = minima[canvas.RootNodeId.Value];
        if (work.Width < rootMinimum.Width || work.Height < rootMinimum.Height)
            throw new PaneLayoutException("MinimumSize", $"This layout needs at least {rootMinimum.Width} × {rootMinimum.Height} pixels. Release a pane or use a larger monitor.");
        void Visit(Guid id, PixelRect rect)
        {
            result.Add(id, rect);
            var node = map[id];
            if (node.IsLeaf) return;
            var firstId = node.FirstChildId!.Value; var secondId = node.SecondChildId!.Value;
            var a = minima[firstId]; var b = minima[secondId];
            if (node.Orientation == PaneOrientation.Vertical)
            {
                var width = Math.Clamp((int)Math.Floor(rect.Width * ClampRatio(node.Ratio)), a.Width, rect.Width - b.Width);
                Visit(firstId, new(rect.X, rect.Y, width, rect.Height));
                Visit(secondId, new(rect.X + width, rect.Y, rect.Width - width, rect.Height));
            }
            else
            {
                var height = Math.Clamp((int)Math.Floor(rect.Height * ClampRatio(node.Ratio)), a.Height, rect.Height - b.Height);
                Visit(firstId, new(rect.X, rect.Y, rect.Width, height));
                Visit(secondId, new(rect.X, rect.Y + height, rect.Width, rect.Height - height));
            }
        }
        Visit(canvas.RootNodeId.Value, work);
        return result;
    }

    private static Dictionary<Guid, (int Width, int Height)> ComputeMinima(PaneCanvas canvas, Dictionary<Guid, PaneNode> map)
    {
        var result = new Dictionary<Guid, (int Width, int Height)>();
        var seen = new HashSet<Guid>();
        (int Width, int Height) Visit(Guid? id, int depth)
        {
            if (id is not { } key || depth > MaximumDepth || !map.TryGetValue(key, out var node) || !seen.Add(key)) throw InvalidTree();
            (int Width, int Height) size;
            if (node.IsLeaf)
            {
                if (node.WindowId == Guid.Empty || node.FirstChildId.HasValue || node.SecondChildId.HasValue) throw InvalidTree();
                var minimum = canvas.MinimumSizes.GetValueOrDefault(node.WindowId!.Value);
                size = (Math.Max(MinimumWidth, minimum?.Width ?? 0), Math.Max(MinimumHeight, minimum?.Height ?? 0));
            }
            else
            {
                if (!Enum.IsDefined(node.Orientation)) throw InvalidTree();
                var a = Visit(node.FirstChildId, depth + 1); var b = Visit(node.SecondChildId, depth + 1);
                var width = node.Orientation == PaneOrientation.Vertical ? (long)a.Width + b.Width : Math.Max(a.Width, b.Width);
                var height = node.Orientation == PaneOrientation.Horizontal ? (long)a.Height + b.Height : Math.Max(a.Height, b.Height);
                if (width > int.MaxValue || height > int.MaxValue) throw new PaneLayoutException("MinimumSize", "The pane tree is too large for a monitor.");
                size = ((int)width, (int)height);
            }
            result.Add(key, size);
            return size;
        }
        if (canvas.RootNodeId is { } root) Visit(root, 0);
        return result;
    }

    private static Dictionary<Guid, PaneNode> NodeMap(PaneCanvas canvas)
    {
        var map = new Dictionary<Guid, PaneNode>();
        foreach (var node in canvas.Nodes)
            if (node is null || node.Id == Guid.Empty || !map.TryAdd(node.Id, node)) throw InvalidTree();
        return map;
    }

    private static void ReplaceReference(PaneCanvas canvas, Guid oldId, Guid newId)
    {
        if (canvas.RootNodeId == oldId) { canvas.RootNodeId = newId; return; }
        foreach (var node in canvas.Nodes)
        {
            if (node.FirstChildId == oldId) { node.FirstChildId = newId; return; }
            if (node.SecondChildId == oldId) { node.SecondChildId = newId; return; }
        }
        throw InvalidTree();
    }

    private static void CommitTree(PaneCanvas destination, PaneCanvas source)
    {
        destination.RootNodeId = source.RootNodeId;
        destination.Nodes = source.Nodes;
        destination.ZoomedLeafId = source.ZoomedLeafId;
    }

    // Actual limits depend on the subtree's physical minimum size, including nested splits.
    private static double ClampRatio(double ratio) => double.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : .5;
    private static PaneLayoutException InvalidTree() => new("InvalidTree", "The pane tree contains an invalid or repeated node. Repair the saved tree before arranging it.");
}
