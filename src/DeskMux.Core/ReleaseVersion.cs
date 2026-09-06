namespace DeskMux.Core;

public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().TrimStart('v', 'V');
        var build = text.IndexOf('+');
        if (build >= 0) text = text[..build];
        var dash = text.IndexOf('-');
        var prerelease = dash >= 0 ? text[(dash + 1)..] : "";
        var numbers = (dash >= 0 ? text[..dash] : text).Split('.');
        if (numbers.Length != 3 || !int.TryParse(numbers[0], out var major) || !int.TryParse(numbers[1], out var minor) || !int.TryParse(numbers[2], out var patch)) return false;
        version = new ReleaseVersion(major, minor, patch, prerelease); return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var number = Major.CompareTo(other.Major); if (number != 0) return number;
        number = Minor.CompareTo(other.Minor); if (number != 0) return number;
        number = Patch.CompareTo(other.Patch); if (number != 0) return number;
        if (Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : 1;
        if (other.Prerelease.Length == 0) return -1;
        var left = Prerelease.Split('.'); var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index == left.Length) return -1;
            if (index == right.Length) return 1;
            var leftNumber = int.TryParse(left[index], out var ln);
            var rightNumber = int.TryParse(right[index], out var rn);
            if (leftNumber && rightNumber) { number = ln.CompareTo(rn); if (number != 0) return number; continue; }
            if (leftNumber != rightNumber) return leftNumber ? -1 : 1;
            number = string.CompareOrdinal(left[index], right[index]); if (number != 0) return number;
        }
        return 0;
    }
}
