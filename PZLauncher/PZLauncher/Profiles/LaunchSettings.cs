namespace PZLauncher.Profiles;

internal sealed class LaunchSettings
{
    public bool SafeMode { get; set; }
    public bool NoSound { get; set; }
    public bool AiTest { get; set; }
    public bool AntiCheats { get; set; }
    public bool ImGui { get; set; }
    public bool ImGuiViewports { get; set; }
    public bool DebugTranslation { get; set; }
    public string DebugLog { get; set; } = "";
    public string DebugConfig { get; set; } = "";
    public string ConnectAddress { get; set; } = "";
    public string ModFolders { get; set; } = "workshop,steam,mods";
    
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConnectPassword { get; set; } = "";
    public IEnumerable<string> Arguments()
    {
        if (SafeMode) yield return "-safemode";
        if (NoSound) yield return "-nosound";
        if (AiTest) yield return "-aitest";
        if (AntiCheats) yield return "-anti-cheats";
        if (ImGuiViewports) yield return "-imguidebugviewports";
        else if (ImGui) yield return "-imgui";
        if (DebugTranslation) yield return "-debugtranslation";
        if (DebugLog.Length > 0) yield return "-debuglog=" + DebugLog;
        if (DebugConfig.Length > 0) yield return "-debugcfg=" + DebugConfig;
        if (ConnectAddress.Length > 0)
        {
            yield return "+connect"; yield return ConnectAddress;
            if (ConnectPassword.Length > 0) { yield return "+password"; yield return ConnectPassword; }
        }
        if (ModFolders != "workshop,steam,mods") { yield return "-modfolders"; yield return ModFolders; }
    }
    public void Validate()
    {
        foreach (string value in new[] { DebugLog, DebugConfig, ConnectAddress, ConnectPassword, ModFolders })
            if (value == null || value.Length > 2048 || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
                throw new InvalidDataException(T("launch.invalid"));
        var folders = ModFolders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (folders.Length == 0 || folders.Distinct().Count() != folders.Length || folders.Any(f => f is not ("workshop" or "steam" or "mods")))
            throw new InvalidDataException(T("launch.foldersInvalid"));
        ModFolders = string.Join(",", folders);
        if (DebugLog.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(DebugLog, @"^[+-]?[A-Za-z][A-Za-z0-9]*(,[+-]?[A-Za-z][A-Za-z0-9]*)*$"))
            throw new InvalidDataException(T("launch.logsInvalid"));
    }
}
