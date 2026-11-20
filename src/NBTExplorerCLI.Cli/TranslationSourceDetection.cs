using System.Runtime.InteropServices;
using System.Text;
using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Cli.TranslationLanguages;

namespace NBTExplorerCLI.Cli;

internal static class TranslationSourceDetection
{
    internal static string Resolve(IReadOnlyList<TextOccurrence> selected)
    {
        var resource = selected.Select(item => TranslationLanguages.ResourceSource(item.Address.Artifact))
            .Distinct(StringComparer.Ordinal).Take(2).ToArray();
        if (resource.Length == 1 && resource[0] is { } tagged) return tagged;
        if (resource.Any(value => value is not null)) return Undetermined;
        return Detect(selected.Select(item => item.Text));
    }

    internal static string Detect(IEnumerable<string> values)
    {
        if (!OperatingSystem.IsWindows()) return Undetermined;
        const int maximumSamples = 12;
        var samples = values.Select(Sample).OfType<string>().Distinct(StringComparer.Ordinal).Take(maximumSamples + 1).ToArray();
        if (samples.Length == 0 || samples.Length > maximumSamples) return Undetermined;
        try
        {
            var guid = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.StructureToPtr(new Guid("cf7e00b1-909b-4d95-a8f4-611f7c377702"), guid, false);
                var options = new EnumOptions { Size = (nuint)Marshal.SizeOf<EnumOptions>(), Guid = guid };
                if (MappingGetServices(in options, out var service, out var count) != 0) return Undetermined;
                try
                {
                    if (count != 1 || service == 0) return Undetermined;
                    var detected = samples.Select(sample => Recognize(service, sample)).Distinct(StringComparer.Ordinal).Take(2).ToArray();
                    return detected.Length == 1 ? detected[0] : Undetermined;
                }
                finally { _ = MappingFreeServices(service); }
            }
            finally { Marshal.FreeHGlobal(guid); }
        }
        catch (DllNotFoundException) { return Undetermined; }
        catch (EntryPointNotFoundException) { return Undetermined; }
    }

    private static string? Sample(string text)
    {
        var span = text.AsSpan(0, Math.Min(text.Length, 2048));
        var sample = new StringBuilder(span.Length);
        var letters = 0;
        while (!span.IsEmpty)
        {
            if (span[0] == '§' && span.Length > 1) { span = span[2..]; continue; }
            _ = Rune.DecodeFromUtf16(span, out var unit, out var used);
            span = span[used..];
            if (Rune.IsLetter(unit)) { sample.Append(unit); letters++; }
            else if (sample.Length != 0 && sample[^1] != ' ') sample.Append(' ');
        }
        return letters >= 20 ? sample.ToString().Normalize() : null;
    }

    private static string Recognize(nint service, string text)
    {
        // ELS can retain the input until the property bag is released.
        var pinned = GCHandle.Alloc(text, GCHandleType.Pinned);
        try
        {
            var bag = new PropertyBag { Size = (nuint)Marshal.SizeOf<PropertyBag>() };
            if (MappingRecognizeText(service, pinned.AddrOfPinnedObject(), (uint)text.Length, 0, 0, ref bag) != 0) return Undetermined;
            try
            {
                if (bag.Count != 1 || bag.Ranges == 0) return Undetermined;
                var range = Marshal.PtrToStructure<DataRange>(bag.Ranges);
                if (range.Data == 0 || range.Bytes is < 4 or > 4096 || range.Bytes % 2 != 0) return Undetermined;
                var result = Marshal.PtrToStringUni(range.Data, (int)range.Bytes / 2)!;
                var end = result.IndexOf('\0');
                return end > 0 ? Concrete(result[..end]) ?? Undetermined : Undetermined;
            }
            finally { _ = MappingFreePropertyBag(ref bag); }
        }
        finally { pinned.Free(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumOptions
    {
        internal nuint Size;
        internal nint Category, InputLanguage, OutputLanguage, InputScript, OutputScript, InputContentType, OutputContentType, Guid;
        internal uint Options;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyBag
    {
        internal nuint Size;
        internal nint Ranges;
        internal uint Count;
        internal nint ServiceData;
        internal uint ServiceDataSize;
        internal nint CallerData;
        internal uint CallerDataSize;
        internal nint Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataRange
    {
        internal uint Start, End;
        internal nint Description;
        internal uint DescriptionLength;
        internal nint Data;
        internal uint Bytes;
        internal nint ContentType, ActionIds;
        internal uint ActionCount;
        internal nint ActionDisplayNames;
    }

    [DllImport("elscore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MappingGetServices(in EnumOptions options, out nint services, out uint count);
    [DllImport("elscore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MappingFreeServices(nint services);
    [DllImport("elscore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MappingRecognizeText(nint service, nint text, uint count, uint index, nint options, ref PropertyBag bag);
    [DllImport("elscore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MappingFreePropertyBag(ref PropertyBag bag);
}
