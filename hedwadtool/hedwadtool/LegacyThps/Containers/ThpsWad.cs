using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace LegacyThps.Containers
{
    class ThpsWad
    {
        public enum ArchiveType
        {
            WadRaw,
            WadHashed,
            Pre
        }

        public ArchiveType archiveType = ArchiveType.WadRaw;

        public int WadSize
        {
            get
            {
                int size = 0;

                foreach (ThpsWadEntry w in Entries)
                    size += w.SizePadded;

                return size;
            }
        }


        public static Dictionary<uint, string> checksums = new Dictionary<uint, string>();

        public static void InitDictionary(string filename = "filenames.txt")
        {
            checksums.Clear();

            if (File.Exists(filename))
            {
                string[] lines = File.ReadAllLines(filename);

                foreach (string s in lines)
                {
                    string temp = s.Trim().ToLower();

                    if (temp != "")
                        if (!temp.Contains("#"))
                        {
                            uint crc = Checksum.CalcLegacy(temp, true);
                            if (!checksums.ContainsKey(crc))
                                checksums.Add(crc, temp);
                        }
                }
            }
        }

        public static string GetName(uint checksum)
        {
            if (checksums.ContainsKey(checksum))
                return checksums[checksum];

            Console.WriteLine(checksum.ToString("X8") + " not found");

            return $"_{checksum.ToString("X8")}_";
        }



        public List<ThpsWadEntry> Entries = new List<ThpsWadEntry>();

        public ThpsWad(BinaryReader hed, BinaryReader wad)
        {
            Read(hed, wad);
        }

        public static ThpsWad FromReader(BinaryReader hed, BinaryReader wad)
        {
            return new ThpsWad(hed, wad);
        }

        /// <summary>
        /// Static method to create ThpsWad object from a given filename for either HED or WAD. Extensions are added automatically.
        /// </summary>
        /// <param name="file">Path to file.</param>
        /// <returns>ThpsWad object.</returns>
        public static ThpsWad FromFile(string file)
        {
            return ThpsWad.FromFile(Path.ChangeExtension(file, ".hed"), Path.ChangeExtension(file, ".wad"));
        }

        /// <summary>
        /// Static method to create ThpsWad object from HED/WAD files.
        /// </summary>
        /// <param name="hedFile">Path to HED</param>
        /// <param name="wadFile">Path to WAD</param>
        /// <returns>ThpsWad object.</returns>
        public static ThpsWad FromFile(string hedFile, string wadFile)
        {
            if (!File.Exists(hedFile))
                throw new Exception($"Not found: {hedFile}");

            if (!File.Exists(wadFile))
                throw new Exception($"Not found: {wadFile}");

            using (BinaryReader hed = new BinaryReader(File.OpenRead(hedFile)))
            {
                using (BinaryReader wad = new BinaryReader(File.OpenRead(wadFile)))
                {
                    return ThpsWad.FromReader(hed, wad);
                }
            }
        }

        public static ThpsWad FromList(string filename)
        {
            if (!File.Exists(filename))
                throw new Exception($"Not found: {filename}");

            string[] lines = File.ReadAllLines(filename);
            // ChangeExtension(f, "") gives "name." - Windows strips the trailing dot, Linux doesn't (always fixed)
            string path = Path.ChangeExtension(filename, null);

            ThpsWad wad = new ThpsWad();

            foreach (string s in lines)
            {
                if (Compat.NoFix ? s != "" : s.Trim() != "")
                {
                    ThpsWadEntry en = new ThpsWadEntry(Path.Combine(path, s.Trim()));
                    wad.Entries.Add(en);
                }
            }

            wad.RecalcOffsets();

            return wad;
        }

        public static ThpsWad FromFolder(string path)
        {
            if (!Directory.Exists(path))
                throw new Exception($"Not found: {path}");

            string[] lines = Directory.GetFiles(path);

            // NTFS lists files alphabetically, Linux filesystems don't (always fixed)
            Array.Sort(lines, StringComparer.OrdinalIgnoreCase);

            ThpsWad wad = new ThpsWad();

            foreach (string s in lines)
            {
                if (s != "")
                {
                    ThpsWadEntry en = new ThpsWadEntry(s);
                    wad.Entries.Add(en);
                }
            }

            wad.RecalcOffsets();

            return wad;
        }

        public void RecalcOffsets()
        {
            int offset = 0;

            foreach (ThpsWadEntry w in Entries)
            {
                w.offset = offset;
                offset += w.SizePadded;
            }
        }

        public void Read(BinaryReader hed, BinaryReader wad)
        {
            // original reads hashed HED only
            if (!Compat.NoFix && TryReadRaw(hed, wad))
            {
                archiveType = ArchiveType.WadRaw;
                return;
            }

            archiveType = ArchiveType.WadHashed;
            hed.BaseStream.Position = 0;

            if (hed.BaseStream.Length % 12 != 0)
                if ((hed.BaseStream.Length - 4) % 12 != 0)
                    throw new Exception("not a THPS2 WAD");

            if (Compat.NoFix)
            {
                do
                {
                    ThpsWadEntry en = new ThpsWadEntry(hed);
                    wad.BaseStream.Position = en.offset;
                    en.Data = wad.ReadBytes(en.size);

                    Entries.Add(en);
                }
                while (hed.BaseStream.Length - hed.BaseStream.Position >= 12 );

                return;
            }

            while (hed.BaseStream.Length - hed.BaseStream.Position >= 4)
            {
                ThpsWadEntry en = new ThpsWadEntry(hed);

                if (en.checksum == 0)
                    break; // terminator

                wad.BaseStream.Position = en.offset;
                en.Data = wad.ReadBytes(en.size);

                if (en.Data.Length != en.size)
                    Console.Error.WriteLine($"warning: {en.name} is truncated ({en.Data.Length} of {en.size} bytes), WAD too short");

                Entries.Add(en);
            }
        }

        static bool IsValidNameChar(byte b)
        {
            return b > 0x20 && b < 0x7F && "/\\:*?\"<>|".IndexOf((char)b) < 0;
        }

        /// <summary>
        /// Reads THPS1-style HED: { name, null padded to 4 bytes, int offset, int size } ... 0xFF.
        /// Validates the whole HED first, so a hashed HED is never mistaken for raw. Leaves Entries empty on failure.
        /// </summary>
        bool TryReadRaw(BinaryReader hed, BinaryReader wad)
        {
            hed.BaseStream.Position = 0;
            byte[] buf = hed.ReadBytes((int)hed.BaseStream.Length);
            long wadLength = wad.BaseStream.Length;

            var found = new List<ThpsWadEntry>();
            int pos = 0;

            while (true)
            {
                if (pos == buf.Length - 1 && buf[pos] == 0xFF) break; // terminator
                if (pos == buf.Length && found.Count > 0) break; // no terminator, still fine
                if (pos >= buf.Length) return false;

                int start = pos;
                while (pos < buf.Length && buf[pos] != 0)
                {
                    if (!IsValidNameChar(buf[pos]) || pos - start > 255) return false;
                    pos++;
                }

                int len = pos - start;
                int next = start + len + (4 - len % 4);

                if (len == 0 || next + 8 > buf.Length) return false;

                for (int i = start + len; i < next; i++)
                    if (buf[i] != 0) return false;

                int offset = BitConverter.ToInt32(buf, next);
                int size = BitConverter.ToInt32(buf, next + 4);

                if (offset < 0 || size < 0 || offset > wadLength) return false;

                found.Add(new ThpsWadEntry(Encoding.ASCII.GetString(buf, start, len), offset, size));
                pos = next + 8;
            }

            foreach (ThpsWadEntry en in found)
            {
                wad.BaseStream.Position = en.offset;
                en.Data = wad.ReadBytes(en.size);

                if (en.Data.Length != en.size)
                    Console.Error.WriteLine($"warning: {en.name} is truncated ({en.Data.Length} of {en.size} bytes), WAD too short");
            }

            Entries.AddRange(found);
            return true;
        }

        public void Extract(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            StringBuilder sb = new StringBuilder();

            foreach (ThpsWadEntry en in Entries)
            {
                en.Save(path);
                sb.AppendLine(en.name);
            }

            File.WriteAllText(path + ".txt", sb.ToString());
        }


        public void Write(string filename)
        {
            //make sure the offsets are ok
            RecalcOffsets();

            if (archiveType != ArchiveType.Pre)
            {
                if (archiveType == ArchiveType.WadHashed)
                {
                    using (BinaryWriter hed = new BinaryWriter(File.Create(Path.ChangeExtension(filename, ".hed"))))
                    {
                        foreach (ThpsWadEntry w in Entries)
                            w.WriteWadHashed(hed);

                        hed.Write((int)0);
                    }
                }

                if (archiveType == ArchiveType.WadRaw)
                {
                    using (BinaryWriter hed = new BinaryWriter(File.Create(Path.ChangeExtension(filename, ".hed"))))
                    {
                        foreach (ThpsWadEntry w in Entries)
                            w.WriteWadRaw(hed);

                        hed.Write((byte)0xFF);
                    }
                }

                using (BinaryWriter wad = new BinaryWriter(File.Create(Path.ChangeExtension(filename, ".wad"))))
                {
                    foreach (ThpsWadEntry w in Entries)
                    {
                        wad.BaseStream.Position = w.offset;
                        wad.Write(w.Data);
                    }

                    if (Compat.NoFix || WadSize > 0)
                    {
                        wad.BaseStream.Position = WadSize - 1;
                        wad.Write((byte)0);
                    }
                }
            }
            else
            {
                throw new NotImplementedException("can't write PRE yet");
            }
        }


        public ThpsWad()
        {
            Reset();
        }

        public void Reset()
        {
            checksums.Clear();

            InitDictionary();
        }
    }
}
