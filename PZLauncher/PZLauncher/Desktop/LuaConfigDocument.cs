using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;


internal sealed class LuaConfigDocument
{
    internal sealed record Scalar(string Name, string Value, string Type, int Start, int Length);
    private sealed record Token(string Text, int Start, int End, bool String = false, string? Value = null);
    internal Dictionary<string, Scalar> Values { get; } = [];
    private readonly Dictionary<string, (int Close, bool Separator)> tables = [];
    private readonly List<Token> tokens = [];
    internal string Text { get; }
    internal LuaConfigDocument(string text)
    {
        Text = text;
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text.AsSpan(i).StartsWith("--"))
            {
                i += 2; var longComment = Regex.Match(text[i..], @"^\[(=*)\[");
                if (longComment.Success) { string end = "]" + longComment.Groups[1].Value + "]"; int at = text.IndexOf(end, i + longComment.Length, StringComparison.Ordinal); if (at < 0) throw new InvalidDataException(T("config.luaComplex")); i = at + end.Length; }
                else { int end = text.IndexOf('\n', i); i = end < 0 ? text.Length : end + 1; }
                continue;
            }
            int start = i;
            if (text[i] is '\'' or '"')
            {
                char quote = text[i++]; var value = new StringBuilder(); bool closed = false;
                while (i < text.Length)
                {
                    char c = text[i++]; if (c == quote) { closed = true; break; }
                    if (c == '\\')
                    {
                        if (i == text.Length) break; c = text[i++];
                        if (char.IsDigit(c)) { string number = c.ToString(); for (int n = 1; n < 3 && i < text.Length && char.IsDigit(text[i]); n++) number += text[i++]; value.Append((char)int.Parse(number)); continue; }
                        c = c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', 'v' => '\v', 'a' => '\a', '\\' => '\\', '\'' => '\'', '"' => '"', _ => throw new InvalidDataException(T("config.luaComplex")) };
                    }
                    value.Append(c);
                }
                if (!closed) throw new InvalidDataException(T("config.luaComplex"));
                tokens.Add(new(text[start..i], start, i, true, value.ToString())); continue;
            }
            var atom = Regex.Match(text[i..], @"^(?:[A-Za-z_]\w*|[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)");
            i += atom.Success ? atom.Length : 1; tokens.Add(new(text[start..i], start, i));
        }
        int first = tokens.FindIndex(t => t.Text == "{");
        if (first < 0) { if (!string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(T("config.luaComplex")); return; }
        ParseTable(ref first, "");
    }
    private void ParseTable(ref int i, string parent)
    {
        i++; int array = 0;
        while (i < tokens.Count && tokens[i].Text != "}")
        {
            if (tokens[i].Text is "," or ";") { i++; continue; }
            string key;
            if (i + 1 < tokens.Count && Regex.IsMatch(tokens[i].Text, @"^[A-Za-z_]\w*$") && tokens[i + 1].Text == "=") { key = tokens[i].Text; i += 2; }
            else if (i + 3 < tokens.Count && tokens[i].Text == "[" && tokens[i + 2].Text == "]" && tokens[i + 3].Text == "=") { key = tokens[i + 1].String ? tokens[i + 1].Value! : "[" + tokens[i + 1].Text + "]"; i += 4; }
            else key = "[" + ++array + "]";
            string name = parent.Length == 0 ? key : parent + (key.StartsWith('[') ? "" : ".") + key;
            if (i >= tokens.Count) throw new InvalidDataException(T("config.luaComplex"));
            if (tokens[i].Text == "{") ParseTable(ref i, name);
            else
            {
                var t = tokens[i]; bool literal = t.String || t.Text is "true" or "false" || double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                if (literal && i + 1 < tokens.Count && tokens[i + 1].Text is "," or ";" or "}")
                {
                    if (Values.ContainsKey(name)) throw new InvalidDataException(T("config.luaComplex"));
                    Values[name] = new(name, t.Value ?? t.Text, t.String ? "string" : t.Text is "true" or "false" ? "boolean" : "double", t.Start, t.End - t.Start); i++;
                }
                else throw new InvalidDataException(T("config.luaComplex"));
            }
        }
        if (i >= tokens.Count) throw new InvalidDataException(T("config.luaComplex"));
        tables[parent] = (tokens[i].Start, tokens[i - 1].Text is "{" or "," or ";"); i++;
    }
    internal static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    internal string Set(string name, string value, string type)
    {
        string literal = type == "string" ? Quote(value) : value;
        if (Values.TryGetValue(name, out var old)) return Text.Remove(old.Start, old.Length).Insert(old.Start, literal);
        if (!Regex.IsMatch(name, @"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$")) throw new InvalidDataException(T("config.luaComplex"));
        string[] parts = name.Split('.'); int depth = parts.Length - 1;
        while (depth > 0 && !tables.ContainsKey(string.Join('.', parts.Take(depth)))) depth--;
        string parent = string.Join('.', parts.Take(depth));
        if (!tables.TryGetValue(parent, out var table)) throw new InvalidDataException(T("config.luaComplex"));
        string entry = parts[^1] + " = " + literal;
        for (int j = parts.Length - 2; j >= depth; j--) entry = parts[j] + " = { " + entry + " }";
        return Text.Insert(table.Close, (table.Separator ? "" : ",") + "\n    " + entry + ",\n");
    }
}
