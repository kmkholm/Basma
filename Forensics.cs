using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Basma
{
    /// <summary>Read-only random access to a file or a device (\\.\PhysicalDriveN, \\.\C:).</summary>
    public sealed class Src : IDisposable
    {
        public readonly string Path; public readonly SafeFileHandle H; public readonly long Length; public readonly int Sector; public readonly bool Device;
        public Src(string path)
        {
            Path = path; Device = Disks.IsDevicePath(path);
            if (Device) { H = Native.Open(path, true); (Length, Sector) = Disks.Geometry(H); }
            else { H = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); Length = RandomAccess.GetLength(H); Sector = 512; }
        }
        /// <summary>Reads up to count bytes at offset; unreadable device sectors are zero-filled and reported.</summary>
        public int Read(long offset, byte[] buf, int count, List<long> bad = null)
        {
            count = (int)Math.Max(0, Math.Min(count, Length - offset));
            if (count == 0) return 0;
            try
            {
                int got = 0;
                while (got < count) { int n = RandomAccess.Read(H, new Span<byte>(buf, got, count - got), offset + got); if (n <= 0) break; got += n; }
                if (got < count && Device) throw new IOException("short read");
                return got;
            }
            catch (IOException) when (Device)
            {
                for (int off = 0; off < count; off += Sector)
                {
                    int len = Math.Min(Sector, count - off);
                    try { if (RandomAccess.Read(H, new Span<byte>(buf, off, len), offset + off) < len) throw new IOException(); }
                    catch (IOException) { Array.Clear(buf, off, len); if (bad != null && bad.Count < 10000) bad.Add((offset + off) / Sector); }
                }
                return count;
            }
        }
        public void Dispose() => H.Dispose();
    }

    public static class Forensics
    {
        const int C = 4 << 20;

        // ------------------------------------------------------------------ imaging (dd)
        public sealed class ImageResult
        {
            public string Source, Dest, Started, Finished, Log; public long Size; public double Seconds; public int SectorSize;
            public List<string> Files = new(); public Dictionary<string, string> SourceHash = new(), ImageHash; public List<long> Bad = new();
            public bool? Verified;
        }

        static string DiskOfLetter(string path) => System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))?.Substring(0, 2).ToUpperInvariant();

        /// <summary>Creates a raw (dd) image of a disk, volume or file, hashing the source while reading, optionally split, then verifies the image.</summary>
        public static ImageResult Image(string source, string dest, long splitBytes, IList<string> algos, bool verify, Prog p, string examiner, string notes)
        {
            if (Disks.IsDevicePath(source))
            {
                var letter = DiskOfLetter(dest);
                if (source.StartsWith(@"\\.\", StringComparison.Ordinal) && source.Length == 6 && source.Substring(4, 2).Equals(letter, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("The destination is on the volume being imaged."));
                var m = Regex.Match(source, @"PhysicalDrive(\d+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var d = Disks.List().FirstOrDefault(x => x.Number == int.Parse(m.Groups[1].Value));
                    if (d != null && d.Volumes.Any(v => v.StartsWith(letter, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(L.T("The destination is on the disk being imaged. Write the image to another disk."));
                }
            }
            else if (System.IO.Path.GetFullPath(source).Equals(System.IO.Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("source = destination");
            var r = new ImageResult { Source = source, Dest = dest, Started = Fmt.NowUtc() };
            using var src = new Src(source);
            r.SectorSize = src.Sector; p.Total = src.Length; p.FilesTotal = 1; p.Current = source;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var mh = new MultiHasher(algos);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(dest)));
            int part = 0; long inPart = 0; FileStream fs = null;
            string PartName(int i) => splitBytes > 0 ? $"{dest}.{i:D3}" : dest;
            void NewPart() { fs?.Dispose(); part++; var n = PartName(part); if (File.Exists(n)) throw new IOException(L.T("Destination file already exists: ") + n); fs = new FileStream(n, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20); r.Files.Add(n); inPart = 0; }
            var buf = new byte[C];
            try
            {
                NewPart();
                for (long off = 0; off < src.Length;)
                {
                    if (p.Cancel) throw new CancelledException();
                    int n = src.Read(off, buf, C, r.Bad);
                    if (n <= 0) break;
                    mh.Append(buf, n);
                    int written = 0;
                    while (written < n)
                    {
                        if (splitBytes > 0 && inPart >= splitBytes) NewPart();
                        int w = (int)(splitBytes > 0 ? Math.Min(n - written, splitBytes - inPart) : n - written);
                        fs.Write(buf, written, w); written += w; inPart += w;
                    }
                    off += n; p.Done = off; p.Bad = r.Bad.Count;
                }
            }
            finally { fs?.Dispose(); }
            r.SourceHash = mh.Finish(); r.Size = p.Done; r.Finished = Fmt.NowUtc(); r.Seconds = sw.Elapsed.TotalSeconds;
            if (verify)
            {
                p.Done = 0; p.Current = L.T("verifying image") + " · " + dest;
                var vr = Engine.HashFiles(r.Files, algos, p, "image", false);
                r.ImageHash = vr.H;
                r.Verified = r.SourceHash.All(kv => vr.Get(kv.Key) == kv.Value);
            }
            var sb = new StringBuilder();
            sb.AppendLine($"BASMA v{App.Version} — forensic image log");
            sb.AppendLine($"Examiner: {examiner}");
            if (!string.IsNullOrWhiteSpace(notes)) sb.AppendLine($"Notes: {notes}");
            sb.AppendLine($"Source: {source}");
            var dm = Regex.Match(source, @"PhysicalDrive(\d+)", RegexOptions.IgnoreCase);
            if (dm.Success) { var d = Disks.List().FirstOrDefault(x => x.Number == int.Parse(dm.Groups[1].Value)); if (d != null) sb.AppendLine($"Device: {d.Model} | S/N {d.Serial} | bus {d.Bus} | {d.Size:N0} bytes"); }
            sb.AppendLine($"Sector size: {r.SectorSize}  |  Bytes: {r.Size:N0}");
            sb.AppendLine($"Image format: raw (dd){(splitBytes > 0 ? $", split every {splitBytes:N0} bytes" : "")}");
            foreach (var f in r.Files) sb.AppendLine($"  segment: {f}");
            sb.AppendLine($"Started (UTC): {r.Started}\nFinished (UTC): {r.Finished}\nDuration: {Fmt.Dur(r.Seconds)}");
            sb.AppendLine("Source hashes (computed while reading):");
            foreach (var kv in r.SourceHash) sb.AppendLine($"  {Algos.Label(kv.Key)}: {kv.Value}");
            if (r.ImageHash != null)
            {
                sb.AppendLine("Image hashes (re-read after writing):");
                foreach (var kv in r.ImageHash) sb.AppendLine($"  {Algos.Label(kv.Key)}: {kv.Value}");
                sb.AppendLine($"Verification: {(r.Verified == true ? "MATCH" : "MISMATCH")}");
            }
            sb.AppendLine($"Unreadable sectors (zero-filled): {r.Bad.Count}{(r.Bad.Count > 0 ? " — " + string.Join(", ", r.Bad.Take(200)) : "")}");
            r.Log = (splitBytes > 0 ? dest : dest) + ".txt";
            File.WriteAllText(r.Log, sb.ToString(), new UTF8Encoding(false));
            return r;
        }

        // ------------------------------------------------------------------ keyword / pattern search
        public sealed class Hit { public long Offset; public string Pattern, Text, Context; }
        static readonly Encoding Latin1 = Encoding.Latin1;
        public static readonly (string key, string label, string rx)[] Presets =
        {
            ("email", "E-mail addresses", @"[A-Za-z0-9._%+\-]{1,64}@[A-Za-z0-9.\-]{1,190}\.[A-Za-z]{2,24}"),
            ("url", "URLs", @"https?://[\x21-\x7E]{4,300}"),
            ("ipv4", "IPv4 addresses", @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b"),
            ("btc", "Bitcoin addresses", @"\b(?:bc1[a-z0-9]{25,62}|[13][a-km-zA-HJ-NP-Z1-9]{25,34})\b"),
            ("card", "Payment-card-like numbers", @"\b(?:4\d{3}|5[1-5]\d{2}|3[47]\d{2}|6011)[ -]?\d{4}[ -]?\d{4}[ -]?\d{1,4}\b"),
            ("phone", "Phone numbers (+9627…, 07…)", @"(?:\+9627[789]\d{7}|\b07[789]\d{7}\b|\+9665\d{8}|\b05\d{8}\b)"),
        };

        public static List<Hit> Search(string source, IList<string> keywords, IList<string> hexPatterns, IList<string> presets, bool utf16, long start, long length, Prog p, int max = 20000)
        {
            var rxs = new List<(string label, Regex rx)>();
            foreach (var k in keywords.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                rxs.Add((k, new Regex(Regex.Escape(Latin1.GetString(Encoding.UTF8.GetBytes(k))), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
                if (utf16) rxs.Add((k + " (UTF-16)", new Regex(Regex.Escape(Latin1.GetString(Encoding.Unicode.GetBytes(k))), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
            }
            foreach (var h in hexPatterns.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                var hex = Regex.Replace(h, @"[^0-9A-Fa-f]", "");
                if (hex.Length >= 2 && hex.Length % 2 == 0) rxs.Add(("hex " + hex.ToUpperInvariant(), new Regex(Regex.Escape(Latin1.GetString(Convert.FromHexString(hex))), RegexOptions.CultureInvariant)));
            }
            foreach (var pr in Presets.Where(x => presets.Contains(x.key))) rxs.Add((L.T(pr.label), new Regex(pr.rx, RegexOptions.CultureInvariant)));
            if (rxs.Count == 0) throw new ArgumentException(L.T("Enter a keyword, a hex pattern or choose a preset."));
            using var src = new Src(source);
            long end = length > 0 ? Math.Min(src.Length, start + length) : src.Length;
            start = Math.Max(0, start) / src.Sector * src.Sector;
            p.Total = end - start; p.FilesTotal = 1; p.Current = source;
            const int Ov = 512;
            var hits = new List<Hit>();
            var buf = new byte[C + Ov]; int carry = 0;
            for (long off = start; off < end;)
            {
                if (p.Cancel) throw new CancelledException();
                int want = (int)Math.Min(C, end - off);
                var tmp = new byte[want];
                int n = src.Read(off, tmp, want);
                if (n <= 0) break;
                Buffer.BlockCopy(tmp, 0, buf, carry, n);
                int total = carry + n;
                var s = Latin1.GetString(buf, 0, total);
                long baseOff = off - carry;
                foreach (var (label, rx) in rxs)
                    foreach (Match m in rx.Matches(s))
                    {
                        if (m.Index + m.Length <= carry) continue; // already reported in the previous chunk
                        int cs = Math.Max(0, m.Index - 24), ce = Math.Min(total, m.Index + m.Length + 24);
                        hits.Add(new Hit { Offset = baseOff + m.Index, Pattern = label, Text = Printable(buf, m.Index, m.Length, label.EndsWith("(UTF-16)")), Context = Printable(buf, cs, ce - cs, false) });
                        if (hits.Count >= max) return hits.OrderBy(x => x.Offset).ToList();
                    }
                carry = Math.Min(Ov, total);
                Buffer.BlockCopy(buf, total - carry, buf, 0, carry);
                off += n; p.Done = off - start;
            }
            return hits.OrderBy(x => x.Offset).ToList();
        }

        static string Printable(byte[] b, int off, int len, bool utf16)
        {
            if (utf16) return Encoding.Unicode.GetString(b, off, len - len % 2);
            var sb = new StringBuilder(len);
            for (int i = off; i < off + len; i++) sb.Append(b[i] >= 32 && b[i] < 127 ? (char)b[i] : '.');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ signature scan & carving
        public sealed class Carved { public long Offset; public string Kind, Ext, File, Sha256; public long Size; }
        static readonly (string kind, string ext, byte[] magic, bool carve)[] Heads =
        {
            ("JPEG image", "jpg", new byte[] { 0xFF, 0xD8, 0xFF }, true), ("PNG image", "png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true),
            ("GIF image", "gif", Encoding.ASCII.GetBytes("GIF8"), true), ("PDF document", "pdf", Encoding.ASCII.GetBytes("%PDF-"), true),
            ("ZIP / Office", "zip", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true), ("Windows executable (PE)", "exe", new byte[] { 0x4D, 0x5A }, false),
            ("SQLite database", "sqlite", Encoding.ASCII.GetBytes("SQLite format 3\0"), false), ("OLE2 (doc/xls/msg)", "ole", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, false),
            ("Registry hive", "hive", Encoding.ASCII.GetBytes("regf"), false), ("RAR archive", "rar", Encoding.ASCII.GetBytes("Rar!\x1a\x07"), false),
            ("7-Zip archive", "7z", new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }, false), ("E01 evidence file", "e01", new byte[] { 0x45, 0x56, 0x46, 0x09, 0x0D, 0x0A, 0xFF, 0x00 }, false),
        };
        public static IEnumerable<string> CarveKinds => Heads.Select(h => h.kind);

        public static List<Carved> Carve(string source, IList<string> kinds, bool extract, string outDir, long maxSize, Prog p, int max = 5000)
        {
            using var src = new Src(source);
            var sel = Heads.Where(h => kinds.Contains(h.kind)).ToList();
            p.Total = src.Length; p.FilesTotal = 1; p.Current = source;
            var found = new List<Carved>();
            if (extract) Directory.CreateDirectory(outDir);
            var buf = new byte[C + 16];
            long lastEnd = -1;
            for (long off = 0; off < src.Length;)
            {
                if (p.Cancel) throw new CancelledException();
                int n = src.Read(off, buf, C + 16);
                if (n <= 0) break;
                int scan = Math.Min(n, C);
                for (int i = 0; i < scan; i++)
                {
                    foreach (var h in sel)
                    {
                        if (buf[i] != h.magic[0] || i + h.magic.Length > n) continue;
                        bool ok = true;
                        for (int k = 1; k < h.magic.Length && ok; k++) ok = buf[i + k] == h.magic[k];
                        if (!ok) continue;
                        long at = off + i;
                        if (h.ext == "exe" && !IsPe(src, at)) continue;
                        if (at < lastEnd && h.ext != "exe") continue; // inside a file already carved
                        var c = new Carved { Offset = at, Kind = h.kind, Ext = h.ext };
                        if (h.carve)
                        {
                            long size = FindEnd(src, at, h.ext, maxSize);
                            c.Size = size;
                            if (size > 0)
                            {
                                lastEnd = at + size;
                                if (extract)
                                {
                                    var data = new byte[size];
                                    src.Read(at, data, (int)size);
                                    c.File = System.IO.Path.Combine(outDir, $"{at:D12}_{h.ext}.{h.ext}");
                                    File.WriteAllBytes(c.File, data);
                                    c.Sha256 = Fmt.Hex(SHA256.HashData(data));
                                }
                            }
                        }
                        found.Add(c);
                        if (found.Count >= max) return found;
                    }
                }
                off += scan; p.Done = off;
            }
            return found;
        }

        static bool IsPe(Src s, long at)
        {
            var b = new byte[64];
            if (s.Read(at, b, 64) < 64) return false;
            int lfa = BitConverter.ToInt32(b, 60);
            if (lfa < 64 || lfa > 4096) return false;
            var pe = new byte[4];
            return s.Read(at + lfa, pe, 4) == 4 && pe[0] == 'P' && pe[1] == 'E' && pe[2] == 0 && pe[3] == 0;
        }

        /// <summary>Header-footer carving: returns the length of the object starting at 'at', or 0 if no valid end was found within maxSize.</summary>
        static long FindEnd(Src s, long at, string ext, long maxSize)
        {
            int win = (int)Math.Min(maxSize, s.Length - at);
            var b = new byte[win];
            int n = s.Read(at, b, win);
            switch (ext)
            {
                case "jpg":
                    int depth = 0;
                    for (int i = 0; i + 1 < n; i++)
                    {
                        if (b[i] != 0xFF) continue;
                        if (b[i + 1] == 0xD8) depth++;
                        else if (b[i + 1] == 0xD9 && --depth == 0) return i + 2;
                    }
                    return 0;
                case "png":
                    for (int i = 8; i + 8 <= n; i++) if (b[i] == 'I' && b[i + 1] == 'E' && b[i + 2] == 'N' && b[i + 3] == 'D') return i + 8;
                    return 0;
                case "gif":
                    for (int i = 13; i + 1 < n; i++) if (b[i] == 0x00 && b[i + 1] == 0x3B) return i + 2;
                    return 0;
                case "pdf":
                    long end = 0;
                    for (int i = 5; i + 5 <= n; i++)
                    {
                        if (b[i] == '%' && b[i + 1] == 'P' && b[i + 2] == 'D' && b[i + 3] == 'F' && b[i + 4] == '-') break; // next document
                        if (b[i] == '%' && b[i + 1] == '%' && b[i + 2] == 'E' && b[i + 3] == 'O' && b[i + 4] == 'F') { end = i + 5; while (end < n && (b[end] == '\r' || b[end] == '\n')) end++; }
                    }
                    return end;
                case "zip":
                    for (int i = 4; i + 22 <= n; i++)
                        if (b[i] == 0x50 && b[i + 1] == 0x4B && b[i + 2] == 0x05 && b[i + 3] == 0x06) { int cl = BitConverter.ToUInt16(b, i + 20); return Math.Min(n, i + 22 + cl); }
                    return 0;
            }
            return 0;
        }

        // ------------------------------------------------------------------ strings
        public sealed class StrHit { public long Offset; public string Enc, Text; }
        public static List<StrHit> Strings(string source, int minLen, bool utf16, long start, long length, Prog p, int max = 200000)
        {
            using var src = new Src(source);
            long end = length > 0 ? Math.Min(src.Length, start + length) : src.Length;
            start = Math.Max(0, start) / src.Sector * src.Sector;
            p.Total = end - start; p.Current = source;
            var res = new List<StrHit>();
            var buf = new byte[C];
            var asc = new StringBuilder(); long ascAt = 0;
            var uni = new StringBuilder(); long uniAt = 0; int uniPhase = 0;
            static bool P(byte c) => (c >= 32 && c < 127) || c == 9;
            for (long off = start; off < end;)
            {
                if (p.Cancel) throw new CancelledException();
                int n = src.Read(off, buf, (int)Math.Min(C, end - off));
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    byte c = buf[i]; long at = off + i;
                    if (P(c)) { if (asc.Length == 0) ascAt = at; asc.Append((char)c); }
                    else { if (asc.Length >= minLen) { res.Add(new StrHit { Offset = ascAt, Enc = "ASCII", Text = asc.ToString() }); if (res.Count >= max) return res; } asc.Clear(); }
                    if (utf16)
                    {
                        // UTF-16LE printable: char byte then 0x00
                        if (uniPhase == 0) { if (P(c)) { if (uni.Length == 0) uniAt = at; uni.Append((char)c); uniPhase = 1; } else Flush(); }
                        else { if (c == 0) uniPhase = 0; else { Flush(); if (P(c)) { uniAt = at; uni.Append((char)c); uniPhase = 1; } } }
                    }
                }
                off += n; p.Done = off - start;
            }
            void Flush() { if (uni.Length >= minLen && res.Count < max) res.Add(new StrHit { Offset = uniAt, Enc = "UTF-16LE", Text = uni.ToString() }); uni.Clear(); uniPhase = 0; }
            if (asc.Length >= minLen) res.Add(new StrHit { Offset = ascAt, Enc = "ASCII", Text = asc.ToString() });
            Flush();
            return res.OrderBy(x => x.Offset).ToList();
        }

        // ------------------------------------------------------------------ entropy map
        public static (long block, List<double> e) EntropyMap(string source, Prog p, int blocks = 800)
        {
            using var src = new Src(source);
            long block = Math.Max(4096, (src.Length / blocks + 4095) / 4096 * 4096);
            int sample = (int)Math.Min(block, 1 << 20);
            p.Total = src.Length; p.Current = source;
            var res = new List<double>();
            var buf = new byte[sample];
            for (long off = 0; off < src.Length; off += block)
            {
                if (p.Cancel) throw new CancelledException();
                int n = src.Read(off, buf, sample);
                res.Add(Engine.Entropy(buf, n));
                p.Done = Math.Min(src.Length, off + block);
            }
            return (block, res);
        }

        // ------------------------------------------------------------------ wipe verification
        public sealed class WipeResult { public long Size, ZeroSectors, NonZeroSectors, FfSectors; public int Sector; public List<long> FirstNonZero = new(); public List<long> Bad = new(); }
        public static WipeResult WipeCheck(string source, Prog p)
        {
            using var src = new Src(source);
            var r = new WipeResult { Size = src.Length, Sector = src.Sector };
            p.Total = src.Length; p.Current = source;
            var buf = new byte[C];
            for (long off = 0; off < src.Length;)
            {
                if (p.Cancel) throw new CancelledException();
                int n = src.Read(off, buf, C, r.Bad);
                if (n <= 0) break;
                for (int s = 0; s < n; s += r.Sector)
                {
                    int len = Math.Min(r.Sector, n - s);
                    var span = new ReadOnlySpan<byte>(buf, s, len);
                    if (span.IndexOfAnyExcept((byte)0) < 0) r.ZeroSectors++;
                    else if (span.IndexOfAnyExcept((byte)0xFF) < 0) r.FfSectors++;
                    else { r.NonZeroSectors++; if (r.FirstNonZero.Count < 50) r.FirstNonZero.Add((off + s) / r.Sector); }
                }
                off += n; p.Done = off;
            }
            return r;
        }

        // ------------------------------------------------------------------ checksum files (md5sum / sha1sum / sha256sum)
        public static string ChecksumText(HashResult r, string algo)
        {
            var sb = new StringBuilder();
            if (r.Rows != null) foreach (var x in r.Rows.Where(x => x.Get(algo) != null)) sb.Append(x.Get(algo)).Append(" *").Append(x.Path).Append('\n');
            else if (r.Get(algo) != null) sb.Append(r.Get(algo)).Append(" *").Append(System.IO.Path.GetFileName(r.Target)).Append('\n');
            return sb.ToString();
        }

        public sealed class CheckLine { public string Path, Expected, Actual, Algo, Status; }
        public static List<CheckLine> VerifyChecksums(string file, Prog p)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file));
            var items = new List<CheckLine>();
            foreach (var raw in File.ReadAllLines(file))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#") || l.StartsWith(";")) continue;
                var m = Regex.Match(l, @"^([0-9A-Fa-f]{8,128})\s+\*?(.+)$");
                var bsd = Regex.Match(l, @"^(MD5|SHA1|SHA256|SHA384|SHA512)\s*\((.+)\)\s*=\s*([0-9A-Fa-f]+)$", RegexOptions.IgnoreCase);
                if (m.Success) items.Add(new CheckLine { Expected = m.Groups[1].Value.ToLowerInvariant(), Path = m.Groups[2].Value.Trim() });
                else if (bsd.Success) items.Add(new CheckLine { Expected = bsd.Groups[3].Value.ToLowerInvariant(), Path = bsd.Groups[2].Value.Trim() });
            }
            p.FilesTotal = items.Count;
            foreach (var it in items)
            {
                if (p.Cancel) throw new CancelledException();
                it.Algo = Algos.ByLength(it.Expected.Length).FirstOrDefault() ?? "?";
                var full = System.IO.Path.IsPathRooted(it.Path) ? it.Path : System.IO.Path.Combine(dir, it.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                p.Current = it.Path;
                if (!File.Exists(full)) { it.Status = "MISSING"; p.FilesDone++; continue; }
                if (it.Algo == "?") { it.Status = "UNKNOWN"; p.FilesDone++; continue; }
                var r = Engine.HashFiles(new[] { full }, new[] { it.Algo }, p, "file", false);
                it.Actual = r.Get(it.Algo);
                it.Status = it.Actual == it.Expected ? "OK" : "FAILED";
                p.FilesDone++;
            }
            return items;
        }

        // ------------------------------------------------------------------ USB device history (registry, read-only)
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegQueryInfoKey(SafeRegistryHandle h, IntPtr cls, IntPtr clsLen, IntPtr res, IntPtr subKeys, IntPtr maxSub, IntPtr maxCls, IntPtr values, IntPtr maxName, IntPtr maxData, IntPtr sec, out long lastWrite);

        public sealed class UsbDev { public string Vendor, Product, Revision, Serial, Friendly, LastWrite, Class; }
        public static List<UsbDev> UsbHistory()
        {
            var res = new List<UsbDev>();
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR");
            if (root == null) return res;
            foreach (var cls in root.GetSubKeyNames())
            {
                using var ck = root.OpenSubKey(cls);
                if (ck == null) continue;
                var parts = cls.Split('&');
                string P(string pre) => parts.FirstOrDefault(x => x.StartsWith(pre, StringComparison.OrdinalIgnoreCase))?.Substring(pre.Length).Replace('_', ' ');
                foreach (var serial in ck.GetSubKeyNames())
                {
                    using var sk = ck.OpenSubKey(serial);
                    string lw = null;
                    try { if (sk != null && RegQueryInfoKey(sk.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out long ft) == 0) lw = Fmt.Utc(DateTime.FromFileTimeUtc(ft)); } catch { }
                    res.Add(new UsbDev
                    {
                        Class = parts[0], Vendor = P("Ven_"), Product = P("Prod_"), Revision = P("Rev_"), Serial = serial.Length > 1 && serial[1] == '&' ? serial + " (" + L.T("generated by Windows — device has no serial") + ")" : serial.Split('&')[0],
                        Friendly = sk?.GetValue("FriendlyName") as string, LastWrite = lw
                    });
                }
            }
            return res.OrderByDescending(x => x.LastWrite).ToList();
        }
    }
}
