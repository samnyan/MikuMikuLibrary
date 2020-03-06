using SpritesDump.Properties;
using System.Drawing;
using System.IO;
using System;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.Archives;
using MikuMikuLibrary.Textures;
using MikuMikuLibrary.Sprites;
using System.Drawing.Imaging;
using System.Diagnostics;

namespace SpritesDump
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine(Resources.HelpText);
                Console.ReadLine();
                return;
            }

            Console.WriteLine("Parameter: {0}", args[0]);

            Stopwatch sw = Stopwatch.StartNew();

            // Check if this is a folder
            if (File.GetAttributes(args[0]).HasFlag(FileAttributes.Directory))
            {

                foreach (string fracFile in Directory.EnumerateFiles(args[0]))
                {
                    if(fracFile.EndsWith(".farc", StringComparison.OrdinalIgnoreCase))
                        FracReader(fracFile);
                }
            } else if (args[0].EndsWith(".farc", StringComparison.OrdinalIgnoreCase))
            {
                FracReader(args[0]);
            }

            sw.Stop();
            Console.WriteLine("Done. Elapsed Time: {0}", sw.Elapsed.TotalSeconds);
            Console.ReadLine();
        }

        static void FracReader(string fracFile)
        {
            string outPath = Path.ChangeExtension(fracFile, null);

            using (var stream = File.OpenRead(fracFile))
            using (var farcArchive = BinaryFile.Load<FarcArchive>(stream))
            {
                Directory.CreateDirectory(outPath);

                foreach (string fileName in farcArchive)
                {
                    using (var source = farcArchive.Open(fileName, EntryStreamMode.MemoryStream))
                    using (var spriteSet = BinaryFile.Load<SpriteSet>(source))
                    {
                        Bitmap[] sourceBitmaps = new Bitmap[spriteSet.TextureSet.Textures.Count];
                        for (var i = 0; i < sourceBitmaps.Length; i++)
                        {
                            sourceBitmaps[i] = TextureDecoder.Decode(spriteSet.TextureSet.Textures[i]);
                            sourceBitmaps[i].RotateFlip(RotateFlipType.RotateNoneFlipY);
                        }

                        foreach (Sprite sprite in spriteSet.Sprites)
                        {
                            Console.WriteLine("Writing {0} - {1}", Path.GetFileName(fracFile), sprite.Name);
                            Rectangle rec = new Rectangle((int)sprite.X, (int)sprite.Y, (int)sprite.Width, (int)sprite.Height);
                            Bitmap targetBitmap = new Bitmap(rec.Width, rec.Height);
                            Graphics graphics = Graphics.FromImage(targetBitmap);
                            graphics.DrawImage(sourceBitmaps[sprite.TextureIndex], -rec.X, -rec.Y);
                            targetBitmap.Save(Path.Combine(outPath, sprite.Name + ".png"), ImageFormat.Png);
                        }
                    }
                }
            }
        }
    }
}
