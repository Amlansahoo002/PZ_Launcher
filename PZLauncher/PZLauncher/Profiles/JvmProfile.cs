namespace PZLauncher.Profiles
{
    internal sealed class JvmProfile
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool SteamEnabled { get; init; } = true;
        public bool ZNetLogEnabled { get; init; }
        public bool DebugEnabled { get; init; }
        public bool NoVoip { get; init; }
        public bool UseZgc { get; init; }
        public bool UseOptimizedG1 { get; init; }
        public int XmsMb { get; init; }
        public int XmxMb { get; init; }
        public int StackKb { get; init; }
        public int PauseTargetMs { get; init; }
        public bool GcLogging { get; init; }
        public bool StringDeduplication { get; init; }
        public int ConsoleLogSizeKb { get; init; } = 1024000;
        public string JavaAgentPath { get; init; } = string.Empty;
        public string ArgumentBase { get; init; } = "game";
        public IReadOnlyList<string> ExtraJvmArguments { get; init; } = Array.Empty<string>();

        public override string ToString()
        {
            return Name;
        }
    }
}
