using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LegacyThps;
using LegacyThps.Containers;

namespace hedwadtool
{
    // Command-line replacement for the WinForms MainForm; the core (LegacyThps) is the original code.
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            Compat.NoFix = args.Contains("--nofix");
            args = args.Where(a => a != "--nofix").ToArray();

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "extract": return Extract(args.Skip(1).ToArray());
                    case "pack": return Pack(args.Skip(1).ToArray());
                    case "checksum": return ChecksumCmd(args.Skip(1).ToArray());
                    case "brute": return Brute(args.Skip(1).ToArray());
                    default:
                        // drag-and-drop style: just pass a file or folder
                        return Auto(args);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine(
@"wadtool - packer/unpacker for Neversoft ""Big Guns"" engine HED/WAD archives

usage:
  wadtool extract <file.hed|file.wad> [-o <outdir>]
      Extracts the archive to <outdir> (default: next to the file, named after it)
      and writes <outdir>.txt with the file order. HED format (raw/hashed) is detected.

  wadtool pack <folder|list.txt> [--raw|--hashed] [-o <out.wad>]
      Builds HED+WAD from a folder (all files) or from a list .txt
      (files are taken from the folder with the same name as the list).
      --raw     names stored in HED (THPS1-style, default, same as the GUI)
      --hashed  checksums stored in HED (THPS2+-style)

  wadtool checksum <string> [--caps]
      Prints the legacy checksum of a string (lowercased unless --caps).

  wadtool brute <checksum_hex> [--prefix <p>] [--suffix <s>] [--max-len <n>]
      Brute-forces names [_0-9a-z] for a checksum (default max length 6). Ctrl+C to stop.

  wadtool <file|folder>
      Same as dropping it onto the GUI: .hed/.wad extract, folder/.txt pack (raw).

Any command accepts --nofix: run the original code with all its bugs (byte-identical to the GUI).

Name dictionary: filenames.txt next to the binary (or in the current directory).");
        }

        static void LoadDictionary()
        {
            string local = "filenames.txt";
            string beside = Path.Combine(AppContext.BaseDirectory, "filenames.txt");
            ThpsWad.InitDictionary(File.Exists(local) ? local : beside);
        }

        static string Opt(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            if (i < 0) return null;
            if (i + 1 >= args.Length) throw new Exception($"missing value for {name}");
            return args[i + 1];
        }

        static string Positional(string[] args)
        {
            string[] withValue = { "-o", "--prefix", "--suffix", "--max-len" };
            for (int i = 0; i < args.Length; i++)
            {
                if (withValue.Contains(args[i])) { i++; continue; }
                if (!args[i].StartsWith("--")) return args[i];
            }
            return null;
        }

        // Linux filesystems are case-sensitive, game discs usually have SKHAN.HED / SKHAN.WAD.
        static string FindSibling(string file, string ext)
        {
            string exact = Path.ChangeExtension(file, ext);
            if (File.Exists(exact)) return exact;

            string dir = Path.GetDirectoryName(Path.GetFullPath(file));
            string baseName = Path.GetFileNameWithoutExtension(file);
            string match = Directory.EnumerateFiles(dir).FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase));

            return match ?? exact;
        }

        static int Extract(string[] args)
        {
            string file = Positional(args) ?? throw new Exception("no input file");
            LoadDictionary();

            string full = Path.GetFullPath(file);
            string outDir = Opt(args, "-o") ?? Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full));
            outDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outDir));

            ThpsWad wad = ThpsWad.FromFile(FindSibling(full, ".hed"), FindSibling(full, ".wad"));
            wad.Extract(outDir);

            bool raw = wad.archiveType == ThpsWad.ArchiveType.WadRaw;
            Console.WriteLine($"extracted {wad.Entries.Count} files ({(raw ? "raw, THPS1-style" : "hashed, THPS2+-style")} HED) to {outDir}");
            Console.WriteLine($"file list: {outDir}.txt");
            Console.WriteLine($"repack:    wadtool pack \"{outDir}.txt\" {(raw ? "--raw" : "--hashed")}{(Compat.NoFix ? " --nofix" : "")}");
            return 0;
        }

        static int Pack(string[] args)
        {
            string input = Positional(args) ?? throw new Exception("no input folder or list");
            input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));

            ThpsWad.ArchiveType type = args.Contains("--hashed") ? ThpsWad.ArchiveType.WadHashed : ThpsWad.ArchiveType.WadRaw;

            ThpsWad wad;
            string output;

            if (Directory.Exists(input))
            {
                wad = ThpsWad.FromFolder(input);
                output = input + ".wad";
            }
            else if (File.Exists(input))
            {
                wad = ThpsWad.FromList(input);
                output = input;
            }
            else throw new Exception($"Not found: {input}");

            output = Opt(args, "-o") ?? output;
            wad.archiveType = type;
            wad.Write(output);

            Console.WriteLine($"packed {wad.Entries.Count} files ({type}) -> {Path.ChangeExtension(output, ".hed")}, {Path.ChangeExtension(output, ".wad")}");
            return 0;
        }

        static int ChecksumCmd(string[] args)
        {
            string text = Positional(args) ?? throw new Exception("no string given");
            Console.WriteLine(Checksum.CalcLegacy(text, args.Contains("--caps")).ToString("X8"));
            return 0;
        }

        static int Brute(string[] args)
        {
            string checksum = Positional(args) ?? throw new Exception("no checksum given");
            if (!Checksum.TryParseHex(checksum, out _)) throw new Exception($"bad hex: {checksum}");

            string prefix = Opt(args, "--prefix") ?? "";
            string suffix = Opt(args, "--suffix") ?? "";
            int maxLen = int.Parse(Opt(args, "--max-len") ?? "6");

            var brute = new BruteHelper(prefix, suffix, checksum);
            var sw = Stopwatch.StartNew();
            int found = 0;

            Console.CancelKeyPress += (s, e) => Console.WriteLine($"\nstopped after {sw.Elapsed.TotalSeconds:F1}s, {found} match(es)");

            while (true)
            {
                if (brute.ChecksumMatches())
                {
                    found++;
                    Console.WriteLine($"Found {brute.GetText()} in {sw.Elapsed.TotalSeconds} seconds");
                }

                if (brute.GetText().Length - prefix.Length - suffix.Length > maxLen)
                    break;

                brute.Next();
            }

            Console.WriteLine($"done: up to {maxLen} chars in {sw.Elapsed.TotalSeconds:F1}s, {found} match(es)");
            return 0;
        }

        static int Auto(string[] args)
        {
            string path = args[0];

            if (Directory.Exists(path)) return Pack(args);

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".hed":
                case ".wad": return Extract(args);
                case ".txt": return Pack(args);
                case ".pre": throw new NotImplementedException("Can't read legacy PRE yet.");
                default:
                    Console.Error.WriteLine("Doesn't look like a supported file. See wadtool --help");
                    return 1;
            }
        }
    }
}
