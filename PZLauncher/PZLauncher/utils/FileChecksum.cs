using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace PZLauncher.utils
{
    internal class FileChecksum
    {
        public static string Get_Sha1(string file_path)
        {
            if (File.Exists(file_path))
                using (var stream = File.OpenRead(file_path))
                {
                    using (var sha = new SHA1Managed())
                    {
                        var checksum = sha.ComputeHash(stream);
                        var sendCheckSum = BitConverter.ToString(checksum).Replace("-", string.Empty);
                        return sendCheckSum;
                    }
                }

            return "";
        }

    }
}
