using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class WorldSettingsDocument
{
    private readonly byte[] original;
    private readonly Dictionary<string, string> initial = [];
    internal Dictionary<string, string> Values { get; } = [];
    internal List<ConfigOption> Options { get; } = [];
    internal string Name { get; }
    internal int Version { get; }
    private readonly List<string> sandboxOrder = [];
    private int tail;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(true, false, true);
    internal WorldSettingsDocument(string name, byte[] bytes)
    {
        Name = name; original = bytes.ToArray();
        if (name == "mods.txt")
        {
            string text = Utf8.GetString(bytes);
            if (!Regex.IsMatch(text, @"\bVERSION\s*=\s*1\b")) throw new InvalidDataException(T("world.format"));
            foreach (var (block, key) in new[] { ("mods", "mod"), ("maps", "map") })
            {
                var match = Regex.Match(text, @"\b" + block + @"\s*\{([^{}]*)\}");
                if (!match.Success) throw new InvalidDataException(T("world.format"));
                string value = string.Join(';', Regex.Matches(match.Groups[1].Value, @"\b" + key + @"\s*=\s*([^,\r\n]+)").Select(m => m.Groups[1].Value.Trim()));
                Add(block, value, "string", title: T("world." + block));
            }
        }
        else
        {
            if (bytes.Length < 8) throw new InvalidDataException(T("world.format"));
            Version = Int(name == "map_ver.bin" ? 0 : 4);
            
            if (Version != 249) throw new InvalidDataException(T("world.unsupportedFormat", Version));
            if (name == "map_ver.bin")
            {
                int count = Int(4); if (count < 0 || count > 65536 || count * 2 > bytes.Length - 8) throw new InvalidDataException(T("world.format"));
                tail = 8 + count * 2; Add("Map", Utf16.GetString(bytes, 8, count * 2), "string", title: T("world.mapNames"));
            }
            else if (name == "map_t.bin")
            {
                if (!bytes.AsSpan(0, 4).SequenceEqual("GMTM"u8) || bytes.Length < 53) throw new InvalidDataException(T("world.format"));
                Add("Multiplier", Float(8), "double", 0.000001, float.MaxValue, T("world.timeMultiplier"));
                Add("NightsSurvived", Int(12).ToString(), "integer", 0, int.MaxValue, T("world.nights"));
                Add("TargetZombies", Int(16).ToString(), "integer", 0, int.MaxValue, T("world.targetZombies"));
                Add("LastTimeOfDay", Float(20), "double", 0, 24, T("world.previousHour"));
                Add("TimeOfDay", Float(24), "double", 0, 24, T("world.hour"));
                Add("Day", (Int(28) + 1).ToString(), "integer", 1, 31, T("world.day"));
                Add("Month", (Int(32) + 1).ToString(), "integer", 1, 12, T("world.month"));
                Add("Year", Int(36).ToString(), "integer", 1, 9999, T("world.year"));
            }
            else if (name == "map_sand.bin")
            {
                if (bytes.Length < 16 || !bytes.AsSpan(0, 4).SequenceEqual("SAND"u8) || Int(8) != 6) throw new InvalidDataException(T("world.format"));
                int count = Int(12), offset = 16; if (count < 0 || count > 100000) throw new InvalidDataException(T("world.format"));
                string Read()
                {
                    if (offset + 2 > bytes.Length) throw new InvalidDataException(T("world.format"));
                    int length = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset)); offset += 2;
                    if (length < 0 || offset + length > bytes.Length) throw new InvalidDataException(T("world.format"));
                    string value = Utf8.GetString(bytes, offset, length); offset += length; return value;
                }
                for (int i = 0; i < count; i++) { string key = Read(), value = Read(); if (!Values.TryAdd(key, value)) throw new InvalidDataException(T("world.format")); sandboxOrder.Add(key); }
                tail = offset;
            }
            else throw new InvalidDataException(T("world.format"));
        }
        foreach (var p in Values) initial[p.Key] = p.Value;
    }
    private int Int(int offset) => BinaryPrimitives.ReadInt32BigEndian(original.AsSpan(offset));
    private string Float(int offset) => BitConverter.Int32BitsToSingle(Int(offset)).ToString("R", CultureInfo.InvariantCulture);
    private void Add(string name, string value, string type, double? min = null, double? max = null, string? title = null)
    { Values[name] = value; Options.Add(new() { Name = name, Default = value, Type = type, Min = min, Max = max, Title = title }); }
    internal byte[] Encode()
    {
        if (Values.Count == initial.Count && Values.All(p => initial.GetValueOrDefault(p.Key) == p.Value)) return original.ToArray();
        foreach (var option in Options) option.Validate(Values[option.Name]);
        if (Name == "mods.txt")
        {
            string text = Utf8.GetString(original);
            foreach (var (block, key) in new[] { ("mods", "mod"), ("maps", "map") })
            {
                if (Values[block] == initial[block]) continue;
                string[] ids = Values[block].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (ids.Any(id => id.IndexOfAny(['{', '}', ',', '\r', '\n', '\0', '=']) >= 0)) throw new InvalidDataException(T("world.format"));
                var match = Regex.Match(text, @"\b" + block + @"\s*\{([^{}]*)\}"); var body = match.Groups[1];
                
                string kept = Regex.Replace(body.Value, @"\b" + key + @"\s*=\s*[^,\r\n]+,?", "");
                text = text.Remove(body.Index, body.Length).Insert(body.Index, kept.TrimEnd() + "\n" + string.Join("\n", ids.Select(id => "    " + key + " = " + id + ",")) + "\n");
            }
            return Utf8.GetBytes(text);
        }
        if (Name == "map_ver.bin")
        {
            string map = Values["Map"]; if (map.Length > 65536) throw new InvalidDataException(T("world.format"));
            byte[] bytes = new byte[8 + map.Length * 2 + original.Length - tail]; original.AsSpan(0, 4).CopyTo(bytes);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), map.Length); Utf16.GetBytes(map).CopyTo(bytes, 8); original.AsSpan(tail).CopyTo(bytes.AsSpan(8 + map.Length * 2)); return bytes;
        }
        if (Name == "map_t.bin")
        {
            int day = int.Parse(Values["Day"]), month = int.Parse(Values["Month"]), year = int.Parse(Values["Year"]);
            if (day > DateTime.DaysInMonth(year, month) || double.Parse(Values["TimeOfDay"], CultureInfo.InvariantCulture) >= 24 || double.Parse(Values["LastTimeOfDay"], CultureInfo.InvariantCulture) >= 24) throw new InvalidDataException(T("world.dateInvalid"));
            byte[] bytes = original.ToArray(); string[] names = ["Multiplier", "NightsSurvived", "TargetZombies", "LastTimeOfDay", "TimeOfDay", "Day", "Month", "Year"];
            for (int i = 0; i < names.Length; i++)
            {
                if (Values[names[i]] == initial[names[i]]) continue;
                int value = i is 0 or 3 or 4 ? BitConverter.SingleToInt32Bits(float.Parse(Values[names[i]], CultureInfo.InvariantCulture)) : int.Parse(Values[names[i]]) - (i is 5 or 6 ? 1 : 0);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8 + 4 * i), value);
            }
            return bytes;
        }
        using var stream = new MemoryStream(); stream.Write(original.AsSpan(0, 12));
        string[] keys = sandboxOrder.Concat(Values.Keys.Except(sandboxOrder)).ToArray();
        byte[] countBytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(countBytes, keys.Length); stream.Write(countBytes);
        void Write(string text)
        {
            byte[] value = Utf8.GetBytes(text); if (value.Length > short.MaxValue) throw new InvalidDataException(T("world.format"));
            byte[] length = new byte[2]; BinaryPrimitives.WriteInt16BigEndian(length, (short)value.Length); stream.Write(length); stream.Write(value);
        }
        foreach (string key in keys) { Write(key); Write(Values[key]); } stream.Write(original.AsSpan(tail)); return stream.ToArray();
    }
    internal static void Save(string world, Dictionary<string, byte[]> original, Dictionary<string, byte[]> edited, Action ensureStopped)
    {
        ensureStopped();
        foreach (var p in original)
        {
            string path = WorldFiles.Within(world, p.Key);
            if (!File.ReadAllBytes(path).SequenceEqual(p.Value)) throw new IOException(T("world.changed"));
        }
        var changed = edited.Where(p => !p.Value.SequenceEqual(original[p.Key])).ToList();
        var written = new List<string>();
        try { foreach (var p in changed) { LauncherStorage.WriteAtomicBytes(WorldFiles.Within(world, p.Key), p.Value); written.Add(p.Key); } }
        catch
        {
            foreach (string name in written.AsEnumerable().Reverse()) LauncherStorage.WriteAtomicBytes(WorldFiles.Within(world, name), original[name]);
            throw;
        }
        foreach (var p in changed) original[p.Key] = p.Value.ToArray();
    }
    internal static void Delete(string cache, string world)
    {
        string root = Path.Combine(cache, "Saves"); string relative = Path.GetRelativePath(root, world);
        string checkedPath = WorldFiles.Within(root, relative);
        if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length != 2) throw new IOException(T("world.path"));
        GameProcessGuard.EnsureStopped(cache); WorldFiles.Enumerate(checkedPath);
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(checkedPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}
