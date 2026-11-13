using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Basma
{
    public static class Algos
    {
        public static readonly string[] All = { "md5", "sha1", "sha256", "sha384", "sha512", "sha3_256", "crc32" };
        public static readonly string[] Default = { "md5", "sha1", "sha256" };
        public static string Label(string a) => a switch { "sha3_256" => "SHA3-256", "crc32" => "CRC32", _ => a.ToUpperInvariant().Replace("SHA", "SHA-") };
        public static bool Supported(string a) => a != "sha3_256" || SHA3_256.IsSupported;
        public static IEnumerable<string> ByLength(int len) => len switch
        {
            8 => new[] { "crc32" }, 32 => new[] { "md5" }, 40 => new[] { "sha1" }, 64 => new[] { "sha256", "sha3_256" },
            96 => new[] { "sha384" }, 128 => new[] { "sha512" }, _ => Array.Empty<string>()
        };
    }

    public sealed class Crc32
    {
        static readonly uint[] T = Enumerable.Range(0, 256).Select(i =>
        {
            uint c = (uint)i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();
        uint c = 0xFFFFFFFF;
        public void Append(byte[] b, int n) { uint x = c; for (int i = 0; i < n; i++) x = T[(x ^ b[i]) & 0xFF] ^ (x >> 8); c = x; }
        public string Hex => (~c).ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>Feeds one byte stream to several algorithms at once (in parallel per chunk).</summary>
    public sealed class MultiHasher : IDisposable
    {
        readonly Dictionary<string, IncrementalHash> inc = new();
        readonly Crc32 crc;
        public MultiHasher(IEnumerable<string> algos)
        {
            foreach (var a in algos.Distinct())
            {
                if (a == "crc32") { crc = new Crc32(); continue; }
                if (!Algos.Supported(a)) continue;
                inc[a] = IncrementalHash.CreateHash(a switch
                {
                    "md5" => HashAlgorithmName.MD5, "sha1" => HashAlgorithmName.SHA1, "sha256" => HashAlgorithmName.SHA256,
                    "sha384" => HashAlgorithmName.SHA384, "sha512" => HashAlgorithmName.SHA512, "sha3_256" => HashAlgorithmName.SHA3_256,
                    _ => throw new ArgumentException(a)
                });
            }
        }
        public void Append(byte[] buf, int n)
        {
            if (n <= 0) return;
            var jobs = inc.Values.Select(h => (Action)(() => h.AppendData(buf, 0, n))).ToList();
            if (crc != null) jobs.Add(() => crc.Append(buf, n));
            if (jobs.Count == 1 || n < 65536) foreach (var j in jobs) j();
            else Parallel.Invoke(jobs.ToArray());
        }
        public Dictionary<string, string> Finish()
        {
            var r = inc.ToDictionary(k => k.Key, k => Fmt.Hex(k.Value.GetHashAndReset()));
            if (crc != null) r["crc32"] = crc.Hex;
            return r;
        }
        public void Dispose() { foreach (var h in inc.Values) h.Dispose(); }
    }

    public sealed class Prog
    {
        public long Done, Total;
        public int FilesDone, FilesTotal;
        public volatile string Current = "";
        public volatile bool Cancel;
        public readonly Stopwatch SW = Stopwatch.StartNew();
        public long Bad;
        public double Speed => SW.Elapsed.TotalSeconds > 0.2 ? Interlocked.Read(ref Done) / SW.Elapsed.TotalSeconds : 0;
        public double? Eta => Speed > 0 && Total > 0 ? (Total - Done) / Speed : null;
        public double Pct => Total > 0 ? 100.0 * Done / Total : (FilesTotal > 0 ? 100.0 * FilesDone / FilesTotal : 0);
    }

    public sealed class CancelledException : Exception { public CancelledException() : base("cancelled") { } }

    public sealed class SigInfo { public string Ext, Detected, Status; }

    public sealed class KnownHit { public string Set, Category, Label; }

    public sealed class FileRow
    {
        public string Path; public long Size;
        public Dictionary<string, string> H = new();
        public string Created, Modified, Accessed, SigDetected, SigStatus, Error;
        public double Entropy; public bool HighEntropy; public int Duplicates;
        public KnownHit Known;
        public string Get(string a) => H.TryGetValue(a, out var v) ? v : null;
    }

    public sealed class HashResult
    {
        public string Target, TargetKind; // file | folder | split-image | disk | volume | text
        public long Size; public int FileCount;
        public string Started, Finished; public double Seconds;
        public Dictionary<string, string> H = new();
        public List<string> Algos = new();
        public SigInfo Sig; public double Entropy;
        public List<long> BadSectors = new();
        public long StartLba = -1, CountLba = -1; public int SectorSize;
        public List<string> Parts;
        public string Note;
        public List<FileRow> Rows;
        public List<string> Errors = new();
        public Dictionary<string, int> Stats;
        public string Get(string a) => H.TryGetValue(a, out var v) ? v : null;
    }

    public static class Engine
    {
        public const int Chunk = 4 << 20;
        public const int EntropySample = 256 * 1024;
        static readonly HashSet<string> Zipx = new(new[] { "zip", "docx", "xlsx", "pptx", "docm", "xlsm", "pptm", "jar", "apk", "odt", "ods", "odp", "epub", "xpi", "kmz", "vsdx", "nupkg", "whl", "aar", "ipa", "3mf" });
        public static readonly (byte[] magic, int off, string kind, HashSet<string> exts)[] Magic =
        {
            (new byte[]{0xFF,0xD8,0xFF},0,"jpeg",new(){"jpg","jpeg","jpe","jfif"}),
            (new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A},0,"png",new(){"png"}),
            (Encoding.ASCII.GetBytes("GIF87a"),0,"gif",new(){"gif"}),
            (Encoding.ASCII.GetBytes("GIF89a"),0,"gif",new(){"gif"}),
            (Encoding.ASCII.GetBytes("%PDF"),0,"pdf",new(){"pdf","ai"}),
            (new byte[]{0x50,0x4B,0x03,0x04},0,"zip",Zipx),
            (new byte[]{0x50,0x4B,0x05,0x06},0,"zip",Zipx),
            (new byte[]{0x52,0x61,0x72,0x21,0x1A,0x07},0,"rar",new(){"rar"}),
            (new byte[]{0x37,0x7A,0xBC,0xAF,0x27,0x1C},0,"7z",new(){"7z"}),
            (new byte[]{0x1F,0x8B},0,"gzip",new(){"gz","tgz"}),
            (new byte[]{0x4D,0x5A},0,"pe-executable",new(){"exe","dll","sys","scr","ocx","cpl","com","efi","drv","mui","pyd","node","winmd","ax"}),
            (new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1},0,"ole2",new(){"doc","xls","ppt","msi","msg","vsd","db","pub","msp","mst"}),
            (new byte[]{0x7F,0x45,0x4C,0x46},0,"elf",new(){"","so","elf","bin","o","ko"}),
            (Encoding.ASCII.GetBytes("SQLite format 3\0"),0,"sqlite",new(){"sqlite","sqlite3","db","db3","sqlitedb"}),
            (new byte[]{0x45,0x56,0x46,0x09,0x0D,0x0A,0xFF,0x00},0,"ewf-e01",new(){"e01","e02","e03","s01"}),
            (Encoding.ASCII.GetBytes("ID3"),0,"mp3",new(){"mp3"}),
            (Encoding.ASCII.GetBytes("OggS"),0,"ogg",new(){"ogg","oga","ogv","opus"}),
            (Encoding.ASCII.GetBytes("RIFF"),0,"riff",new(){"wav","avi","webp","ani","cda"}),
            (Encoding.ASCII.GetBytes("ftyp"),4,"iso-media",new(){"mp4","m4a","m4v","mov","3gp","heic","heif","avif","m4b"}),
            (new byte[]{0x1A,0x45,0xDF,0xA3},0,"matroska",new(){"mkv","webm","mka"}),
            (Encoding.ASCII.GetBytes("{\\rtf"),0,"rtf",new(){"rtf","doc"}),
            (Encoding.ASCII.GetBytes("regf"),0,"registry-hive",new(){"","dat","hve","hiv","log1","log2","sav"}),
            (new byte[]{0x4C,0,0,0,0x01,0x14,0x02,0},0,"windows-lnk",new(){"lnk"}),
            (Encoding.ASCII.GetBytes("MSCF"),0,"cab",new(){"cab","msu"}),
            (Encoding.ASCII.GetBytes("BM"),0,"bmp",new(){"bmp","dib"}),
            (new byte[]{0,0,1,0},0,"ico",new(){"ico"}),
            (Encoding.ASCII.GetBytes("wOF2"),0,"woff2",new(){"woff2"}),
            (Encoding.ASCII.GetBytes("wOFF"),0,"woff",new(){"woff"}),
            (new byte[]{0x4D,0x53,0x46,0x54},0,"windows-memdump?",new(){"dmp"}),
        };
        static readonly Dictionary<string, string> ExtClaims = BuildClaims();
        public static readonly HashSet<string> Compressed = new(new[] { "jpeg", "png", "gif", "pdf", "zip", "rar", "7z", "gzip", "mp3", "ogg", "iso-media", "matroska", "cab", "woff", "woff2", "ewf-e01" });

        static Dictionary<string, string> BuildClaims()
        {
            var d = new Dictionary<string, string>();
            foreach (var m in Magic)
                foreach (var e in m.exts)
                    if (e != "" && !new[] { "ole2", "riff", "zip", "elf", "registry-hive", "sqlite", "windows-memdump?" }.Contains(m.kind) && !d.ContainsKey(e)) d[e] = m.kind;
            foreach (var e in new[] { "docx", "xlsx", "pptx", "zip", "apk", "jar", "epub" }) d[e] = "zip";
            foreach (var e in new[] { "doc", "xls", "ppt", "msi", "msg" }) d.TryAdd(e, "ole2");
            return d;
        }

        public static string Sniff(byte[] head)
        {
            foreach (var m in Magic)
            {
                if (head.Length < m.off + m.magic.Length) continue;
                bool ok = true;
                for (int i = 0; i < m.magic.Length && ok; i++) ok = head[m.off + i] == m.magic[i];
                if (ok) return m.kind;
            }
            return null;
        }

        public static SigInfo Signature(string name, byte[] head, long size)
        {
            string ext = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (size == 0) return new SigInfo { Ext = ext, Status = "empty" };
            string kind = Sniff(head);
            var allowed = new HashSet<string>();
            foreach (var m in Magic) if (m.kind == kind) allowed.UnionWith(m.exts);
            string st = kind != null && !allowed.Contains(ext) ? "mismatch"
                : kind == null && ExtClaims.ContainsKey(ext) ? "mismatch"
                : kind != null ? "ok" : "unknown";
            return new SigInfo { Ext = ext, Detected = kind, Status = st };
        }

        public static double Entropy(byte[] b, int n)
        {
            if (n <= 0) return 0;
            var c = new long[256];
            for (int i = 0; i < n; i++) c[b[i]]++;
            double e = 0;
            foreach (var x in c) if (x > 0) { double p = (double)x / n; e -= p * Math.Log2(p); }
            return Math.Round(e, 3);
        }

        static int ReadFull(Stream s, byte[] buf, int want)
        {
            int got = 0, n;
            while (got < want && (n = s.Read(buf, got, want - got)) > 0) got += n;
            return got;
        }

        /// <summary>Double-buffered pump: reads chunk k+1 while chunk k is being hashed.</summary>
        static void Pump(Func<byte[], int> read, MultiHasher mh, Prog p, Action<byte[], int> first, int size = Chunk)
        {
            byte[] a = new byte[size], b = new byte[size];
            int n = read(a);
            bool isFirst = true;
            while (n > 0)
            {
                if (p.Cancel) throw new CancelledException();
                if (isFirst) { first?.Invoke(a, n); isFirst = false; }
                var cur = a; int len = n;
                var t = Task.Run(() => mh.Append(cur, len));
                (a, b) = (b, a);
                n = read(a);
                t.Wait();
                Interlocked.Add(ref p.Done, len);
            }
        }

        public static List<string> SplitParts(string path)
        {
            var m = Regex.Match(path, @"^(?<base>.+)\.(?<num>\d{3})$");
            if (!m.Success) return null;
            var parts = new List<string>();
            for (int i = 1; ; i++)
            {
                string p = $"{m.Groups["base"].Value}.{i:D3}";
                if (!File.Exists(p)) break;
                parts.Add(p);
            }
            return parts.Count > 0 ? parts : null;
        }

        static (string m, string a, string c) Times(string path)
        {
            var fi = new FileInfo(path);
            return (Fmt.Utc(fi.LastWriteTimeUtc), Fmt.Utc(fi.LastAccessTimeUtc), Fmt.Utc(fi.CreationTimeUtc));
        }

        /// <summary>Hash one or more files as one continuous stream.</summary>
        public static HashResult HashFiles(IList<string> paths, IEnumerable<string> algos, Prog p, string kind, bool own = true)
        {
            var algo = algos.Where(Algos.Supported).ToList();
            var r = new HashResult { Target = System.IO.Path.GetFullPath(paths[0]), TargetKind = kind, Started = Fmt.NowUtc(), Algos = algo };
            long total = paths.Sum(x => new FileInfo(x).Length);
            long before = p.Done;
            if (own) { p.Total = total; p.FilesTotal = paths.Count; }
            byte[] head = Array.Empty<byte>(); byte[] sample = null; int sn = 0;
            using var mh = new MultiHasher(algo);
            var sw = Stopwatch.StartNew();
            int idx = 0; Stream cur = null;
            try
            {
                Func<byte[], int> read = buf =>
                {
                    int filled = 0;
                    while (filled < buf.Length)
                    {
                        if (cur == null)
                        {
                            if (idx >= paths.Count) break;
                            p.Current = paths[idx];
                            cur = new FileStream(paths[idx], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                            idx++;
                        }
                        int n = cur.Read(buf, filled, buf.Length - filled);
                        if (n == 0) { cur.Dispose(); cur = null; if (own) p.FilesDone++; continue; }
                        filled += n;
                    }
                    return filled;
                };
                Pump(read, mh, p, (b, n) =>
                {
                    head = b.Take(Math.Min(64, n)).ToArray();
                    sn = Math.Min(n, EntropySample); sample = b.Take(sn).ToArray();
                }, (int)Math.Max(65536, Math.Min(Chunk, total + 1)));
            }
            finally { cur?.Dispose(); }
            r.H = mh.Finish();
            r.Size = p.Done - before; r.FileCount = paths.Count; r.Finished = Fmt.NowUtc(); r.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 3);
            r.Sig = Signature(paths[0], head, r.Size);
            r.Entropy = sample == null ? 0 : Entropy(sample, sn);
            if (paths.Count > 1) r.Parts = paths.Select(System.IO.Path.GetFileName).ToList();
            var ext = System.IO.Path.GetExtension(paths[0]).ToLowerInvariant();
            if (ext == ".e01" || ext == ".ex01") r.Note = "e01-container";
            return r;
        }

        public static HashResult HashFile(string path, IEnumerable<string> algos, Prog p, bool split = false)
        {
            var parts = split ? SplitParts(path) : null;
            return HashFiles(parts ?? new List<string> { path }, algos, p, parts != null ? "split-image" : "file");
        }

        /// <summary>Hash a physical disk or volume (or a sector range of it). Unreadable sectors are zero-filled and logged.</summary>
        public static HashResult HashDevice(string device, IEnumerable<string> algos, Prog p, long startLba = 0, long countLba = -1)
        {
            var algo = algos.Where(Algos.Supported).ToList();
            using var h = Native.Open(device, true);
            var (size, sector) = Disks.Geometry(h);
            if (size <= 0) throw new IOException(L.T("Could not read the device size."));
            long totalSectors = size / sector;
            if (startLba < 0 || startLba >= totalSectors) throw new ArgumentOutOfRangeException(nameof(startLba));
            long count = countLba > 0 ? Math.Min(countLba, totalSectors - startLba) : totalSectors - startLba;
            long pos = startLba * sector, end = pos + count * sector;
            var r = new HashResult
            {
                Target = device, TargetKind = device.Contains("PhysicalDrive", StringComparison.OrdinalIgnoreCase) ? "disk" : "volume",
                Started = Fmt.NowUtc(), Algos = algo, SectorSize = sector, StartLba = startLba, CountLba = count
            };
            p.Total = end - pos; p.FilesTotal = 1; p.Current = device;
            using var mh = new MultiHasher(algo);
            var sw = Stopwatch.StartNew();
            int chunk = Chunk / sector * sector;
            byte[] head = null;
            Func<byte[], int> read = buf =>
            {
                long remain = end - pos;
                if (remain <= 0) return 0;
                int want = (int)Math.Min(chunk, remain);
                try
                {
                    int got = 0;
                    while (got < want)
                    {
                        int n = RandomAccess.Read(h, new Span<byte>(buf, got, want - got), pos + got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got < want) throw new IOException("short read");
                }
                catch (IOException)
                {
                    for (int off = 0; off < want; off += sector)
                    {
                        try
                        {
                            int n = RandomAccess.Read(h, new Span<byte>(buf, off, sector), pos + off);
                            if (n < sector) throw new IOException("short");
                        }
                        catch (IOException)
                        {
                            Array.Clear(buf, off, sector);
                            lock (r.BadSectors) { if (r.BadSectors.Count < 10000) r.BadSectors.Add((pos + off) / sector); }
                            Interlocked.Increment(ref p.Bad);
                        }
                    }
                }
                pos += want;
                return want;
            };
            Pump(read, mh, p, (b, n) => head = b.Take(Math.Min(512, n)).ToArray());
            r.H = mh.Finish();
            r.Size = p.Done; r.FileCount = 1; r.Finished = Fmt.NowUtc(); r.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 3);
            return r;
        }

        public sealed class Filter
        {
            public string[] Patterns = Array.Empty<string>(); public bool SkipHidden, SkipSystem; public long MaxBytes;
            Regex rx;
            public static Filter Parse(string patterns, bool hidden, bool system, long maxMb)
            {
                var f = new Filter { SkipHidden = hidden, SkipSystem = system, MaxBytes = maxMb > 0 ? maxMb << 20 : 0 };
                f.Patterns = (patterns ?? "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Patterns.Length > 0) f.rx = new Regex("^(?:" + string.Join("|", f.Patterns.Select(x => Regex.Escape(x).Replace(@"\*", ".*").Replace(@"\?", "."))) + ")$", RegexOptions.IgnoreCase);
                return f;
            }
            public bool Active => Patterns.Length > 0 || SkipHidden || SkipSystem || MaxBytes > 0;
            public bool SkipAttr(FileAttributes a) => (SkipHidden && a.HasFlag(FileAttributes.Hidden)) || (SkipSystem && a.HasFlag(FileAttributes.System));
            public bool Accept(FileInfo fi) => !SkipAttr(fi.Attributes) && (rx == null || rx.IsMatch(fi.Name)) && (MaxBytes <= 0 || fi.Length <= MaxBytes);
            public override string ToString() => Active ? $"filter: {(Patterns.Length > 0 ? string.Join(";", Patterns) : "*")}{(SkipHidden ? " -hidden" : "")}{(SkipSystem ? " -system" : "")}{(MaxBytes > 0 ? $" <= {MaxBytes >> 20} MB" : "")}" : "";
        }

        public static List<(string rel, string full)> Walk(string root, List<string> errors, Filter filter = null)
        {
            var outp = new List<(string, string)>();
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(d).EnumerateFileSystemInfos().ToList(); }
                catch (Exception e) { errors.Add($"{System.IO.Path.GetRelativePath(root, d)}: {e.Message}"); continue; }
                foreach (var e in entries)
                {
                    try
                    {
                        if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (e is DirectoryInfo) { if (filter == null || !filter.SkipAttr(e.Attributes)) stack.Push(e.FullName); }
                        else if (filter == null || filter.Accept((FileInfo)e)) outp.Add((System.IO.Path.GetRelativePath(root, e.FullName).Replace('\\', '/'), e.FullName));
                    }
                    catch (Exception ex) { errors.Add($"{e.Name}: {ex.Message}"); }
                }
            }
            outp.Sort((x, y) => string.CompareOrdinal(x.Item1, y.Item1));
            return outp;
        }

        /// <summary>Folder digest = SHA-256 over sorted "relpath\tsize\tsha256\n" lines (same rule as ISNAD).</summary>
        public static string FolderDigest(IEnumerable<FileRow> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows) sb.Append(r.Path).Append('\t').Append(r.Size.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(r.Get("sha256") ?? "ERROR").Append('\n');
            return Fmt.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static HashResult HashFolder(string root, IEnumerable<string> algos, Prog p, KnownSets known, Filter filter = null)
        {
            var algo = algos.Where(Algos.Supported).ToList();
            if (!algo.Contains("sha256")) algo.Add("sha256");
            root = System.IO.Path.GetFullPath(root);
            var r = new HashResult { Target = root, TargetKind = "folder", Started = Fmt.NowUtc(), Algos = algo, Rows = new() };
            var sw = Stopwatch.StartNew();
            p.Current = L.T("listing files…") + " " + root;
            var files = Walk(root, r.Errors, filter);
            if (filter?.Active == true) r.Note = filter.ToString();
            var sizes = files.ToDictionary(f => f.rel, f => { try { return new FileInfo(f.full).Length; } catch { return 0L; } });
            p.FilesTotal = files.Count; p.Total = sizes.Values.Sum();
            foreach (var (rel, full) in files)
            {
                if (p.Cancel) throw new CancelledException();
                var row = new FileRow { Path = rel, Size = sizes[rel] };
                try
                {
                    p.Current = rel;
                    var fr = HashFiles(new[] { full }, algo, p, "file", false);
                    row.H = fr.H; row.Size = fr.Size;
                    (row.Modified, row.Accessed, row.Created) = Times(full);
                    row.SigDetected = fr.Sig.Detected; row.SigStatus = fr.Sig.Status;
                    row.Entropy = fr.Entropy;
                    row.HighEntropy = fr.Entropy >= 7.5 && fr.Size >= 4096 && !Compressed.Contains(fr.Sig.Detected ?? "");
                }
                catch (CancelledException) { throw; }
                catch (Exception e)
                {
                    var msg = e.Message;
                    row.Error = msg; r.Errors.Add($"{rel}: {msg}");
                }
                r.Rows.Add(row);
                p.FilesDone++;
            }
            var bySha = r.Rows.Where(x => x.Get("sha256") != null).GroupBy(x => x.Get("sha256")).ToDictionary(g => g.Key, g => g.Count());
            foreach (var row in r.Rows)
            {
                row.Duplicates = row.Get("sha256") is string s ? bySha[s] - 1 : 0;
                if (known != null) foreach (var a in new[] { "md5", "sha1", "sha256" }) { var hit = row.Get(a) is string hv ? known.Lookup(hv) : null; if (hit != null) { row.Known = hit; break; } }
            }
            r.H = new Dictionary<string, string> { ["sha256"] = FolderDigest(r.Rows) };            r.Size = r.Rows.Sum(x => x.Size); r.FileCount = r.Rows.Count;
            r.Finished = Fmt.NowUtc(); r.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 3);
            r.Stats = new Dictionary<string, int>
            {
                ["files"] = r.Rows.Count, ["errors"] = r.Errors.Count,
                ["sig_mismatch"] = r.Rows.Count(x => x.SigStatus == "mismatch"),
                ["high_entropy"] = r.Rows.Count(x => x.HighEntropy),
                ["known_bad"] = r.Rows.Count(x => x.Known?.Category == "bad"),
                ["known_good"] = r.Rows.Count(x => x.Known?.Category == "good"),
                ["known_notable"] = r.Rows.Count(x => x.Known?.Category == "notable"),
                ["duplicate_sets"] = bySha.Values.Count(v => v > 1),
            };
            return r;
        }

        public static HashResult HashText(string text, IEnumerable<string> algos)
        {
            var b = Encoding.UTF8.GetBytes(text ?? "");
            using var mh = new MultiHasher(algos.Where(Algos.Supported));
            mh.Append(b, b.Length);
            return new HashResult { Target = "text", TargetKind = "text", Size = b.Length, H = mh.Finish(), Started = Fmt.NowUtc(), Finished = Fmt.NowUtc(), Algos = algos.ToList() };
        }

        // ------------------------------------------------------------------ manifests
        public static readonly string[] Cols = { "path", "size", "md5", "sha1", "sha256", "sha512", "created", "modified", "accessed", "sig_detected", "sig_status", "entropy", "high_entropy", "duplicates", "known_category", "known_label", "error", "sha384", "sha3_256", "crc32" };

        static string Csv(string s) => s == null ? "" : (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);

        public static string ManifestCsv(HashResult r)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", Cols)).Append('\n');
            foreach (var x in r.Rows)
            {
                var v = new[] { x.Path, x.Size.ToString(CultureInfo.InvariantCulture), x.Get("md5"), x.Get("sha1"), x.Get("sha256"), x.Get("sha512"), x.Created, x.Modified, x.Accessed,
                    x.SigDetected, x.SigStatus, x.Entropy.ToString(CultureInfo.InvariantCulture), x.HighEntropy ? "True" : "False", x.Duplicates.ToString(CultureInfo.InvariantCulture),
                    x.Known?.Category, x.Known?.Label, x.Error, x.Get("sha384"), x.Get("sha3_256"), x.Get("crc32") };
                sb.Append(string.Join(",", v.Select(Csv))).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Writes manifest CSV (UTF-8 with BOM) and returns its SHA-256.</summary>
        public static string WriteManifest(HashResult r, string path)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var data = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(ManifestCsv(r))).ToArray();
            File.WriteAllBytes(path, data);
            return Fmt.Hex(SHA256.HashData(data));
        }

        public static List<string> ParseCsvLine(string line)
        {
            var res = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res;
        }

        /// <summary>Reads a Basma or ISNAD manifest CSV.</summary>
        public static List<FileRow> ReadManifest(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0) return new List<FileRow>();
            var head = ParseCsvLine(lines[0].TrimStart('﻿'));
            int I(string c) => head.IndexOf(c);
            var rows = new List<FileRow>();
            foreach (var l in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                var v = ParseCsvLine(l);
                string G(string c) { int i = I(c); return i >= 0 && i < v.Count && v[i] != "" ? v[i] : null; }
                var row = new FileRow { Path = G("path"), Size = long.TryParse(G("size"), out var sz) ? sz : 0, Modified = G("modified"), Created = G("created"), Error = G("error") };
                foreach (var a in Algos.All) if (G(a) is string hv) row.H[a] = hv.ToLowerInvariant();
                rows.Add(row);
            }
            return rows;
        }

        // ------------------------------------------------------------------ comparisons
        public sealed class FolderDiff
        {
            public List<string> Identical = new(), Modified = new(), OnlyA = new(), OnlyB = new();
            public List<(string from, string to)> Moved = new();
            public bool Same => Modified.Count == 0 && OnlyA.Count == 0 && OnlyB.Count == 0 && Moved.Count == 0;
        }

        public static FolderDiff Diff(IEnumerable<FileRow> a, IEnumerable<FileRow> b, string algo = "sha256")
        {
            var A = a.ToDictionary(x => x.Path, x => x.Get(algo));
            var B = b.ToDictionary(x => x.Path, x => x.Get(algo));
            var d = new FolderDiff();
            foreach (var k in A.Keys.Intersect(B.Keys).OrderBy(x => x, StringComparer.Ordinal))
                (A[k] == B[k] ? d.Identical : d.Modified).Add(k);
            var onlyA = A.Keys.Except(B.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var onlyB = B.Keys.Except(A.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var bByHash = onlyB.Where(k => B[k] != null).GroupBy(k => B[k]).ToDictionary(g => g.Key, g => new Queue<string>(g));
            foreach (var k in onlyA)
            {
                if (A[k] != null && bByHash.TryGetValue(A[k], out var q) && q.Count > 0) { var to = q.Dequeue(); d.Moved.Add((k, to)); onlyB.Remove(to); }
                else d.OnlyA.Add(k);
            }
            d.OnlyB.AddRange(onlyB);
            return d;
        }

        public sealed class FileDiff
        {
            public long SizeA, SizeB, DiffBytes, FirstDiff = -1, Compared;
            public List<(long off, int a, int b)> Samples = new();
            public string ShaA, ShaB;
            public bool Identical => SizeA == SizeB && DiffBytes == 0;
        }

        /// <summary>Byte-by-byte comparison of two files or devices, plus SHA-256 of each.</summary>
        public static FileDiff CompareBytes(string a, string b, Prog p)
        {
            var d = new FileDiff { SizeA = Disks.LengthOf(a), SizeB = Disks.LengthOf(b) };
            p.Total = Math.Max(d.SizeA, d.SizeB);
            using var ha = SHA256.Create(); using var hb = SHA256.Create();
            const int C = 1 << 20;
            var ba = new byte[C]; var bb = new byte[C];
            long off = 0;
            while (true)
            {
                if (p.Cancel) throw new CancelledException();
                var ra = off < d.SizeA ? Disks.ReadAt(a, off, (int)Math.Min(C, d.SizeA - off)) : Array.Empty<byte>();
                var rb = off < d.SizeB ? Disks.ReadAt(b, off, (int)Math.Min(C, d.SizeB - off)) : Array.Empty<byte>();
                if (ra.Length == 0 && rb.Length == 0) break;
                ha.TransformBlock(ra, 0, ra.Length, null, 0); hb.TransformBlock(rb, 0, rb.Length, null, 0);
                int n = Math.Min(ra.Length, rb.Length);
                for (int i = 0; i < n; i++)
                {
                    if (ra[i] != rb[i])
                    {
                        d.DiffBytes++;
                        if (d.FirstDiff < 0) d.FirstDiff = off + i;
                        if (d.Samples.Count < 256) d.Samples.Add((off + i, ra[i], rb[i]));
                    }
                }
                d.Compared += n;
                off += Math.Max(ra.Length, rb.Length);
                p.Done = off;
            }
            ha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); hb.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            d.ShaA = Fmt.Hex(ha.Hash); d.ShaB = Fmt.Hex(hb.Hash);
            if (d.SizeA != d.SizeB && d.FirstDiff < 0) d.FirstDiff = Math.Min(d.SizeA, d.SizeB);
            return d;
        }

        public static readonly Regex HexRe = new(@"\b([0-9a-fA-F]{8}|[0-9a-fA-F]{32}|[0-9a-fA-F]{40}|[0-9a-fA-F]{64}|[0-9a-fA-F]{96}|[0-9a-fA-F]{128})\b", RegexOptions.Compiled);
    }

    /// <summary>Known-file hash sets (KFF / NSRL style / custom bad lists).</summary>
    public sealed class KnownSets
    {
        public sealed class SetInfo { public string Id, Name, Category, Source, SourceSha256, Created; public int Count; }
        readonly string dir;
        readonly Dictionary<string, KnownHit> map = new();
        public List<SetInfo> Sets = new();
        public KnownSets(string dir) { this.dir = dir; Directory.CreateDirectory(dir); Reload(); }

        public void Reload()
        {
            map.Clear(); Sets.Clear();
            foreach (var meta in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var s = Js.D<SetInfo>(File.ReadAllText(meta));
                    var data = System.IO.Path.ChangeExtension(meta, ".txt");
                    if (!File.Exists(data)) continue;
                    foreach (var l in File.ReadLines(data))
                    {
                        int t = l.IndexOf('\t');
                        var h = (t < 0 ? l : l[..t]).Trim();
                        if (h.Length > 0) map.TryAdd(h, new KnownHit { Set = s.Name, Category = s.Category, Label = t < 0 ? "" : l[(t + 1)..] });
                    }
                    Sets.Add(s);
                }
                catch { }
            }
            Sets = Sets.OrderByDescending(x => x.Created).ToList();
        }

        public KnownHit Lookup(string h) => h != null && map.TryGetValue(h.ToLowerInvariant(), out var v) ? v : null;

        public SetInfo Import(string path, string name, string category)
        {
            var items = new List<string>();
            foreach (var line in File.ReadLines(path))
            {
                var m = Engine.HexRe.Match(line);
                if (!m.Success) continue;
                var rest = (line[..m.Index] + " " + line[(m.Index + m.Length)..]).Replace("\"", "").Trim(' ', ',', ';', '\t');
                rest = Regex.Replace(rest, @"\s+", " ");
                items.Add(m.Value.ToLowerInvariant() + "\t" + (rest.Length > 160 ? rest[..160] : rest));
            }
            if (items.Count == 0) throw new InvalidDataException(L.T("No hashes found in the file."));
            var s = new SetInfo
            {
                Id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture), Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(path) : name,
                Category = category, Count = items.Count, Source = path, Created = Fmt.NowUtc(),
                SourceSha256 = Fmt.Hex(SHA256.HashData(File.ReadAllBytes(path)))
            };
            File.WriteAllLines(System.IO.Path.Combine(dir, s.Id + ".txt"), items);
            File.WriteAllText(System.IO.Path.Combine(dir, s.Id + ".json"), Js.S(s));
            Reload();
            return s;
        }

        public void Delete(string id)
        {
            foreach (var ext in new[] { ".json", ".txt" }) { var f = System.IO.Path.Combine(dir, id + ext); if (File.Exists(f)) File.Delete(f); }
            Reload();
        }
    }
}
