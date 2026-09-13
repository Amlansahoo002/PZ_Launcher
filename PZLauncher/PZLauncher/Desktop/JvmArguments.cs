namespace PZLauncher.Desktop;

internal static class JvmArguments
{
    internal static readonly string[] Bases = ["launcher", "game", "custom"];
    internal static List<string> Parse(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    internal static void Validate(string argumentBase, IReadOnlyList<string> arguments)
    {
        if (!Bases.Contains(argumentBase) || arguments == null || arguments.Count > 128) throw new InvalidDataException(T("jvm.extraInvalid"));
        foreach (string arg in arguments)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.Length > 4096 || arg.Any(char.IsControl)
                || !(arg.StartsWith("-D") && arg.Contains('=') || arg.StartsWith("-XX:") || arg.StartsWith("-Xlog:") || arg is "-Xverify:all" or "-Xverify:remote")
                || arg.StartsWith("-Dzomboid.steam=") || arg.StartsWith("-Dzomboid.znetlog=") || arg.StartsWith("-Dzomboid.ConsoleDotTxtSizeKB=")
                || arg.StartsWith("-XX:MaxGCPauseMillis=") || arg.Contains("UseStringDeduplication")
                || arg.StartsWith("-XX:MaxHeapSize=") || arg.StartsWith("-XX:InitialHeapSize=") || arg.StartsWith("-XX:ThreadStackSize=")
                || (arg.StartsWith("-XX:+Use") || arg.StartsWith("-XX:-Use")) && arg.EndsWith("GC"))
                throw new InvalidDataException(T("jvm.extraInvalid") + "\n" + arg);
        }
    }
    internal static void ApplySuggestion(PlayerProfile profile, JvmSuggestion suggestion)
    {
        profile.InitialMemoryMb = suggestion.XmsMb; profile.MemoryMb = suggestion.XmxMb;
        profile.StackKb = suggestion.StackKb; profile.PauseTargetMs = suggestion.PauseTargetMs;
        profile.Collector = suggestion.Collector; profile.StringDeduplication = suggestion.StringDeduplication;
    }
}
