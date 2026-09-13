using System.Diagnostics;
using System.Text.Json;
using PZLauncher.Profiles;
using PZLauncher.Desktop;

namespace PZLauncher.Services;

internal sealed class GameLaunchService
{
    public LaunchPlan CreateLaunchPlan(string pzPath, GameProfile gameProfile, JvmProfile jvmProfile)
    {
        gameProfile.Launch.Validate();
        string configPath = Path.Combine(pzPath, "ProjectZomboid64.json");
        if (!JvmArguments.Bases.Contains(jvmProfile.ArgumentBase)) throw new InvalidDataException(T("jvm.extraInvalid"));
        
        
        using var config = jvmProfile.ArgumentBase == "game" ? JsonDocument.Parse(File.ReadAllText(configPath)) : null;
        var root = config?.RootElement ?? default;
        var args = config == null ? new List<string>() : root.GetProperty("vmArgs").EnumerateArray().Select(e => e.GetString()!)
            .Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (config != null && root.TryGetProperty("windows", out var windows))
        {
            var platform = windows.EnumerateObject()
                .Where(p => Version.TryParse(p.Name, out var version) && version <= Environment.OSVersion.Version)
                .OrderByDescending(p => Version.Parse(p.Name)).FirstOrDefault();
            if (platform.Value.ValueKind == JsonValueKind.Object && platform.Value.TryGetProperty("vmArgs", out var platformArgs))
                args.AddRange(platformArgs.EnumerateArray().Select(e => e.GetString()!));
        }
        if (jvmProfile.ArgumentBase == "launcher")
        {
            args.AddRange(["-Djava.awt.headless=true", "-Djava.library.path=win64/;.", "-XX:-CreateCoredumpOnCrash", "-XX:-OmitStackTraceInFastThrow"]);
            string release = Path.Combine(pzPath, "jre64", "release");
            string version = File.Exists(release) ? File.ReadLines(release).FirstOrDefault(l => l.StartsWith("JAVA_VERSION=")) ?? "" : "";
            int.TryParse(version.Split('"').ElementAtOrDefault(1)?.Split('.')[0], out int major);
            if (major >= 9) args.Add("--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED");
            if (major >= 17) args.Add("--enable-native-access=ALL-UNNAMED");
        }
        void SetProperty(string prefix, string value)
        {
            args.RemoveAll(a => a.StartsWith(prefix, StringComparison.Ordinal));
            args.Add(prefix + value);
        }
        SetProperty("-Dzomboid.steam=", jvmProfile.SteamEnabled && !InstallationLocator.IsGog(pzPath) ? "1" : "0");
        SetProperty("-Dzomboid.znetlog=", jvmProfile.ZNetLogEnabled ? "1" : "0");
        SetProperty("-Dzomboid.ConsoleDotTxtSizeKB=", jvmProfile.ConsoleLogSizeKb.ToString());
        if (jvmProfile.XmxMb > 0) SetProperty("-Xmx", jvmProfile.XmxMb + "m");
        if (jvmProfile.XmsMb > 0)
        {
            if (jvmProfile.XmxMb > 0 && jvmProfile.XmsMb > jvmProfile.XmxMb)
                throw new InvalidOperationException(T("error.heap"));
            SetProperty("-Xms", jvmProfile.XmsMb + "m");
        }
        if (jvmProfile.UseZgc || jvmProfile.UseOptimizedG1 ||
            jvmProfile.ExtraJvmArguments.Any(a => a.StartsWith("-XX:+Use") && a.EndsWith("GC")))
        {
            args.RemoveAll(a => a.StartsWith("-XX:+Use") && a.EndsWith("GC"));
            if (jvmProfile.UseZgc) args.Add("-XX:+UseZGC");
            else if (jvmProfile.UseOptimizedG1) args.Add("-XX:+UseG1GC");
        }
        args.AddRange(jvmProfile.ExtraJvmArguments);
        if (jvmProfile.XmsMb > 0)
        {
            string? effectiveMax = args.LastOrDefault(a => a.StartsWith("-Xmx"));
            if (effectiveMax != null && ParseMemoryMb(effectiveMax[4..]) < jvmProfile.XmsMb)
                throw new InvalidOperationException(T("error.heap"));
        }
        if (jvmProfile.StackKb > 0) SetProperty("-Xss", jvmProfile.StackKb + "k");
        bool g1 = args.Contains("-XX:+UseG1GC");
        args.RemoveAll(a => a is "-XX:+UseStringDeduplication" or "-XX:-UseStringDeduplication");
        if (!g1) args.RemoveAll(a => a.StartsWith("-XX:MaxGCPauseMillis="));
        if (g1 && jvmProfile.PauseTargetMs > 0) SetProperty("-XX:MaxGCPauseMillis=", jvmProfile.PauseTargetMs.ToString());
        if (g1 && jvmProfile.StringDeduplication) args.Add("-XX:+UseStringDeduplication");
        if (jvmProfile.GcLogging) args.Add("-Xlog:gc*=info");
        if (!string.IsNullOrWhiteSpace(jvmProfile.JavaAgentPath))
        {
            if (!File.Exists(jvmProfile.JavaAgentPath)) throw new FileNotFoundException(T("error.agent"), jvmProfile.JavaAgentPath);
            args.Add("-javaagent:" + jvmProfile.JavaAgentPath);
        }
        string classpath = config != null ? string.Join(Path.PathSeparator, root.GetProperty("classpath").EnumerateArray().Select(e => e.GetString()))
            : File.Exists(Path.Combine(pzPath, "projectzomboid.jar")) ? ".;projectzomboid.jar" : LegacyClasspath(pzPath);
        string mainClass = config != null ? root.GetProperty("mainClass").GetString()?.Replace('/', '.')
            ?? throw new InvalidDataException(T("error.mainClass")) : "zombie.gameStates.MainScreenState";
        args.AddRange(["-cp", classpath, mainClass, "-cachedir=" + Path.GetFullPath(gameProfile.CacheDir),
            "-console_dot_txt_size_kb=" + jvmProfile.ConsoleLogSizeKb]);
        if (jvmProfile.NoVoip) args.Add("-novoip");
        if (jvmProfile.DebugEnabled) args.Add("-debug");
        args.AddRange(gameProfile.Launch.Arguments());
        return new LaunchPlan
        {
            JavaExecutable = Path.Combine(pzPath, "jre64", "bin", "java.exe"),
            WorkingDirectory = pzPath, CacheDirectory = gameProfile.CacheDir,
            ConsoleLogPath = gameProfile.ConsoleLogPath, Arguments = args
        };
    }
    private static string LegacyClasspath(string installation)
    {
        
        string[] dependencies = ["commons-compress", "istack-commons-runtime", "jakarta.activation", "jakarta.xml.bind-api", "javacord", "javax.activation-api",
            "jaxb-runtime", "joml", "lwjgl", "sqlite-jdbc", "trove", "uncommons-maths"];
        string code = File.Exists(Path.Combine(installation, "java", "zombie", "gameStates", "MainScreenState.class")) ? "java" : ".";
        return string.Join(Path.PathSeparator, new[] { code }.Concat(new[] { installation, Path.Combine(installation, "java") }
            .Where(Directory.Exists).SelectMany(p => Directory.EnumerateFiles(p, "*.jar"))
            .Where(p => dependencies.Any(prefix => Path.GetFileNameWithoutExtension(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(p => Path.GetRelativePath(installation, p)).Order(StringComparer.OrdinalIgnoreCase)));
    }
    internal static int ParseMemoryMb(string value)
    {
        if (value.Length == 0) return 0;
        char unit = char.ToLowerInvariant(value[^1]);
        if (!long.TryParse(char.IsLetter(unit) ? value[..^1] : value, out long size)) return 0;
        return (int)Math.Min(int.MaxValue, unit switch { 'g' => size * 1024, 'm' => size, 'k' => size / 1024, _ => size / 1048576 });
    }

    public Process Start(LaunchPlan plan, DataReceivedEventHandler outputHandler,
        DataReceivedEventHandler errorHandler, EventHandler exitedHandler)
    {
        if (!InstallationLocator.HasGameCode(plan.WorkingDirectory))
            throw new FileNotFoundException(T("install.invalid"));
        if (!File.Exists(plan.JavaExecutable)) throw new FileNotFoundException(T("error.java"), plan.JavaExecutable);
        Directory.CreateDirectory(plan.CacheDirectory);
        var info = new ProcessStartInfo
        {
            FileName = plan.JavaExecutable, WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string arg in plan.Arguments) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += outputHandler;
        process.ErrorDataReceived += errorHandler;
        process.Exited += exitedHandler;
        try
        {
            if (!process.Start()) throw new InvalidOperationException(T("error.start"));
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch { process.Dispose(); throw; }
    }
}
