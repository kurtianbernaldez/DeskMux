namespace DeskMux.Core;

/// <summary>Deliberately conservative window identity matching. A PID or title alone is never identity.</summary>
public static class WindowMatcher
{
    public static bool IsExactIdentity(ManagedWindow saved, WindowSnapshot candidate) =>
        saved.Handle != 0 && saved.Handle == candidate.Handle &&
        saved.Fingerprint.ProcessId > 0 && saved.Fingerprint.ProcessId == candidate.Fingerprint.ProcessId &&
        saved.Fingerprint.ProcessStartTimeUtcTicks > 0 &&
        saved.Fingerprint.ProcessStartTimeUtcTicks == candidate.Fingerprint.ProcessStartTimeUtcTicks &&
        SameApplication(saved.Fingerprint, candidate.Fingerprint) &&
        SameClass(saved.Fingerprint, candidate.Fingerprint);

    public static bool IsConfidentMatch(WindowFingerprint saved, WindowFingerprint candidate) =>
        SameApplication(saved, candidate) && SameClass(saved, candidate) &&
        !string.IsNullOrWhiteSpace(saved.Title) && string.Equals(saved.Title, candidate.Title, StringComparison.Ordinal);

    public static WindowSnapshot? FindUniqueMatch(ManagedWindow saved, IReadOnlyList<WindowSnapshot> candidates)
    {
        var exact = candidates.Where(c => IsExactIdentity(saved, c)).Take(2).ToArray();
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1) return null;
        var matches = candidates.Where(c => IsConfidentMatch(saved.Fingerprint, c.Fingerprint)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static bool SameApplication(WindowFingerprint left, WindowFingerprint right)
    {
        if (!string.IsNullOrWhiteSpace(left.ExecutablePath) && !string.IsNullOrWhiteSpace(right.ExecutablePath))
            return string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(left.ProcessName) &&
            string.Equals(NormalizeProcess(left.ProcessName), NormalizeProcess(right.ProcessName), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeProcess(string process)
    {
        var name = process.Trim().Trim('"').Replace('\\', '/').Split('/').Last();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool SameClass(WindowFingerprint left, WindowFingerprint right) =>
        !string.IsNullOrWhiteSpace(left.WindowClass) && string.Equals(left.WindowClass, right.WindowClass, StringComparison.Ordinal);
}
