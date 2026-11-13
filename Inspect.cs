using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Basma
{
    public sealed class PartInfo
    {
        public string Scheme; public int Index; public string Type, TypeCode, Name, Guid;
        public long StartLba, Sectors; public bool Bootable;
        public long EndLba => StartLba + Sectors - 1;
    }

    public sealed class DiskLayout
    {
        public string Scheme = "none";
        public List<PartInfo> Parts = new();
        public List<(long start, long sectors)> Gaps = new();
        public List<string> Notes = new();
        public string DiskSignature, DiskGuid;
        public bool BootSig;
    }

    /// <summary>Parses MBR and GPT partition tables (read-only).</summary>
    public static class Partitions
    {
        static readonly Dictionary<byte, string> MbrTypes = new()
        {
            [0x01] = "FAT12", [0x04] = "FAT16 <32M", [0x05] = "Extended", [0x06] = "FAT16", [0x07] = "NTFS / exFAT", [0x0B] = "FAT32 CHS",
            [0x0C] = "FAT32 LBA", [0x0E] = "FAT16 LBA", [0x0F] = "Extended LBA", [0x11] = "Hidden FAT12", [0x17] = "Hidden NTFS",
            [0x1B] = "Hidden FAT32", [0x27] = "Windows RE (hidden)", [0x82] = "Linux swap", [0x83] = "Linux", [0x8E] = "Linux LVM",
            [0xA5] = "FreeBSD", [0xAF] = "Apple HFS+", [0xEE] = "GPT protective", [0xEF] = "EFI system", [0xFD] = "Linux RAID",
        };
        static readonly Dictionary<string, string> GptTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["C12A7328-F81F-11D2-BA4B-00A0C93EC93B"] = "EFI system partition",
            ["E3C9E316-0B5C-4DB8-817D-F92DF00215AE"] = "Microsoft reserved (MSR)",
            ["EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"] = "Basic data (NTFS/FAT/exFAT)",
            ["DE94BBA4-06D1-4D40-A16A-BFD50179D6AC"] = "Windows recovery",
            ["5808C8AA-7E8F-42E0-85D2-E1E90434CFB3"] = "LDM metadata",
            ["AF9B60A0-1431-4F62-BC68-3311714A69AD"] = "LDM data",
            ["E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D"] = "Storage Spaces",
            ["0FC63DAF-8483-4772-8E79-3D69D8477DE4"] = "Linux filesystem",
            ["0657FD6D-A4AB-43C4-84E5-0933C84B4F4F"] = "Linux swap",
            ["E6D6D379-F507-44C2-A23C-238F2A3DF928"] = "Linux LVM",
            ["A19D880F-05FC-4D3B-A006-743F0F84911E"] = "Linux RAID",
            ["7C3457EF-0000-11AA-AA11-00306543ECAC"] = "Apple APFS",
            ["48465300-0000-11AA-AA11-00306543ECAC"] = "Apple HFS+",
            ["21686148-6449-6E6F-744E-656564454649"] = "BIOS boot",
        };

        /// <summary>readSectors(lba, count) → bytes.</summary>
        public static DiskLayout Parse(Func<long, int, byte[]> readSectors, int sector, long totalSectors)
        {
            var L = new DiskLayout();
            var mbr = readSectors(0, 1);
            if (mbr.Length < 512) { L.Notes.Add("sector 0 unreadable"); return L; }
            L.BootSig = mbr[510] == 0x55 && mbr[511] == 0xAA;
            L.DiskSignature = BitConverter.ToUInt32(mbr, 440).ToString("X8", CultureInfo.InvariantCulture);
            if (!L.BootSig) { L.Notes.Add("no 55 AA boot signature at offset 0x1FE"); return L; }
            var mbrParts = new List<PartInfo>();
            for (int i = 0; i < 4; i++)
            {
                int o = 446 + 16 * i;
                byte type = mbr[o + 4];
                if (type == 0) continue;
                mbrParts.Add(new PartInfo
                {
                    Scheme = "MBR", Index = i + 1, Bootable = mbr[o] == 0x80, TypeCode = "0x" + type.ToString("X2", CultureInfo.InvariantCulture),
                    Type = MbrTypes.TryGetValue(type, out var n) ? n : "unknown",
                    StartLba = BitConverter.ToUInt32(mbr, o + 8), Sectors = BitConverter.ToUInt32(mbr, o + 12)
                });
            }
            if (mbrParts.Any(p => p.TypeCode == "0xEE"))
            {
                var hdr = readSectors(1, 1);
                if (hdr.Length >= 92 && Encoding.ASCII.GetString(hdr, 0, 8) == "EFI PART")
                {
                    L.Scheme = "GPT";
                    L.DiskGuid = new Guid(hdr.AsSpan(56, 16)).ToString().ToUpperInvariant();
                    long entLba = BitConverter.ToInt64(hdr, 72);
                    int n = (int)BitConverter.ToUInt32(hdr, 80), size = (int)BitConverter.ToUInt32(hdr, 84);
                    n = Math.Min(n, 256);
                    int secs = (n * size + sector - 1) / sector;
                    var ent = readSectors(entLba, secs);
                    for (int i = 0; i < n && (i + 1) * size <= ent.Length; i++)
                    {
                        int o = i * size;
                        var tg = new Guid(ent.AsSpan(o, 16));
                        if (tg == Guid.Empty) continue;
                        long first = BitConverter.ToInt64(ent, o + 32), last = BitConverter.ToInt64(ent, o + 40);
                        string name = Encoding.Unicode.GetString(ent, o + 56, Math.Min(72, size - 56)).TrimEnd('\0');
                        string tgs = tg.ToString().ToUpperInvariant();
                        L.Parts.Add(new PartInfo
                        {
                            Scheme = "GPT", Index = i + 1, TypeCode = tgs, Type = GptTypes.TryGetValue(tgs, out var tn) ? tn : "unknown",
                            Name = name, Guid = new Guid(ent.AsSpan(o + 16, 16)).ToString().ToUpperInvariant(), StartLba = first, Sectors = last - first + 1
                        });
                    }
                    L.Notes.Add("protective MBR (type 0xEE) → GPT header at LBA 1 ('EFI PART')");
                }
                else { L.Scheme = "MBR"; L.Parts = mbrParts; L.Notes.Add("0xEE entry but no GPT header found"); }
            }
            else { L.Scheme = "MBR"; L.Parts = mbrParts; }
            // unpartitioned gaps ≥ 1 MiB (data outside partitions)
            long minGap = Math.Max(1, (1 << 20) / sector);
            long cursor = L.Scheme == "GPT" ? 34 : 1;
            foreach (var p in L.Parts.OrderBy(p => p.StartLba))
            {
                if (p.StartLba - cursor >= minGap) L.Gaps.Add((cursor, p.StartLba - cursor));
                cursor = Math.Max(cursor, p.StartLba + p.Sectors);
            }
            long endUsable = totalSectors > 0 ? (L.Scheme == "GPT" ? totalSectors - 33 : totalSectors) : cursor;
            if (endUsable - cursor >= minGap) L.Gaps.Add((cursor, endUsable - cursor));
            return L;
        }
    }

    /// <summary>Byte and timestamp interpreters (like FTK's Hex Value Interpreter).</summary>
    public static class Interp
    {
        static readonly DateTime Epoch1601 = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly DateTime Unix = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly DateTime Mac2001 = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static string D(DateTime? d) => d == null ? "—" : Fmt.Utc(d.Value);

        public static DateTime? FileTime(long v) { try { return v <= 0 || v > 0x24C85A5ED1C04000 ? null : Epoch1601.AddTicks(v); } catch { return null; } }
        public static DateTime? UnixSec(long v) { try { return v < 0 || v > 32503680000 ? null : Unix.AddSeconds(v); } catch { return null; } }
        public static DateTime? UnixMs(long v) { try { return v < 0 || v > 32503680000000 ? null : Unix.AddMilliseconds(v); } catch { return null; } }
        public static DateTime? WebKit(long v) { try { return v <= 0 ? null : Epoch1601.AddTicks(v * 10); } catch { return null; } }
        public static DateTime? MacAbs(double v) { try { return Mac2001.AddSeconds(v); } catch { return null; } }
        public static DateTime? Dos(uint v)
        {
            int date = (int)(v >> 16), time = (int)(v & 0xFFFF);
            int y = 1980 + (date >> 9), mo = (date >> 5) & 15, d = date & 31, h = time >> 11, mi = (time >> 5) & 63, s = (time & 31) * 2;
            try { return new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc); } catch { return null; }
        }

        public static List<(string k, string v)> Bytes(byte[] b, int off)
        {
            var r = new List<(string, string)>();
            int n = b.Length - off;
            if (n <= 0) return r;
            string X(int len, bool le)
            {
                var s = b.Skip(off).Take(len).ToArray();
                if (!le) Array.Reverse(s);
                return s.Length == len ? "" : null;
            }
            r.Add(("byte / int8", $"{b[off]}  /  {(sbyte)b[off]}  /  0x{b[off]:X2}"));
            if (n >= 2) r.Add(("uint16 LE / BE", $"{BitConverter.ToUInt16(b, off)}  /  {(ushort)((b[off] << 8) | b[off + 1])}"));
            if (n >= 4)
            {
                uint le = BitConverter.ToUInt32(b, off);
                uint be = (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
                r.Add(("uint32 LE / BE", $"{le}  /  {be}"));
                r.Add(("int32 LE", BitConverter.ToInt32(b, off).ToString(CultureInfo.InvariantCulture)));
                r.Add(("float32 LE", BitConverter.ToSingle(b, off).ToString("G9", CultureInfo.InvariantCulture)));
                r.Add(("Unix time (32-bit LE)", D(UnixSec(le))));
                r.Add(("DOS date/time (LE)", D(Dos(le))));
            }
            if (n >= 8)
            {
                long q = BitConverter.ToInt64(b, off);
                r.Add(("uint64 LE", BitConverter.ToUInt64(b, off).ToString(CultureInfo.InvariantCulture)));
                r.Add(("Windows FILETIME (LE)", D(FileTime(q))));
                r.Add(("Unix ms (64-bit LE)", D(UnixMs(q))));
                r.Add(("Chrome/WebKit µs (LE)", D(WebKit(q))));
                r.Add(("double LE", BitConverter.ToDouble(b, off).ToString("G17", CultureInfo.InvariantCulture)));
            }
            if (n >= 16) r.Add(("GUID (LE)", new Guid(b.AsSpan(off, 16)).ToString().ToUpperInvariant()));
            var asc = new string(b.Skip(off).Take(32).Select(c => c >= 32 && c < 127 ? (char)c : '.').ToArray());
            r.Add(("ASCII", asc));
            if (n >= 2) r.Add(("UTF-16LE", Encoding.Unicode.GetString(b, off, Math.Min(64, n - n % 2)).Replace('\0', '·')));
            return r;
        }

        /// <summary>Decodes a typed number (decimal or 0x-hex) as every common timestamp format.</summary>
        public static List<(string k, string v)> Timestamp(string input)
        {
            var r = new List<(string, string)>();
            input = (input ?? "").Trim().Replace(" ", "");
            long v; bool ok;
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var hex = input[2..];
                ok = long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
                if (ok && hex.Length == 16)
                {
                    var bytes = Convert.FromHexString(hex);
                    long le = BitConverter.ToInt64(bytes, 0);
                    r.Add(("FILETIME (bytes as stored, LE)", D(FileTime(le))));
                }
            }
            else ok = long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            if (!ok)
            {
                if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)) r.Add(("Mac absolute (s since 2001)", D(MacAbs(dv))));
                return r;
            }
            r.Add(("Windows FILETIME (100 ns since 1601)", D(FileTime(v))));
            r.Add(("Unix seconds", D(UnixSec(v))));
            r.Add(("Unix milliseconds", D(UnixMs(v))));
            r.Add(("Chrome / WebKit (µs since 1601)", D(WebKit(v))));
            r.Add(("Mac absolute (s since 2001)", D(MacAbs(v))));
            r.Add(("DOS date/time (32-bit)", v >= 0 && v <= uint.MaxValue ? D(Dos((uint)v)) : "—"));
            return r;
        }

        public static string HexDump(byte[] b, long baseOffset, int width = 16)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length; i += width)
            {
                sb.Append((baseOffset + i).ToString("X10", CultureInfo.InvariantCulture)).Append("  ");
                for (int j = 0; j < width; j++)
                {
                    if (i + j < b.Length) sb.Append(b[i + j].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(' ');
                for (int j = 0; j < width && i + j < b.Length; j++) { byte c = b[i + j]; sb.Append(c >= 32 && c < 127 ? (char)c : '.'); }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
