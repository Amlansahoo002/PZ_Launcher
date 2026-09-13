using System.Runtime.InteropServices;
using System.Text;

namespace PZLauncher.utils
{
    internal class IniFile
    {
        public string path;


        public IniFile(string INIPath)
        {
            path = INIPath;
        }

        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section,
            string key,
            string val,
            string filePath
        );

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section,
            string key,
            string def,
            StringBuilder retVal,
            int size,
            string filePath
        );

        [DllImport("kernel32.dll")]
        private static extern int GetPrivateProfileSection(string lpAppName,
            byte[] lpszReturnBuffer,
            int nSize,
            string lpFileName
        );


        [DllImport("kernel32.dll")]
        private static extern int GetPrivateProfileString(int section, string key,
            string def, [MarshalAs(UnmanagedType.LPArray)] byte[] lpszReturnBuffer,
            int nSize, string filePath);

        public void IniWriteValue(string Section, string Key, string Value)
        {
            WritePrivateProfileString(Section, Key, Value, path);
        }

        public string IniReadValue(string Section, string Key)
        {
            var temp = new StringBuilder(2048);
            if (Section == "" || Section == String.Empty)
            {
                string[] lines = File.ReadAllLines(path);

                foreach (string line in lines)
                {
                    if (!line.StartsWith(";") && !string.IsNullOrWhiteSpace(line))
                    {

                        string[] keyValuePair = line.Split('=');
                        string key = keyValuePair[0];
                        string value = keyValuePair[1];
                        if (key == Key) temp.Append(value);
                    }
                }
            }
            else
            {
                var i = GetPrivateProfileString(Section, Key, "", temp,
                2048, path);
            }



            return temp.ToString();
        }


        public string IniReadValueAndCreateKeyIfNull(string Section, string Key, string createValue)
        {
            var temp = new StringBuilder(2048);

            try
            {
                var i = GetPrivateProfileString(Section, Key, "", temp,
                    2048, path);
                if (temp.ToString() == "" || temp.ToString() == string.Empty)
                {
                    IniWriteValue(Section, Key, createValue);
                    return createValue;
                }
                return temp.ToString();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


        public List<string> IniReadSectionCode(string section)
        {
            var buffer = new byte[2048];

            GetPrivateProfileSection(section, buffer, 2048, path);

            var tmp = Encoding.ASCII.GetString(buffer).Trim('\0').Split('\0');

            var result = new List<string>();

            foreach (var entry in tmp)
                result.Add(entry.Substring(0, entry.IndexOf("=")));

            return result;
        }

        public List<string> IniReadSectionValue(string section)
        {
            var buffer = new byte[2048];

            GetPrivateProfileSection(section, buffer, 2048, path);

            var tmp = Encoding.ASCII.GetString(buffer).Trim('\0').Split('\0');

            var result = new List<string>();

            foreach (var entry in tmp)
                result.Add(entry.Substring(entry.IndexOf("=") + 1, entry.Length - entry.IndexOf("=") - 1));

            return result;
        }

        public List<string> IniGetSectionNames()
        {
            for (var maxsize = 500; ; maxsize *= 2)
            {
                var bytes = new byte[maxsize];
                var size = GetPrivateProfileString(0, "", "", bytes, maxsize, path);

                if (size < maxsize - 2)
                {
                    var selected = Encoding.ASCII.GetString(bytes, 0,
                        size - (size > 0 ? 1 : 0));
                    return selected.Split('\0').ToList();
                }
            }
        }

        public List<string> IniGetSectionNamesMaskValue(string mask)
        {
            var list = new List<string>();
            try
            {
                for (var maxsize = 500; ; maxsize *= 2)
                {
                    var bytes = new byte[maxsize];
                    var size = GetPrivateProfileString(0, "", "", bytes, maxsize, path);

                    if (size < maxsize - 2)
                    {
                        var selected = Encoding.ASCII.GetString(bytes, 0,
                            size - (size > 0 ? 1 : 0));
                        list = selected.Split('\0').ToList();
                        break;
                    }
                }
                for (var i = list.Count() - 1; i >= 0; i--)
                    if (list[i].IndexOf(mask) != 0)
                        list.RemoveAt(i);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return list;
        }

        public Dictionary<string, string> IniReadSectionFieldMaskValueAfter(string section, string mask)
        {
            var buffer = new byte[2048];

            GetPrivateProfileSection(section, buffer, 2048, path);

            var tmp = Encoding.ASCII.GetString(buffer).Trim('\0').Split('\0');

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in tmp)
            {
                var clef = entry.Substring(0, entry.IndexOf("="));
                var value = entry.Substring(entry.IndexOf("=") + 1, entry.Length - entry.IndexOf("=") - 1);
                if (clef.IndexOf(mask) == 0)
                {
                    clef = clef.Substring(mask.Length, clef.Length - mask.Length);
                    result.Add(clef, value);
                }
            }
            return result;
        }

        public Dictionary<string, string> IniReadSectionFieldMaskValue(string section, string mask)
        {
            var buffer = new byte[2048];

            GetPrivateProfileSection(section, buffer, 2048, path);

            var tmp = Encoding.ASCII.GetString(buffer).Trim('\0').Split('\0');

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in tmp)
            {
                var clef = entry.Substring(0, entry.IndexOf("="));
                var value = entry.Substring(entry.IndexOf("=") + 1, entry.Length - entry.IndexOf("=") - 1);
                if (clef.IndexOf(mask) == 0)
                    result.Add(clef, value);
            }
            return result;
        }

        public Dictionary<string, string> IniReadSection(string section)
        {
            var buffer = new byte[2048];

            GetPrivateProfileSection(section, buffer, 2048, path);

            var tmp = Encoding.ASCII.GetString(buffer).Trim('\0').Split('\0');

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in tmp)
            {
                var clef = entry.Substring(0, entry.IndexOf("="));
                var value = entry.Substring(entry.IndexOf("=") + 1, entry.Length - entry.IndexOf("=") - 1);
                result.Add(clef, value);
            }
            return result;
        }
    }
}
