using System.IO.Compression;
using System.Text;

namespace PZLauncher.Desktop;


internal static class GameVersionReader
{
    private sealed record Constant(int Tag, int A = 0, int B = 0, string Text = "");
    internal static Version? Read(string jar)
    {
        if (!File.Exists(jar)) return null;
        try
        {
            using var archive = jar.EndsWith(".class", StringComparison.OrdinalIgnoreCase) ? null : ZipFile.OpenRead(jar);
            var entry = archive?.GetEntry("zombie/core/Core.class"); if (archive != null && entry == null) return null;
            using var reader = new BinaryReader(archive == null ? File.OpenRead(jar) : entry!.Open());
            int U2() => (reader.ReadByte() << 8) | reader.ReadByte();
            int U4() => (U2() << 16) | U2();
            if (U4() != unchecked((int)0xCAFEBABE)) return null;
            U2(); U2(); int count = U2(); var pool = new Constant[count];
            for (int i = 1; i < count; i++)
            {
                int tag = reader.ReadByte();
                pool[i] = tag switch
                {
                    1 => new(tag, Text: Encoding.UTF8.GetString(reader.ReadBytes(U2()))),
                    3 or 4 => new(tag, U4()),
                    5 or 6 => new(tag, U4(), U4()),
                    7 or 8 or 16 or 19 or 20 => new(tag, U2()),
                    9 or 10 or 11 or 12 or 17 or 18 => new(tag, U2(), U2()),
                    15 => new(tag, reader.ReadByte(), U2()),
                    _ => throw new InvalidDataException("Type de constante Java non reconnu.")
                };
                if (tag is 5 or 6) i++;
            }
            U2(); U2(); U2(); int interfaces = U2(); for (int i = 0; i < interfaces; i++) U2();
            void SkipAttributes() { int n = U2(); for (int a = 0; a < n; a++) { U2(); reader.ReadBytes(U4()); } }
            int fields = U2(); for (int i = 0; i < fields; i++) { U2(); U2(); U2(); SkipAttributes(); }
            int methods = U2();
            for (int method = 0; method < methods; method++)
            {
                U2(); string name = pool[U2()].Text; U2(); int attrs = U2();
                for (int attr = 0; attr < attrs; attr++)
                {
                    string attribute = pool[U2()].Text; int length = U4(); byte[] data = reader.ReadBytes(length);
                    if (name != "<clinit>" || attribute != "Code") continue;
                    int At(int p) => (data[p] << 8) | data[p + 1];
                    int end = 8 + (At(4) << 16 | At(6));
                    string ClassName(int cp) => pool[pool[cp].A].Text;
                    int Integer(ref int p)
                    {
                        int op = data[p++];
                        if (op is >= 2 and <= 8) return op - 3;
                        if (op == 16) return (sbyte)data[p++];
                        if (op == 17) { int v = (short)At(p); p += 2; return v; }
                        if (op == 18) return pool[data[p++]].A;
                        if (op == 19) { int v = pool[At(p)].A; p += 2; return v; }
                        return -1;
                    }
                    for (int p = 8; p + 16 < end; p++)
                    {
                        if (data[p] != 187 || ClassName(At(p + 1)) != "zombie/core/GameVersion" || data[p + 3] != 89) continue;
                        int q = p + 4, major = Integer(ref q), minor = Integer(ref q);
                        if (major is < 1 or > 100 || minor < 0 || minor > 1000) continue;
                        if (data[q] == 18) q += 2; else if (data[q] == 19) q += 3; else continue;
                        if (data[q] != 183 || data[q + 3] != 179) continue;
                        int field = At(q + 4);
                        if (pool[pool[pool[field].B].A].Text == "gameVersion") return new Version(major, minor);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or IndexOutOfRangeException or NullReferenceException)
        { LauncherStorage.Log("Version PZ non déterminée : " + e.Message); }
        return null;
    }
}
