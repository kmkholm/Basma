using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Basma
{
    public partial class MainWindow
    {
        // ================================================================ DASHBOARD
        FrameworkElement PageHome()
        {
            Head("BASMA", L.T("Dashboard"), Ui.Btn(L.T("New case"), () => Go("cases", "new"), "primary", "plus"), Ui.Btn(L.T("Hash"), () => Go("hash"), "plain", "hash"));
            var cases = Store.AllCases();
            var hist = Store.History();
            int ev = cases.Sum(c => c.Items.Count), cu = cases.Sum(c => c.Custody.Count), mis = cases.Sum(c => c.Hashes.Count(h => h.Result == "MISMATCH"));
            var sp = new StackPanel();
            var nums = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6 };
            nums.Children.Add(Ui.Num(cases.Count.ToString(), L.T("cases")));
            nums.Children.Add(Ui.Num(cases.Count(c => c.Info.Status == "open").ToString(), L.T("open"), Ui.Orange, Ui.Ink));
            nums.Children.Add(Ui.Num(ev.ToString(), L.T("exhibits")));
            nums.Children.Add(Ui.Num(cu.ToString(), L.T("custody entries")));
            nums.Children.Add(Ui.Num(hist.Count.ToString(), L.T("hash runs")));
            nums.Children.Add(Ui.Num(mis.ToString(), L.T("mismatches"), mis > 0 ? Ui.Red : null, mis > 0 ? Ui.White : null));
            sp.Children.Add(new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(3), Background = Ui.CardBg, Child = nums, Margin = new Thickness(0, 0, 0, 26) });

            sp.Children.Add(Ui.Section(L.T("Start here"), "→"));
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 18) };
            foreach (var (ic, t, d, go) in new (string, string, string, Action)[]
            {
                ("hash", L.T("Hash a file or folder"), L.T("MD5 · SHA-1 · SHA-256 · SHA-512 · SHA3 · CRC32 in one pass"), () => Go("hash")),
                ("disk", L.T("Hash a physical disk"), L.T("\\\\.\\PhysicalDriveN, whole disk or a sector range"), () => Go("disks")),
                ("compare", L.T("Compare"), L.T("hashes · files byte-by-byte · folders · manifests · disk vs image"), () => Go("compare")),
                ("cases", L.T("Cases & custody"), L.T("exhibits, custody chain, continuity, reports"), () => Go("cases")),
            })
            {
                var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 14, 16, 14) };
                var s = new StackPanel();
                s.Children.Add(Ui.Icon(ic, Ui.Ink, 30)); ((FrameworkElement)s.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
                s.Children.Add(Ui.Tx(t, 17, true)); s.Children.Add(Ui.Tx(d, 12, false, Ui.Muted));
                b.Content = s; b.Click += (_, _) => go(); b.Margin = new Thickness(0, 0, 14, 8);
                tiles.Children.Add(b);
            }
            sp.Children.Add(tiles);

            sp.Children.Add(Ui.Section(L.T("Workflow for every case"), "07"));
            var flow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
            string[][] steps =
            {
                new[]{ "Open case", "Number, legal authority and its scope" }, new[]{ "Live response", "Before power-off: RAM, processes, netstat, sessions" },
                new[]{ "Register exhibits", "Each medium is its own item with its form" }, new[]{ "Chain of custody", "Every transfer: from, to, when, where, signature" },
                new[]{ "Acquisition hash", "Disk or image: MD5 + SHA-1 + SHA-256" }, new[]{ "Verification", "Re-hash before analysis and before court" },
                new[]{ "Report", "Fingerprinted report, forms and court bundle" },
            };
            for (int i = 0; i < steps.Length; i++)
            {
                var s = new StackPanel { Margin = new Thickness(12) };
                s.Children.Add(Ui.Tx((i + 1).ToString("D2"), 26, true, Ui.Ink, true));
                s.Children.Add(Ui.Tx(L.T(steps[i][0]), 14, true));
                s.Children.Add(Ui.Tx(L.T(steps[i][1]), 11.5, false, Ui.Muted));
                flow.Children.Add(new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(0, 0, 2, 0), Child = s, MinHeight = 120 });
            }
            sp.Children.Add(new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Background = Ui.CardBg, Child = flow, Margin = new Thickness(0, 0, 0, 26) });

            sp.Children.Add(Ui.Section(L.T("Recent hash runs"), hist.Count.ToString("D2")));
            if (hist.Count == 0) sp.Children.Add(Ui.Card2Box(Ui.Tx(L.T("No hash runs yet."), 14, false, Ui.Muted), Ui.Card2, 24));
            else sp.Children.Add(Ui.Table(new[] { L.T("Time (UTC)"), L.T("Kind"), L.T("Result"), L.T("Source"), L.T("Size"), "SHA-256" },
                hist.Select(h => new GridRow(h, h.Ts, L.T(h.Kind), L.T(h.Result), h.Target, Fmt.Bytes(h.Size), h.Sha256 ?? h.Md5 ?? "")), new[] { 0, 3, 5 },
                r => { var h = (HistoryItem)r.Tag; if (h.Report != null && File.Exists(h.Report)) Store.OpenInShell(h.Report); }, new double[] { 165, 110, 110, -2, 90, -2 }));
            return sp;
        }

        static string SafeVol(DriveInfo d) { try { return d.IsReady ? $"{d.VolumeLabel} · {d.DriveFormat} · {Fmt.Bytes(d.TotalSize)}" : d.DriveType.ToString(); } catch { return ""; } }
        static List<DiskInfo> SafeDisks() { try { return Disks.List(); } catch { return new(); } }

        public static HashResult HashAny(string path, string mode, List<string> algos, Prog p, long startLba = 0, long count = -1, Engine.Filter filter = null)
        {
            if (Disks.IsDevicePath(path)) return Engine.HashDevice(path, algos, p, startLba, count);
            if (Directory.Exists(path)) return Engine.HashFolder(path, algos, p, Store.Known, filter);
            if (!File.Exists(path)) throw new FileNotFoundException(L.T("Path not found: ") + path);
            return Engine.HashFile(path, algos, p, mode == "split" || Engine.SplitParts(path) != null && mode != "file");
        }

        static string Verdict(HashResult r, string expected, HashResult second)
        {
            if (second != null && second.H.Any(kv => second.Get(kv.Key) != r.Get(kv.Key))) return "MISMATCH";
            if (!string.IsNullOrWhiteSpace(expected)) return r.H.Values.Contains(expected.Trim().ToLowerInvariant()) ? "MATCH" : "MISMATCH";
            return "COMPUTED";
        }

        static string SaveHashReport(HashResult r, string expected, string lang)
        {
            try
            {
                var (html, _) = Report.HashReport(r, Store.Actor, lang, expected);
                var stem = $"hash_{DateTime.UtcNow:yyyyMMddTHHmmssZ}_{CaseFile.Safe(Path.GetFileName(r.Target.TrimEnd('\\', '/')))}";
                var p = Report.Save(html, Store.ReportsDir, stem);
                if (r.Rows != null) Engine.WriteManifest(r, Path.Combine(Store.ReportsDir, stem + ".csv"));
                return p;
            }
            catch { return null; }
        }

        public FrameworkElement HashResultView(HashResult r, string expected, HashResult second, string reportPath = null)
        {
            var sp = new StackPanel();
            if (!string.IsNullOrWhiteSpace(expected))
            {
                var e = expected.Trim().ToLowerInvariant();
                var hit = r.H.FirstOrDefault(kv => kv.Value == e);
                bool ok = hit.Key != null;
                var guess = string.Join(" / ", Algos.ByLength(e.Length).Select(Algos.Label));
                sp.Children.Add(Ui.Verdict(ok ? "ok" : "bad", ok ? "✓" : "✗", Ui.Tx(ok ? L.T("MATCH") + " — " + Algos.Label(hit.Key) : L.T("NO MATCH"), 18, true),
                    Ui.Tx(L.T("Expected") + $" ({(guess == "" ? L.T("unknown length") : guess)}): ", 12.5, false, Ui.Muted), Ui.Sel(e, 12.5)));
            }
            if (second != null)
            {
                bool same = second.H.All(kv => r.Get(kv.Key) == kv.Value);
                sp.Children.Add(Ui.Verdict(same ? "ok" : "bad", same ? "×2 ✓" : "×2 ✗", Ui.Tx(same ? L.T("Second pass returned the identical hash — the read is stable.") : L.T("Second pass returned a DIFFERENT hash — the source changed or has unstable sectors."), 15, true)));
            }
            var meta = new List<(string, string)>
            {
                (L.T("Source"), r.Target), (L.T("Type"), L.T(r.TargetKind)), (L.T("Size"), $"{r.Size:N0} B  ({Fmt.Bytes(r.Size)})"),
                (L.T("Time"), $"{r.Started} → {r.Finished}  ·  {Fmt.Dur(r.Seconds)}  ·  {Fmt.Bytes(r.Seconds > 0 ? r.Size / r.Seconds : 0)}/s"),
            };
            if (r.TargetKind != "file" && r.TargetKind != "text") meta.Add((L.T("Files"), r.FileCount.ToString()));
            if (r.StartLba >= 0) meta.Add((L.T("Sector range"), $"LBA {r.StartLba:N0} + {r.CountLba:N0} × {r.SectorSize} B"));
            if (r.Parts != null) meta.Add((L.T("Segments (one stream)"), string.Join(", ", r.Parts)));
            if (r.TargetKind == "disk" || r.TargetKind == "volume") meta.Add((L.T("Unreadable sectors"), r.BadSectors.Count == 0 ? L.T("none") : $"{r.BadSectors.Count}: " + string.Join(", ", r.BadSectors.Take(30))));
            var kv = new Grid();
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); kv.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < meta.Count; i++)
            {
                kv.RowDefinitions.Add(new RowDefinition());
                var k = Ui.Tx(meta[i].Item1, 13, true); var v = Ui.Sel(meta[i].Item2, 13);
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                k.Margin = v.Margin = new Thickness(0, 0, 0, 4);
                kv.Children.Add(k); kv.Children.Add(v);
            }
            var lines = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var a in Algos.All.Where(r.H.ContainsKey))
            {
                bool m = !string.IsNullOrWhiteSpace(expected) && r.Get(a) == expected.Trim().ToLowerInvariant();
                lines.Children.Add(Ui.HashLine(r.TargetKind == "folder" && a == "sha256" ? "FOLDER" : Algos.Label(a), r.Get(a), m ? Ui.OkBg : null));
            }
            var tags = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (r.Sig != null && r.TargetKind != "text") { tags.Children.Add(Ui.Tag($"{L.T("signature")}: {r.Sig.Detected ?? "?"} · .{r.Sig.Ext} · {L.T(r.Sig.Status)}", r.Sig.Status == "mismatch" ? Ui.Yellow : null)); tags.Children.Add(Ui.Tag($"{L.T("entropy")} {r.Entropy}", r.Entropy >= 7.5 ? Ui.Yellow : null)); }
            var acts = Ui.Row(
                reportPath != null ? Ui.Btn(L.T("Open report"), () => Store.OpenInShell(reportPath), "primary", "print") : null,
                reportPath != null ? Ui.Btn(L.T("Show in folder"), () => Store.OpenInShell(reportPath.Replace(".html", r.Rows != null ? ".csv" : ".html")), "plain", "folder") : null,
                Ui.Btn(L.T("Copy all"), () => { try { Clipboard.SetText(string.Join(Environment.NewLine, r.H.Select(x => $"{Algos.Label(x.Key)}: {x.Value}").Prepend(r.Target))); Toast(L.T("Copied")); } catch { } }, "plain"));
            if (r.TargetKind != "text")
                foreach (var a in new[] { "md5", "sha1", "sha256" }.Where(a => r.Rows != null ? r.Rows.Any(x => x.Get(a) != null) : r.Get(a) != null))
                    acts.Children.Add(Ui.Btn(L.T("Export") + " ." + a, () => ExportChecksum(r, a), "small"));
            if (r.Note != null && r.Note.StartsWith("filter")) tags.Children.Add(Ui.Tag(r.Note, Ui.Yellow));
            var card = new List<UIElement> { kv, lines, tags };
            if (r.Note == "e01-container") card.Add(Ui.Finding("info", L.T("This is an E01 container. Its file hash is NOT the source-disk hash; the disk hash is stored inside the container and verified by the imaging tool.")));
            if (r.TargetKind == "folder") { card.Add(Ui.Tx(L.T("Folder hash = SHA-256 over sorted 'path⇥size⇥SHA-256' lines (same rule as ISNAD)."), 12, false, Ui.Muted)); card.Add(StatsTags(r.Stats)); }
            card.Add(acts);
            sp.Children.Add(Ui.Card(L.T("Result"), "✓", card.ToArray()));
            if (r.Rows != null) sp.Children.Add(Ui.Card(L.T("File manifest"), r.Rows.Count.ToString(), ManifestView(r.Rows)));
            if (r.Errors.Count > 0) sp.Children.Add(Ui.Card(L.T("Read errors"), r.Errors.Count.ToString(), r.Errors.Take(100).Select(e => (UIElement)Ui.Finding("warning", e)).ToArray()));
            return sp;
        }

        public static FrameworkElement StatsTags(Dictionary<string, int> st)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
            if (st == null) return w;
            void T(string k, string lab, Brush hot) { int n = st.TryGetValue(k, out var v) ? v : 0; w.Children.Add(Ui.Tag($"{n} {L.T(lab)}", n > 0 ? hot : null, n > 0 && hot == Ui.Red ? Ui.White : null)); }
            T("sig_mismatch", "lying extension", Ui.Yellow); T("high_entropy", "high entropy", Ui.Yellow); T("known_bad", "known bad", Ui.Red);
            T("known_good", "known good", Ui.OkBg); T("duplicate_sets", "duplicate sets", Ui.Card2); T("errors", "read errors", Ui.Red);
            return w;
        }

        public FrameworkElement ManifestView(List<FileRow> rows)
        {
            var sp = new StackPanel();
            var host = new ContentControl();
            var chips = new WrapPanel();
            void Show(string f)
            {
                var sel = f switch
                {
                    "mis" => rows.Where(x => x.SigStatus == "mismatch"), "ent" => rows.Where(x => x.HighEntropy), "known" => rows.Where(x => x.Known != null),
                    "dup" => rows.Where(x => x.Duplicates > 0), "err" => rows.Where(x => x.Error != null), _ => rows
                };
                host.Content = Ui.Table(new[] { L.T("Path"), L.T("Size"), "SHA-256", "MD5", L.T("Signature"), L.T("Entropy"), L.T("Hash set"), L.T("Dup."), L.T("Modified (UTC)") },
                    sel.Take(20000).Select(x => new GridRow(x, x.Path, x.Size.ToString("N0"), x.Get("sha256") ?? x.Error, x.Get("md5") ?? "", (x.SigDetected ?? "") + (x.SigStatus == "mismatch" ? " ⚠" : ""),
                        x.Entropy.ToString("0.00", CultureInfo.InvariantCulture) + (x.HighEntropy ? " ⚠" : ""), x.Known?.Label ?? "", x.Duplicates > 0 ? x.Duplicates.ToString() : "", x.Modified)),
                    new[] { 0, 1, 2, 3, 5, 8 }, null, new double[] { -3, 90, -3, -2, 110, 80, 130, 50, 160 });
            }
            foreach (var (k, lab, n) in new[] { ("all", L.T("All"), rows.Count), ("mis", L.T("Lying extension"), rows.Count(x => x.SigStatus == "mismatch")), ("ent", L.T("High entropy"), rows.Count(x => x.HighEntropy)),
                ("known", L.T("In hash sets"), rows.Count(x => x.Known != null)), ("dup", L.T("Duplicates"), rows.Count(x => x.Duplicates > 0)), ("err", L.T("Errors"), rows.Count(x => x.Error != null)) })
            {
                var rb = new RadioButton { Content = $"{lab} · {n}", Style = (Style)Application.Current.Resources["Chip"], GroupName = "mf" + GetHashCode() + rows.GetHashCode(), IsChecked = k == "all" };
                rb.Checked += (_, _) => Show(k);
                chips.Children.Add(rb);
            }
            Show("all");
            sp.Children.Add(chips); sp.Children.Add(host);
            return sp;
        }

        // ================================================================ DISKS
        FrameworkElement PageDisks()
        {
            Head(L.T("Disks & partitions"), L.T("Physical disks connected to this machine"), Ui.Btn(L.T("Refresh"), () => Go("disks"), "plain"));
            var sp = new StackPanel();
            if (!Native.IsAdmin())
                sp.Children.Add(Ui.Verdict("warn", "!", Ui.Tx(L.T("Not running as administrator."), 16, true), Ui.Tx(L.T("The disk list works, but reading sectors, partition tables and hashing a disk need administrator rights."), 13), new Border { Height = 8 }, AdminButton()));
            var disks = SafeDisks();
            if (disks.Count == 0) sp.Children.Add(Ui.Finding("warning", L.T("No disks could be enumerated.")));
            foreach (var d in disks)
            {
                var res = new StackPanel();
                var head = new DockPanel();
                var big = Ui.Tx("PhysicalDrive" + d.Number, 30, true, Ui.Ink, true, false);
                var tags = Ui.Row(Ui.Tag(d.Bus ?? "?", Ui.Card2), d.Removable ? Ui.Tag(L.T("removable"), Ui.Yellow) : null, Ui.Tag(Fmt.Bytes(d.Size), Ui.Orange), Ui.Tag($"{d.SectorSize} B/sector"));
                DockPanel.SetDock(tags, Dock.Right); head.Children.Add(tags); head.Children.Add(big);
                var info = Ui.Tx($"{d.Model}   ·   S/N {(string.IsNullOrEmpty(d.Serial) ? "—" : d.Serial)}   ·   rev {d.Revision}   ·   {d.Size:N0} B", 13, false, Ui.Muted, true);
                var vols = Ui.Tx(L.T("Volumes") + ": " + (d.Volumes.Count == 0 ? "—" : string.Join("   ", d.Volumes)), 13);
                var dd = d;
                var btns = Ui.Row(
                    Ui.Btn(L.T("Hash this disk"), () => Go("hash", dd.Path), "primary", "hash"),
                    Ui.Btn(L.T("Partition table & sector 0"), () => { res.Children.Clear(); res.Children.Add(PartitionView(dd)); }, "plain", "eye"),
                    Ui.Btn(L.T("Hex viewer"), () => Go("tools", "hex|" + dd.Path), "plain", "tools"),
                    Ui.Btn(L.T("Image to file (dd)"), () => Go("tools", "image|" + dd.Path), "plain", "box"));
                var volBtns = new WrapPanel();
                foreach (var v in d.Volumes)
                {
                    var letter = v.Substring(0, 2);
                    volBtns.Children.Add(Ui.Tag(v, Ui.Card2));
                    volBtns.Children.Add(Ui.Btn(L.T("Hash all files on") + " " + letter, () => Go("hash", "drive|" + letter + "\\"), "small", "folder"));
                    volBtns.Children.Add(Ui.Btn(L.T("Hash raw volume") + " " + letter, () => Go("hash", @"\\.\" + letter), "small", "hash"));
                }
                sp.Children.Add(Ui.Card(null, null, head, info, vols, volBtns, new Border { Height = 6 }, btns, res));
            }
            return sp;
        }

        FrameworkElement PartitionView(DiskInfo d)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            try
            {
                int sec = d.SectorSize;
                byte[] Rd(long lba, int n) => Disks.ReadAt(d.Path, lba * sec, n * sec);
                var lay = Partitions.Parse(Rd, sec, d.Size / sec);
                sp.Children.Add(Ui.Row(Ui.Tag(L.T("Scheme") + ": " + lay.Scheme, Ui.Yellow), Ui.Tag("55 AA: " + (lay.BootSig ? "✓" : "✗"), lay.BootSig ? Ui.OkBg : Ui.BadBg),
                    Ui.Tag(L.T("Disk signature") + " " + lay.DiskSignature), lay.DiskGuid != null ? Ui.Tag("GUID " + lay.DiskGuid) : null));
                foreach (var n in lay.Notes) sp.Children.Add(Ui.Finding("info", n));
                sp.Children.Add(Ui.Table(new[] { "#", L.T("Type"), L.T("Name"), L.T("Start LBA"), L.T("End LBA"), L.T("Sectors"), L.T("Size"), L.T("Type code") },
                    lay.Parts.Select(p => new GridRow(p, p.Index.ToString(), p.Type + (p.Bootable ? " ★" : ""), p.Name ?? "", p.StartLba.ToString("N0"), p.EndLba.ToString("N0"), p.Sectors.ToString("N0"), Fmt.Bytes((double)p.Sectors * sec), p.TypeCode)),
                    new[] { 3, 4, 5, 7 }, r => { var p = (PartInfo)r.Tag; Go("hash", $"range|{d.Path}|{p.StartLba}|{p.Sectors}"); }, new double[] { 40, -2, -2, 120, 120, 120, 90, -3 }));
                sp.Children.Add(Ui.Tx(L.T("Double-click a partition to hash exactly its sector range."), 12, false, Ui.Muted));
                if (lay.Gaps.Count > 0)
                {
                    sp.Children.Add(Ui.Tx(L.T("Unpartitioned space (data outside partitions — check it)"), 14, true));
                    foreach (var g in lay.Gaps) sp.Children.Add(Ui.Finding("warning", $"LBA {g.start:N0} → {g.start + g.sectors - 1:N0}  ·  {Fmt.Bytes((double)g.sectors * sec)}"));
                }
                var s0 = Rd(0, 1);
                sp.Children.Add(Ui.Tx(L.T("Sector 0 (MBR)"), 14, true));
                var hex = Ui.Sel(Interp.HexDump(s0, 0), 12.5); hex.TextWrapping = TextWrapping.NoWrap;
                sp.Children.Add(Ui.Card2Box(hex, Ui.White, 10));
                sp.Children.Add(Ui.Tx(L.T("Offsets: 0x1B8 disk signature · 0x1BE–0x1FD four partition entries (type at +4, start LBA at +8, sectors at +12) · 0x1FE 55 AA"), 12, false, Ui.Muted));
            }
            catch (Exception e)
            {
                sp.Children.Add(Ui.Finding("critical", e.Message));
                if (e is UnauthorizedAccessException) sp.Children.Add(AdminButton());
            }
            return sp;
        }
    }
}
