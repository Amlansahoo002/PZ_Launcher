using System.IO;
using VCDiff.Decoders;
using VCDiff.Encoders;
using VCDiff.Includes;

namespace PZLauncher.utils
{
    internal class Patch
    {

        public static bool CreatePatch(String PatchOutput, String OriginalFile, String Patch) {

            using (FileStream output = new FileStream(PatchOutput, FileMode.Create, FileAccess.Write))
            using (FileStream dict = new FileStream(OriginalFile, FileMode.Open, FileAccess.Read))
            using (FileStream target = new FileStream(Patch, FileMode.Open, FileAccess.Read))
            {
                VCDiff.Encoders.VcEncoder coder = new VcEncoder(dict, target, output);
                VCDiffResult result = coder.Encode(); 
                if (result != VCDiffResult.SUCCESS)
                {
                    return false;
                } else
                {
                    return true;
                }
            }

        }

        public static bool ApplyPatch(String PatchOutput, String OriginalFile, String Patch)
        {

            using (FileStream output = new FileStream(PatchOutput, FileMode.Create, FileAccess.Write))
            using (FileStream dict = new FileStream(OriginalFile, FileMode.Open, FileAccess.Read))
            using (FileStream target = new FileStream(Patch, FileMode.Open, FileAccess.Read))
            {
                VCDiff.Decoders.VcDecoder decoder = new VcDecoder(dict, target, output);

                long bytesWritten = 0;
                VCDiffResult result = decoder.Decode(out bytesWritten);
                if (result != VCDiffResult.SUCCESS)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

        }
    }
}
