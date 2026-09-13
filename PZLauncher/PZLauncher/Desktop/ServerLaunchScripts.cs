using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using PZLauncher.Services;

namespace PZLauncher.Desktop;

internal static class ServerLaunchScripts
{
    private static readonly UTF8Encoding Utf8 = new(false);
    internal static string Export(LaunchPlan plan, string directory, string name, bool windows, bool linux, string linuxCache)
    {
        if (!windows && !linux) throw new ArgumentException("Choose at least one script format.");
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,64}$")) throw new ArgumentException(nameof(name));
        if (linux && (!linuxCache.StartsWith('/') || linuxCache.StartsWith("//") || linuxCache.Contains('\\') || linuxCache.IndexOfAny(['\0', '\r', '\n']) >= 0))
            throw new ArgumentException("The Linux cache must be an absolute Linux path, for example /home/pz/Zomboid.");
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("The export destination already exists.");
        if (plan.Arguments.Any(a => a.IndexOfAny(['\0', '\r', '\n']) >= 0)) throw new InvalidDataException("A launch argument contains a line break or NUL.");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string assets = name + ".assets";
        if (windows)
        {
            var snapshot = new Snapshot(plan, directory, assets, false, files);
            files[assets + "/windows.json"] = Utf8.GetBytes(JsonSerializer.Serialize(new
            {
                javaExecutable = plan.JavaExecutable, workingDirectory = plan.WorkingDirectory,
                arguments = snapshot.Arguments(linuxCache)
            }, new JsonSerializerOptions { WriteIndented = true }));
            files[assets + "/run-windows.ps1"] = Utf8.GetBytes(WindowsBootstrap);
            files[name + ".bat"] = Utf8.GetBytes(WindowsScript(assets));
        }
        if (linux)
        {
            var snapshot = new Snapshot(plan, directory, assets, true, files);
            files[assets + "/linux.args"] = Utf8.GetBytes(ArgumentFile(snapshot.Arguments(linuxCache)));
            files[name + ".sh"] = Utf8.GetBytes(LinuxScript(assets));
        }
        files["README.txt"] = Utf8.GetBytes(
            "PZLauncher server launch export: " + name + "\n\n" +
            "Windows: run " + name + ".bat. It uses the original Windows installation and cache. Keep this export at:\n" + directory + "\n\n" +
            "Linux: copy " + name + ".sh AND the " + assets + " directory into the ROOT of a Linux Project Zomboid installation of the same version.\n" +
            "Run: sh ./" + name + ".sh\nLinux cache: " + linuxCache + "\n\n" +
            "Only launch scripts and Java loader/classpath dependencies are exported. Transfer server configuration, saves and traditional/Workshop mods separately.\n" +
            "Use Linux game binaries and native libraries; the Windows installation is not a Linux server. Individual Java mods may also need Linux-compatible native files.\n" +
            "The selected JVM settings, main class, loader and Java mod selection are preserved. Additional server arguments can be passed to either script.\n" +
            "Windows uses the Windows PowerShell included with Windows to pass arguments through the same Windows process API as the launcher. Its script execution policy override applies only to that child process.\n" +
            "On first start, Project Zomboid can request an admin password in the console. If you selected password inclusion, the .json/.args files contain it in plaintext.\n");
        
        Directory.CreateDirectory(directory);
        foreach (var file in files.OrderBy(p => p.Key.EndsWith(".bat") || p.Key.EndsWith(".sh")))
        {
            string target = WorldFiles.Within(directory, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(file.Value);
        }
        return directory;
    }

    
    internal static string ArgumentFile(IEnumerable<string> arguments) => string.Join("\n", arguments.Select(a =>
        "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t").Replace("\f", "\\f") + "\"")) + "\n";

    internal static string WindowsScript(string assets)
    {
        return "@echo off\r\nsetlocal DisableDelayedExpansion\r\nchcp 65001 >nul\r\n" +
            "set \"_JAVA_OPTIONS=\"\r\n" +
            "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"%~dp0" + assets + "\\run-windows.ps1\" %*\r\n" +
            "exit /b %ERRORLEVEL%\r\n";
    }

    
    
    
    private const string WindowsBootstrap = """
        param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ExtraArgs)
        $ErrorActionPreference = 'Stop'
        $config = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'windows.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
        function Quote-WindowsArgument([string]$value) {
            $escaped = [regex]::Replace($value, '(\\*)"', '$1$1\"')
            $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
            return '"' + $escaped + '"'
        }
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $config.javaExecutable
        $start.WorkingDirectory = $config.workingDirectory
        $start.UseShellExecute = $false
        $arguments = @($config.arguments)
        if ($ExtraArgs) { $arguments += $ExtraArgs }
        $start.Arguments = (($arguments | ForEach-Object { Quote-WindowsArgument ([string]$_) }) -join ' ')
        $start.EnvironmentVariables.Remove('_JAVA_OPTIONS')
        $process = [Diagnostics.Process]::Start($start)
        $process.WaitForExit()
        $code = $process.ExitCode
        $process.Dispose()
        exit $code
        """;

    internal static string LinuxScript(string assets) =>
        "#!/bin/sh\nset -eu\nunset _JAVA_OPTIONS\n" +
        "SCRIPT_DIR=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\ncd \"$SCRIPT_DIR\"\n" +
        "if [ ! -f ./projectzomboid.jar ] || [ ! -x ./jre64/bin/java ]; then\n" +
        "  printf '%s\\n' 'Place this script and its .assets directory in a Linux Project Zomboid installation.' >&2\n  exit 1\nfi\n" +
        "LD_LIBRARY_PATH=\"$SCRIPT_DIR/linux64:$SCRIPT_DIR/natives:$SCRIPT_DIR/natives/linux64:$SCRIPT_DIR/jre64/lib:$SCRIPT_DIR/jre64/lib/server:$SCRIPT_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}\"\n" +
        "export LD_LIBRARY_PATH\nexec ./jre64/bin/java \"@./" + assets + "/linux.args\" \"$@\"\n";

    private sealed class Snapshot(LaunchPlan plan, string output, string assets, bool linux, Dictionary<string, byte[]> files)
    {
        private readonly Dictionary<string, string> copied = new(StringComparer.OrdinalIgnoreCase);
        private readonly string side = linux ? "linux" : "windows";
        private string Reference(string relative) => linux ? "./" + relative : Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar));
        private string Full(string path) => Path.GetFullPath(path, plan.WorkingDirectory);
        private bool Same(string left, string right) => Path.TrimEndingDirectorySeparator(Full(left)).Equals(Path.TrimEndingDirectorySeparator(Full(right)), StringComparison.OrdinalIgnoreCase);

        private string Dependency(string source)
        {
            string path = Full(source);
            if (Same(path, plan.WorkingDirectory)) return linux ? "." : source;
            if (Same(path, Path.Combine(plan.WorkingDirectory, "projectzomboid.jar"))) return linux ? "projectzomboid.jar" : source;
            if (copied.TryGetValue(path, out var existing)) return Reference(existing);
            if (!File.Exists(path)) throw new FileNotFoundException("A Java export dependency is missing or is an unsupported classpath directory.", path);
            string relative = assets + "/" + side + "/" + copied.Count.ToString("D3") + "-" + Path.GetFileName(path);
            copied.Add(path, relative); files[relative] = File.ReadAllBytes(path);
            return Reference(relative);
        }

        private string Manifest(string path, bool community)
        {
            string relative = assets + "/" + side + (community ? "/launch.properties" : "/leaf-mods.txt");
            string[] lines = File.ReadAllLines(path);
            if (community)
            {
                if (!lines.Contains("format=1") || !lines.Contains("side=server")) throw new InvalidDataException("Unsupported Java agent manifest for server export.");
                lines = lines.Select(line =>
                {
                    int split = line.IndexOf('=');
                    if (split < 0) return line;
                    string key = line[..split];
                    if (key != "gameJar" && !Regex.IsMatch(key, "^mod\\.[0-9]+\\.path$")) return line;
                    string value = Encoding.UTF8.GetString(Convert.FromBase64String(line[(split + 1)..]));
                    return key + "=" + Convert.ToBase64String(Utf8.GetBytes(Dependency(value)));
                }).ToArray();
            }
            else lines = lines.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Dependency).ToArray();
            files[relative] = Utf8.GetBytes(string.Join("\n", lines) + "\n");
            return Reference(relative);
        }

        internal IReadOnlyList<string> Arguments(string linuxCache)
        {
            var args = plan.Arguments.ToArray();
            int cp = Array.IndexOf(args, "-cp");
            if (cp < 0 || cp + 2 >= args.Length) throw new InvalidDataException("Server launch classpath is missing.");
            args[cp + 1] = string.Join(linux ? ':' : ';', args[cp + 1].Split(';').Select(Dependency));
            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (argument.StartsWith("-javaagent:"))
                {
                    string value = argument["-javaagent:".Length..];
                    int split = value.IndexOf('=');
                    args[i] = "-javaagent:" + Dependency(split < 0 ? value : value[..split]) +
                        (split < 0 ? "" : "=" + Manifest(value[(split + 1)..], true));
                }
                else if (argument.StartsWith("-Dleaf.addMods=@")) args[i] = "-Dleaf.addMods=@" + Manifest(argument["-Dleaf.addMods=@".Length..], false);
                else if (linux && argument.StartsWith("-Dleaf.gamePath=")) args[i] = "-Dleaf.gamePath=.";
                else if (linux && argument.StartsWith("-Djava.library.path=")) args[i] = "-Djava.library.path=./linux64:./natives:./natives/linux64:.";
                else if (linux && argument.StartsWith("-cachedir=")) args[i] = "-cachedir=" + (linuxCache.Length > 1 ? linuxCache.TrimEnd('/') : linuxCache);
            }
            return args;
        }
    }
}
