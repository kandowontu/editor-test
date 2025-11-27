using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static int Main(string[] args)
    {
        string publishedDir = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "native-windows", "published"));
        string dllPath = Path.Combine(publishedDir, "FamidashEditor.dll");
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"FamidashEditor.dll not found at {dllPath}");
            return 2;
        }

        try
        {
            var asm = Assembly.LoadFrom(dllPath);
            var names = asm.GetManifestResourceNames();
            Console.WriteLine($"Assembly: {dllPath}");
            Console.WriteLine($"Found {names.Length} embedded resources:\n");
            foreach (var n in names.OrderBy(x => x))
            {
                Console.WriteLine(n);
            }

            // Inspect common image resources
            string[] targets = new[] { "famidash.bmp", "palette.png", "palette.bmp", "sprites.png", "parallax.bmp", "ground.bmp" };
            Console.WriteLine();
            foreach (var t in targets)
            {
                var rn = names.FirstOrDefault(r => r.EndsWith(t, StringComparison.OrdinalIgnoreCase));
                if (rn == null) { Console.WriteLine($"Resource not found for: {t}"); continue; }
                Console.WriteLine($"\nResource match for '{t}': {rn}");
                using (var s = asm.GetManifestResourceStream(rn))
                {
                    if (s == null) { Console.WriteLine("  Failed to open stream"); continue; }
                    Console.WriteLine($"  Length: {s.Length} bytes");
                    // If BMP, parse width/height at offsets 18,22
                    if (t.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) && s.Length >= 26)
                    {
                        byte[] hdr = new byte[26];
                        s.Read(hdr, 0, hdr.Length);
                        int width = BitConverter.ToInt32(hdr, 18);
                        int height = BitConverter.ToInt32(hdr, 22);
                        Console.WriteLine($"  BMP width={width}, height={height}");
                    }
                    // If PNG, check first 8 bytes signature
                    if (t.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && s.Length >= 24)
                    {
                        s.Seek(0, SeekOrigin.Begin);
                        byte[] sig = new byte[8]; s.Read(sig,0,8);
                        bool isPng = sig.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10});
                        Console.WriteLine($"  PNG signature ok={isPng}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex);
            return 3;
        }
        return 0;
    }
}
