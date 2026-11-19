using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace HypaceTray
{
    // The realistic mouse's three product photos (Lofree's, cut out of their background; see photos/README.md),
    // built into the program by build.ps1, so nothing is downloaded.
    public static class Photos
    {
        public static string ResourceName(Colourway c) { return "HypaceTray.photos." + c + ".png"; }

        // Null if the photo is missing from the program (built without build.ps1's /resource flags) or unreadable;
        // the drawn mouse is shown then.
        public static MousePhoto Load(Colourway c)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(c)))
                {
                    if (s == null) return null;
                    using (var bmp = new Bitmap(s)) return new MousePhoto(bmp);
                }
            }
            catch (Exception) { return null; }
        }
    }
}
