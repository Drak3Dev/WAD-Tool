using System.IO;
using System;

namespace LegacyThps.Containers
{
    class ThpsWadEntry
    {
        public string name;
        public uint checksum;
        public int size;
        public int offset;

        public byte[] Data;

        public int SizePadded
        {
            get
            {
                int val = size;
                while (val % 2048 != 0) val++;
                return val;
            }
        }

        public ThpsWadEntry()
        {
        }

        public ThpsWadEntry(BinaryReader br)
        {
            checksum = br.ReadUInt32();

            if (checksum != 0)
            {
                offset = br.ReadInt32();
                size = br.ReadInt32();
                name = ThpsWad.GetName(checksum);
            }
        }

        public ThpsWadEntry(string filename)
        {
            name = Path.GetFileName(filename);
            checksum = NameToChecksum(name);
            Data = File.ReadAllBytes(filename);
            size = Data.Length;
            offset = 0;
        }

        public ThpsWadEntry(string n, int off, int s )
        {
            name = n;
            size = s;
            offset = off;
            checksum = Checksum.CalcLegacy(name, false);
        }

        /// <summary>
        /// Checksum for a file name. Unknown names are extracted as _XXXXXXXX_, so they map back to that checksum.
        /// </summary>
        public static uint NameToChecksum(string name)
        {
            if (!Compat.NoFix && name.Length == 10 && name[0] == '_' && name[9] == '_')
                if (uint.TryParse(name.Substring(1, 8), System.Globalization.NumberStyles.HexNumber, null, out uint hex))
                    return hex;

            return Checksum.CalcLegacy(name, false); // original: always hash the name itself
        }

        public void Save(string path)
        {
            File.WriteAllBytes(Path.Combine(path, name), Data);
        }

        public void WriteWadHashed(BinaryWriter bw)
        {
            bw.Write(checksum);
            bw.Write(offset);
            bw.Write(size);
        }

        public void WriteWadRaw(BinaryWriter bw)
        {
            bw.Write(name.ToCharArray());

            int diff = size % 2048;
            int diff2 = name.Length % 4;

            if (Compat.NoFix)
                bw.Write(new byte[(diff == 0) ? 4 : 4 - diff2]); // original behaviour
            else
                bw.Write(new byte[4 - diff2]); // name + at least one null, aligned to 4

            bw.Write(offset);
            bw.Write(size);
        }

        public override string ToString()
        {
            return name + " (" + size + " bytes) at 0x" + offset;
        }
    }
}
