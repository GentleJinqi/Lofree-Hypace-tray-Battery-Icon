using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;

namespace HypaceTray
{
    // Lofree's top-down product photos for the realistic mouse. They are not part of this program: the first time a
    // colourway is picked its photo is downloaded from lofree.co, cut out and kept in the folder as <Colourway>.png.
    public sealed class PhotoStore
    {
        readonly string folder;

        public PhotoStore(string folder) { this.folder = folder; }

        public static string Url(Colourway c)
        {
            return "https://www.lofree.co/cdn/shop/files/Hypace_" + c + "_PI01.webp?width=1000";
        }

        public string PathFor(Colourway c) { return Path.Combine(folder, c + ".png"); }

        // The cached cut-out, or null when it is missing, unreadable or not a sensible mouse.
        public MousePhoto TryLoad(Colourway c)
        {
            try
            {
                string file = PathFor(c);
                if (!File.Exists(file)) return null;
                using (var ms = new MemoryStream(File.ReadAllBytes(file)))
                using (var bmp = new Bitmap(ms))
                    return RealMouse.LooksLikeMouse(bmp.Width, bmp.Height) ? new MousePhoto(bmp) : null;
            }
            catch (Exception) { return null; }
        }

        public void Save(Colourway c, Bitmap cutout)
        {
            Directory.CreateDirectory(folder);
            string file = PathFor(c), part = file + ".part";
            cutout.Save(part, ImageFormat.Png);
            if (File.Exists(file)) File.Delete(file);
            File.Move(part, file);
        }

        // Downloads the photo (asking for JPEG: the CDN converts it, and .NET Framework cannot read WebP), cuts the
        // mouse out and caches it. Throws if anything fails, including a photo that no longer looks like the mouse.
        public MousePhoto Download(Colourway c)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(Url(c));
            request.Accept = "image/jpeg";
            request.UserAgent = "HypaceTray";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            using (var response = request.GetResponse())
            using (var body = response.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                body.CopyTo(ms);
                ms.Position = 0;
                using (var photo = new Bitmap(ms))
                using (var cut = RealMouse.Cutout(photo))
                {
                    if (cut == null || !RealMouse.LooksLikeMouse(cut.Width, cut.Height))
                        throw new InvalidDataException("the photo is no longer a top-down picture of the mouse");
                    Save(c, cut);
                    return new MousePhoto(cut);
                }
            }
        }
    }
}
