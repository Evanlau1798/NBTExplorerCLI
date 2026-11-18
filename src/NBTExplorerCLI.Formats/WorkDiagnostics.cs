using System.Diagnostics;

namespace NBTExplorerCLI.Formats;

public static class WorkDiagnostics
{
    public const string SourceName = "nbtcli.work";
    private static readonly ActivitySource Source = new(SourceName);
    public static bool IsEnabled => Source.HasListeners();

    public static Activity? Start(string operation, string? artifact = null, string? phase = null, Guid? worldId = null)
    {
        var activity = Source.StartActivity(operation);
        if (activity is not null)
        {
            activity.SetTag("artifact", artifact);
            activity.SetTag("phase", phase);
            if (worldId is { } id) activity.SetTag("world", id.ToString("D"));
        }
        return activity;
    }
}
