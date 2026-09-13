using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed record LogIssue(int Line, string Level, string Summary, string Explanation, string Attribution, string Evidence, int Count)
{ internal IReadOnlyList<int> Lines { get; init; } = [Line]; }
internal sealed record LogReport(List<LogIssue> Issues, bool TailOnly, long StartByte)
{ internal string Text { get; init; } = ""; }
internal static class ConsoleDiagnostics
{
    private static readonly Regex Header = new(@"^(?:\[[^\]]+\]\s*)?(?<level>LOG|WARN|ERROR|DEBUG)\s*:", RegexOptions.IgnoreCase);
    private static readonly Regex Exception = new(@"(?:\b[A-Za-z_$][\w$]*(?:\.[\w$]+)*(?:Exception|Error)\b|Caused by:|STACK TRACE|stack traceback)", RegexOptions.IgnoreCase);
    internal static LogReport Read(string path, IEnumerable<InstalledMod> mods)
    {
        const long limit = 16 * 1024 * 1024;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, file.Length - limit); file.Position = start;
        using var reader = new StreamReader(file); if (start > 0) reader.ReadLine();
        string text = reader.ReadToEnd();
        return new(Parse(text, mods), start > 0, start) { Text = text };
    }
    internal static List<LogIssue> Parse(string text, IEnumerable<InstalledMod> mods)
    {
        var catalog = mods.Where(m => m.Location.Length > 0).ToList();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var findings = new List<LogIssue>();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i]; var header = Header.Match(line);
            bool known = Regex.IsMatch(line, @"black chunk|invalid chunk width|checksum.*(?:mismatch|different)|mod.*(?:not found|missing)|failed to load", RegexOptions.IgnoreCase);
            string headerLevel = header.Groups["level"].Value.ToUpperInvariant();
            bool trigger = header.Success && headerLevel is "ERROR" or "WARN" || Exception.IsMatch(line) || known;
            if (!trigger) continue;
            int start = i, end = i;
            
            while (end + 1 < lines.Length && end - start < 80)
            {
                string next = lines[end + 1];
                if (Header.IsMatch(next) && !Regex.IsMatch(next, @"\bat [\w.$]+\(|Caused by:|function:|STACK TRACE|stack traceback", RegexOptions.IgnoreCase)) break;
                if (next.Length == 0 && end > start) break;
                end++;
            }
            string evidence = string.Join("\r\n", lines[start..(end + 1)]);
            string level = header.Success && headerLevel == "WARN" && !Exception.IsMatch(evidence) ? "WARN" : "ERROR";
            string key = evidence.Contains("OutOfMemoryError") ? Regex.IsMatch(evidence, "Direct buffer memory|native thread|native memory", RegexOptions.IgnoreCase) ? "log.nativeMemory" : "log.heap"
                : Regex.IsMatch(evidence, "BufferUnderflow|EOFException|CRC|corrupt", RegexOptions.IgnoreCase) ? "log.binary"
                : evidence.Contains("UnsupportedClassVersionError") ? "log.javaVersion"
                : Regex.IsMatch(evidence, "NoSuchMethodError|NoSuchFieldError|VerifyError|ClassFormatError|LinkageError") ? "log.javaConflict"
                : Regex.IsMatch(evidence, "ClassNotFoundException|NoClassDefFoundError") ? "log.classMissing"
                : Regex.IsMatch(evidence, "BindException|Address already in use", RegexOptions.IgnoreCase) ? "log.port"
                : Regex.IsMatch(evidence, "AccessDenied|Access is denied|Permission denied", RegexOptions.IgnoreCase) ? "log.access"
                : Regex.IsMatch(evidence, "SQLITE_BUSY|database is locked", RegexOptions.IgnoreCase) ? "log.dbLock"
                : Regex.IsMatch(evidence, "black chunk|invalid chunk width|lotheader|lotpack", RegexOptions.IgnoreCase) ? "log.map"
                : Regex.IsMatch(evidence, "checksum.*(?:mismatch|different)", RegexOptions.IgnoreCase) ? "log.checksum"
                : Regex.IsMatch(evidence, "mod.*(?:not found|missing)", RegexOptions.IgnoreCase) ? "log.modMissing"
                : Regex.IsMatch(evidence, @"function:|\.lua|Kahlua", RegexOptions.IgnoreCase) ? "log.lua" : "log.unknown";
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            string normalized = evidence.Replace('\\', '/');
            foreach (var mod in catalog)
                if (normalized.Contains(mod.Location.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(normalized, @"(?:mod\s*(?:=|:|ID\s*[:=]?)\s*['""\[]?)" + Regex.Escape(mod.Id) + @"(?:['""\]\s,]|$)", RegexOptions.IgnoreCase)) referenced.Add(mod.Name);
            foreach (Match modPath in Regex.Matches(normalized, @"(?:/mods/)([^/\r\n]+)", RegexOptions.IgnoreCase)) referenced.Add(modPath.Groups[1].Value);
            string attribution = referenced.Count > 0 ? T("log.reference", string.Join(", ", referenced)) : T("log.unattributed");
            string summary = Regex.Replace(line, @"^.*?t:\d+>\s*", ""); if (summary.Length > 220) summary = summary[..220] + "…";
            findings.Add(new(start + 1, level, summary, T(key), attribution, evidence, 1)); i = end;
        }
        string Signature(LogIssue issue) => Regex.Replace(issue.Evidence, @"(?m)^.*?t:\d+>\s*|0x[0-9a-fA-F]+", "");
        return findings.GroupBy(Signature).Select(g => g.First() with { Count = g.Count(), Lines = g.Select(i => i.Line).ToArray() }).ToList();
    }
}
