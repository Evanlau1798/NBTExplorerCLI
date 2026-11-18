namespace NBTExplorerCLI.Domain;

public sealed record PackFormatVersion : IComparable<PackFormatVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public PackFormatVersion(int major, int minor)
    {
        if (major < 0 || minor < 0) throw new ArgumentOutOfRangeException(nameof(major));
        (Major, Minor) = (major, minor);
    }
    public int CompareTo(PackFormatVersion? other) => other is null ? 1 : Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);
}
