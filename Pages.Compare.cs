using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Basma
{
    public partial class MainWindow
    {
        // ================================================================ COMPARE
        FrameworkElement PageCompare()
        {
            Head(L.T("Compare"), L.T("Hashes, files, folders, manifests and whole sources"));
            var sp = new StackPanel();
            var host = new ContentControl();
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var modes = new (string k, string lab, Func<FrameworkElement> ui)[]
            {
                ("hashes", L.T("Two hashes"), CmpHashes), ("files", L.T("Two files — byte by byte"), CmpFiles), ("folders", L.T("Two folders"), CmpFolders),
                ("manifest", L.T("Folder vs saved manifest"), CmpManifest), ("sources", L.T("Two sources by hash (disk vs image…)"), CmpSources),
            };
            foreach (var m in modes)
            {
                var rb = new RadioButton { Content = m.lab, Style = (Style)Application.Current.Resources["Chip"], GroupName = "cmp", IsChecked = m.k == "hashes" };
                var mm = m; rb.Checked += (_, _) => host.Content = mm.ui();
                chips.Children.Add(rb);
            }
            host.Content = CmpHashes();
            sp.Children.Add(chips); sp.Children.Add(host);
            return sp;
        }

        FrameworkElement CmpHashes()
        {
            var a = Ui.Input("", true); var b = Ui.Input("", true);
            var res = new StackPanel();
            void Upd()
            {
                res.Children.Clear();
                string x = a.Text.Trim().ToLowerInvariant(), y = b.Text.Trim().ToLowerInvariant();
                if (x == "" || y == "") return;
                bool ok = x == y;
                var g = string.Join(" / ", Algos.ByLength(x.Length).Select(Algos.Label));
                res.Children.Add(Ui.Verdict(ok ? "ok" : "bad", ok ? "✓" : "✗", Ui.Tx(ok ? L.T("MATCH") : L.T("NO MATCH"), 18, true), Ui.Tx($"{x.Length} / {y.Length} {L.T("hex characters")} · {(g == "" ? L.T("unknown length") : g)}", 12.5, false, Ui.Muted)));
                if (!ok)
                {
                    var tb = new TextBlock { FontFamily = Ui.Mono, FontSize = 16, FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.Wrap };
                    int n = Math.Max(x.Length, y.Length), diff = 0;
                    for (int i = 0; i < n; i++)
                    {
                        char ca = i < x.Length ? x[i] : ' ', cb = i < y.Length ? y[i] : ' ';
                        var run = new Run(ca.ToString());
                        if (ca != cb) { run.Background = Ui.Red; run.Foreground = Ui.White; diff++; }
                        tb.Inlines.Add(run);
                    }
                    res.Children.Add(Ui.Card2Box(tb));
                    res.Children.Add(Ui.Tx($"{diff} {L.T("differing positions")}", 12.5, false, Ui.Muted));
                }
            }
            a.TextChanged += (_, _) => Upd(); b.TextChanged += (_, _) => Upd();
            return Ui.Card(L.T("Two hashes"), "01", Ui.Field(L.T("First hash"), a), Ui.Field(L.T("Second hash"), b), res);
        }

        FrameworkElement CmpFiles()
        {
            var (ua, a) = PathPicker(L.T("File A (or device path)"), "", true, false);
            var (ub, b) = PathPicker(L.T("File B (or device path)"), "", true, false);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Compare byte by byte"), () =>
            {
                string pa = a.Text.Trim().Trim('"'), pb = b.Text.Trim().Trim('"');
                if (pa == "" || pb == "") { Toast(L.T("Choose both sources."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Engine.CompareBytes(pa, pb, p), d =>
                {
                    string v = d.Identical ? "IDENTICAL" : "DIFFERENT";
                    res.Children.Add(Ui.Verdict(d.Identical ? "ok" : "bad", d.Identical ? "=" : "≠", Ui.Tx(L.T(v), 20, true),
                        Ui.Tx(d.Identical ? L.T("Every byte is the same.") : $"{L.T("First difference at offset")} {d.FirstDiff:N0} (0x{d.FirstDiff:X}) · {d.DiffBytes:N0} {L.T("bytes differ")}", 13)));
                    var sum = new List<(string, string)> { ("A", pa), ("B", pb), (L.T("Size A"), $"{d.SizeA:N0} B"), (L.T("Size B"), $"{d.SizeB:N0} B"), (L.T("Bytes compared"), d.Compared.ToString("N0")),
                        (L.T("Differing bytes"), d.DiffBytes.ToString("N0")), (L.T("First difference"), d.FirstDiff < 0 ? "—" : $"{d.FirstDiff:N0} (0x{d.FirstDiff:X})"), ("SHA-256 A", d.ShaA), ("SHA-256 B", d.ShaB) };
                    var det = d.Samples.Select(s => $"0x{s.off:X10}  ({s.off:N0})   A={s.a:X2}  B={s.b:X2}").ToList();
                    var (html, _) = Report.CompareReport(L.T("Byte comparison report"), sum, v, det, Store.Actor, Store.S.Lang);
                    var rp = Report.Save(html, Store.ReportsDir, $"compare_bytes_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                    res.Children.Add(Ui.Card(L.T("Details"), "≡", Ui.HashLine("SHA-256 A", d.ShaA, d.Identical ? Ui.OkBg : null), Ui.HashLine("SHA-256 B", d.ShaB, d.Identical ? Ui.OkBg : null),
                        Ui.Tx($"{L.T("Size A")} {d.SizeA:N0} B · {L.T("Size B")} {d.SizeB:N0} B · {L.T("Bytes compared")} {d.Compared:N0}", 13, false, Ui.Muted),
                        d.Samples.Count > 0 ? Ui.Table(new[] { L.T("Offset (hex)"), L.T("Offset"), "A", "B" }, d.Samples.Select(s => new GridRow(s, $"0x{s.off:X10}", s.off.ToString("N0"), s.a.ToString("X2"), s.b.ToString("X2"))), new[] { 0, 1, 2, 3 }) : null,
                        Ui.Row(Ui.Btn(L.T("Open report"), () => Store.OpenInShell(rp), "primary", "print"))));
                }, () => go.IsEnabled = true);
            }, "primary", "compare");
            return Ui.Card(L.T("Two files — byte by byte"), "02", ua, ub, Ui.Tx(L.T("Shows the exact offsets that differ — for example a single byte changed in an image after acquisition."), 12.5, false, Ui.Muted), Ui.Row(go), job, res);
        }

        FrameworkElement FolderDiffView(Engine.FolderDiff d, string title, string a, string b)
        {
            var sp = new StackPanel();
            string v = d.Same ? "IDENTICAL" : "DIFFERENT";
            sp.Children.Add(Ui.Verdict(d.Same ? "ok" : "bad", d.Same ? "=" : "≠", Ui.Tx(L.T(v), 20, true),
                Ui.Row(Ui.Tag($"{d.Identical.Count} {L.T("identical")}", Ui.OkBg), Ui.Tag($"{d.Modified.Count} {L.T("modified")}", d.Modified.Count > 0 ? Ui.Red : null, d.Modified.Count > 0 ? Ui.White : null),
                    Ui.Tag($"{d.OnlyA.Count} {L.T("only in A (deleted)")}", d.OnlyA.Count > 0 ? Ui.Yellow : null), Ui.Tag($"{d.OnlyB.Count} {L.T("only in B (added)")}", d.OnlyB.Count > 0 ? Ui.Yellow : null),
                    Ui.Tag($"{d.Moved.Count} {L.T("moved / renamed")}", d.Moved.Count > 0 ? Ui.Card2 : null))));
            var rows = d.Modified.Select(x => new GridRow(null, L.T("modified"), x, ""))
                .Concat(d.OnlyA.Select(x => new GridRow(null, L.T("only in A (deleted)"), x, "")))
                .Concat(d.OnlyB.Select(x => new GridRow(null, L.T("only in B (added)"), x, "")))
                .Concat(d.Moved.Select(m => new GridRow(null, L.T("moved / renamed"), m.from, m.to)))
                .Concat(d.Identical.Select(x => new GridRow(null, L.T("identical"), x, ""))).ToList();
            var det = rows.Select(r => $"{r[0]}: {r[1]} {r[2]}").ToList();
            var (html, _) = Report.CompareReport(title, new[] { ("A", a), ("B", b), (L.T("identical"), d.Identical.Count.ToString()), (L.T("modified"), d.Modified.Count.ToString()),
                (L.T("only in A (deleted)"), d.OnlyA.Count.ToString()), (L.T("only in B (added)"), d.OnlyB.Count.ToString()), (L.T("moved / renamed"), d.Moved.Count.ToString()) }, v, det, Store.Actor, Store.S.Lang);
            var rp = Report.Save(html, Store.ReportsDir, $"compare_folders_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
            sp.Children.Add(Ui.Card(L.T("Differences"), rows.Count.ToString(), Ui.Table(new[] { L.T("Status"), L.T("Path (A)"), L.T("Path (B)") }, rows, new[] { 1, 2 }, null, new double[] { 190, -1, -1 }),
                Ui.Row(Ui.Btn(L.T("Open report"), () => Store.OpenInShell(rp), "primary", "print"))));
            return sp;
        }

        FrameworkElement CmpFolders()
        {
            var (ua, a) = PathPicker(L.T("Folder A (original)"), "", false, true);
            var (ub, b) = PathPicker(L.T("Folder B (copy / later state)"), "", false, true);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Compare folders"), () =>
            {
                string pa = a.Text.Trim().Trim('"'), pb = b.Text.Trim().Trim('"');
                if (!Directory.Exists(pa) || !Directory.Exists(pb)) { Toast(L.T("Both folders must exist."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p =>
                {
                    var ra = Engine.HashFolder(pa, new List<string> { "sha256" }, p, null);
                    p.Done = 0; p.FilesDone = 0;
                    var rb = Engine.HashFolder(pb, new List<string> { "sha256" }, p, null);
                    return (ra, rb);
                }, r =>
                {
                    res.Children.Add(Ui.Row(Ui.Tag("A · " + r.ra.Get("sha256")), Ui.Tag("B · " + r.rb.Get("sha256"))));
                    res.Children.Add(FolderDiffView(Engine.Diff(r.ra.Rows, r.rb.Rows), L.T("Folder comparison report"), pa, pb));
                }, () => go.IsEnabled = true);
            }, "primary", "compare");
            return Ui.Card(L.T("Two folders"), "03", ua, ub, Ui.Tx(L.T("Matches files by path and SHA-256; a file with the same content under a new name is reported as moved/renamed."), 12.5, false, Ui.Muted), Ui.Row(go), job, res);
        }

        FrameworkElement CmpManifest()
        {
            var (ua, a) = PathPicker(L.T("Folder to check now"), "", false, true);
            var (ub, b) = PathPicker(L.T("Saved manifest CSV (BASMA or ISNAD)"), "", true, false, "CSV|*.csv|" + L.T("All files") + "|*.*");
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Verify against manifest"), () =>
            {
                string pa = a.Text.Trim().Trim('"'), pb = b.Text.Trim().Trim('"');
                if (!Directory.Exists(pa) || !File.Exists(pb)) { Toast(L.T("Choose the folder and the manifest."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => (Engine.ReadManifest(pb), Engine.HashFolder(pa, new List<string> { "sha256" }, p, null)),
                    r => res.Children.Add(FolderDiffView(Engine.Diff(r.Item1, r.Item2.Rows), L.T("Manifest verification report"), pb, pa)), () => go.IsEnabled = true);
            }, "primary", "shield");
            return Ui.Card(L.T("Folder vs saved manifest"), "04", ua, ub, Ui.Tx(L.T("A = the manifest saved at acquisition, B = the folder as it is now."), 12.5, false, Ui.Muted), Ui.Row(go), job, res);
        }

        FrameworkElement CmpSources()
        {
            var (ua, a) = PathPicker(L.T("Source A (file, folder, .001 or \\\\.\\PhysicalDriveN)"), "", true, true);
            var (ub, b) = PathPicker(L.T("Source B"), "", true, true);
            var disks = SafeDisks();
            var fillA = Ui.Combo(new[] { ("", L.T("— put a disk in A —")) }.Concat(disks.Select(d => (d.Path, d.Title))));
            fillA.SelectionChanged += (_, _) => { if (Ui.Val(fillA) is string v && v != "") a.Text = v; };
            var algos = new List<string> { "md5", "sha256" };
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Hash both and compare"), () =>
            {
                string pa = a.Text.Trim().Trim('"'), pb = b.Text.Trim().Trim('"');
                if (pa == "" || pb == "") { Toast(L.T("Choose both sources."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p =>
                {
                    var ra = HashAny(pa, "auto", algos, p);
                    p.Done = 0; p.FilesDone = 0;
                    var rb = HashAny(pb, "auto", algos, p);
                    return (ra, rb);
                }, r =>
                {
                    bool ok = r.ra.Get("sha256") == r.rb.Get("sha256") && r.ra.Get("md5") == r.rb.Get("md5");
                    if (r.ra.TargetKind == "folder" || r.rb.TargetKind == "folder") ok = r.ra.Get("sha256") == r.rb.Get("sha256");
                    res.Children.Add(Ui.Verdict(ok ? "ok" : "bad", ok ? "=" : "≠", Ui.Tx(ok ? L.T("MATCH") : L.T("MISMATCH"), 20, true),
                        Ui.Tx($"A: {L.T(r.ra.TargetKind)} · {Fmt.Bytes(r.ra.Size)}    B: {L.T(r.rb.TargetKind)} · {Fmt.Bytes(r.rb.Size)}", 13)));
                    if ((r.ra.Note == "e01-container") ^ (r.rb.Note == "e01-container")) res.Children.Add(Ui.Finding("info", L.T("One side is an E01 container: it never hashes the same as the raw disk. Compare a disk with a raw (dd) image instead.")));
                    var (html, _) = Report.CompareReport(L.T("Source comparison report"), new[] { ("A", pa), ("B", pb), ("MD5 A", r.ra.Get("md5") ?? ""), ("MD5 B", r.rb.Get("md5") ?? ""), ("SHA-256 A", r.ra.Get("sha256")), ("SHA-256 B", r.rb.Get("sha256")) },
                        ok ? "MATCH" : "MISMATCH", Array.Empty<string>(), Store.Actor, Store.S.Lang);
                    var rp = Report.Save(html, Store.ReportsDir, $"compare_sources_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                    res.Children.Add(Ui.Card(L.T("Hashes"), "≡", Ui.HashLine("A · MD5", r.ra.Get("md5") ?? "—", ok ? Ui.OkBg : null), Ui.HashLine("B · MD5", r.rb.Get("md5") ?? "—", ok ? Ui.OkBg : null),
                        Ui.HashLine("A · SHA-256", r.ra.Get("sha256"), ok ? Ui.OkBg : null), Ui.HashLine("B · SHA-256", r.rb.Get("sha256"), ok ? Ui.OkBg : null),
                        Ui.Row(Ui.Btn(L.T("Open report"), () => Store.OpenInShell(rp), "primary", "print"))));
                }, () => go.IsEnabled = true);
            }, "primary", "compare");
            return Ui.Card(L.T("Two sources by hash"), "05", fillA, ua, ub,
                Ui.Tx(L.T("Typical use: the suspect disk (through a write blocker) against its raw dd image — they must hash identically."), 12.5, false, Ui.Muted), Ui.Row(go), job, res);
        }

        // ================================================================ TOOLS
        FrameworkElement ToolsClassic(string preset, long startOffset = 0)
        {
            var sp = new StackPanel();
            // ---- hex viewer
            var (srcUi, src) = PathPicker(L.T("Source (file or \\\\.\\PhysicalDriveN)"), preset ?? "", true, false);
            var disks = SafeDisks();
            var fill = Ui.Combo(new[] { ("", L.T("— or choose a disk —")) }.Concat(disks.Select(d => (d.Path, d.Title))));
            fill.SelectionChanged += (_, _) => { if (Ui.Val(fill) is string v && v != "") src.Text = v; };
            var off = Ui.Input("0", true); var lba = Ui.Input("", true);
            var size = Ui.Combo(new[] { ("512", "512 B"), ("4096", "4 KB"), ("16384", "16 KB") });
            var hexOut = Ui.Sel("", 12.5); hexOut.TextWrapping = TextWrapping.NoWrap; hexOut.MinHeight = 120;
            var ioff = Ui.Input("0", true);
            var interp = new StackPanel();
            byte[] cur = Array.Empty<byte>(); long curOff = 0;
            void ShowInterp()
            {
                interp.Children.Clear();
                if (!int.TryParse(ioff.Text.Trim().Replace("0x", ""), ioff.Text.Contains("0x") ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out int o) || o < 0 || o >= cur.Length) return;
                foreach (var (k, v) in Interp.Bytes(cur, o))
                {
                    var g = new DockPanel();
                    var kk = Ui.Tx(k, 12.5, true); kk.Width = 200; DockPanel.SetDock(kk, Dock.Left);
                    g.Children.Add(kk); g.Children.Add(Ui.Sel(v, 12.5));
                    interp.Children.Add(new Border { BorderBrush = Ui.Card2, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 3, 0, 3), Child = g });
                }
            }
            void Load(long o)
            {
                try
                {
                    string p = src.Text.Trim().Trim('"');
                    if (p == "") { Toast(L.T("Choose a source first."), true); return; }
                    int n = int.Parse(Ui.Val(size));
                    if (o < 0) o = 0;
                    cur = Disks.ReadAt(p, o, n); curOff = o;
                    off.Text = o.ToString(CultureInfo.InvariantCulture);
                    hexOut.Text = Interp.HexDump(cur, o) + (cur.Length == 0 ? L.T("(end of source)") : "");
                    ShowInterp();
                }
                catch (Exception e) { hexOut.Text = e.Message; if (e is UnauthorizedAccessException) Toast(L.T("Administrator rights are required to read a disk directly."), true); }
            }
            ioff.TextChanged += (_, _) => ShowInterp();
            var nav = Ui.Row(
                Ui.Btn(L.T("Read"), () => { long o = 0; if (lba.Text.Trim() != "" && long.TryParse(lba.Text, out var l)) o = l * 512; else long.TryParse(off.Text.Trim(), out o); lba.Text = ""; Load(o); }, "primary", "eye"),
                Ui.Btn("◀ " + L.T("Previous"), () => Load(curOff - int.Parse(Ui.Val(size))), "plain"),
                Ui.Btn(L.T("Next") + " ▶", () => Load(curOff + int.Parse(Ui.Val(size))), "plain"),
                Ui.Btn("LBA 0 (MBR)", () => Load(0), "small"), Ui.Btn("LBA 1 (GPT)", () => Load(512), "small"));
            var hexCard = Ui.Card(L.T("Hex viewer"), "01", srcUi, fill, Ui.Cols(3, Ui.Field(L.T("Offset (bytes)"), off), Ui.Field(L.T("or sector (LBA × 512)"), lba), Ui.Field(L.T("Page size"), size)), nav,
                Ui.Card2Box(new ScrollViewer { Content = hexOut, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, Ui.White, 10),
                Ui.Field(L.T("Interpret bytes at offset within this page (decimal or 0x…)"), ioff), interp);
            sp.Children.Add(hexCard);
            if (!string.IsNullOrEmpty(preset)) Dispatcher.BeginInvoke(new Action(() => Load(startOffset / 512 * 512)));

            // ---- timestamp decoder
            var ts = Ui.Input("", true); var tsOut = new StackPanel();
            ts.TextChanged += (_, _) =>
            {
                tsOut.Children.Clear();
                foreach (var (k, v) in Interp.Timestamp(ts.Text))
                {
                    var g = new DockPanel(); var kk = Ui.Tx(k, 12.5, true); kk.Width = 290; DockPanel.SetDock(kk, Dock.Left);
                    g.Children.Add(kk); g.Children.Add(Ui.Sel(v, 13));
                    tsOut.Children.Add(new Border { BorderBrush = Ui.Card2, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 3, 0, 3), Child = g });
                }
            };
            // ---- signature / entropy
            var (sigUi, sigPath) = PathPicker(L.T("File"), "", true, false);
            var sigOut = new StackPanel();
            var sigBtn = Ui.Btn(L.T("Check"), () =>
            {
                sigOut.Children.Clear();
                try
                {
                    string p = sigPath.Text.Trim().Trim('"');
                    var fi = new FileInfo(p);
                    var head = Disks.ReadAt(p, 0, (int)Math.Min(fi.Length, 8 << 20));
                    var sig = Engine.Signature(p, head.Take(64).ToArray(), fi.Length);
                    double ent = Engine.Entropy(head, head.Length);
                    bool hot = ent >= 7.5 && fi.Length >= 4096 && !Engine.Compressed.Contains(sig.Detected ?? "");
                    sigOut.Children.Add(Ui.Verdict(sig.Status == "mismatch" || hot ? "bad" : "ok", sig.Status == "mismatch" ? "⚠" : "✓",
                        Ui.Tx($"{L.T("Detected")}: {sig.Detected ?? L.T("unknown")}   ·   {L.T("extension")}: .{sig.Ext}   ·   {L.T(sig.Status)}", 15, true),
                        Ui.Tx($"{L.T("entropy")} {ent} {L.T("bits/byte")} ({L.T("first")} {Fmt.Bytes(head.Length)})" + (hot ? "  —  " + L.T("very high: possibly an encrypted container") : ""), 13)));
                    sigOut.Children.Add(Ui.Tx($"{L.T("Created")} {Fmt.Utc(fi.CreationTimeUtc)}  ·  {L.T("Modified")} {Fmt.Utc(fi.LastWriteTimeUtc)}  ·  {L.T("Accessed")} {Fmt.Utc(fi.LastAccessTimeUtc)}  ·  {fi.Length:N0} B", 12.5, false, Ui.Muted, true));
                    var hx = Ui.Sel(Interp.HexDump(head.Take(64).ToArray(), 0), 12.5); hx.TextWrapping = TextWrapping.NoWrap;
                    sigOut.Children.Add(Ui.Card2Box(hx, Ui.White, 8));
                }
                catch (Exception e) { sigOut.Children.Add(Ui.Finding("critical", e.Message)); }
            }, "primary", "eye");
            // ---- verify document
            var (docUi, doc) = PathPicker(L.T("Report or form (.html) issued by BASMA or ISNAD"), "", true, false, "HTML|*.html|" + L.T("All files") + "|*.*");
            var docOut = new StackPanel();
            var docBtn = Ui.Btn(L.T("Verify document"), () =>
            {
                docOut.Children.Clear();
                try
                {
                    var v = Report.Verify(doc.Text.Trim().Trim('"'));
                    docOut.Children.Add(Ui.Verdict(v.ok ? "ok" : "bad", v.ok ? "✓" : "✗", Ui.Tx(v.ok ? L.T("Document intact: fingerprint matches") : L.T("Document ALTERED or unsigned: fingerprint does not match"), 15, true),
                        Ui.Sel(v.claimed ?? v.reason ?? "", 12), v.ok || v.actual == null ? new Border() : Ui.Sel(v.actual, 12)));
                }
                catch (Exception e) { docOut.Children.Add(Ui.Finding("critical", e.Message)); }
            }, "primary", "shield");
            sp.Children.Add(Ui.Split(
                Ui.Card(L.T("Timestamp decoder"), "02", Ui.Field(L.T("Number (decimal or 0x…) — FILETIME, Unix, WebKit, Mac, DOS"), ts), tsOut),
                Ui.Card(L.T("Signature & entropy check"), "03", sigUi, Ui.Row(sigBtn), sigOut), 1, 1));
            sp.Children.Add(Ui.Card(L.T("Verify an issued document"), "04", docUi, Ui.Row(docBtn), docOut));
            return sp;
        }

        // ================================================================ HASH SETS
        FrameworkElement PageSets()
        {
            Head(L.T("Known hash sets"), L.T("KFF / NSRL-style lists matched automatically in every folder hash"));
            var sp = new StackPanel();
            var (ui, path) = PathPicker(L.T("Hash list file (txt / csv)"), "", true, false, "TXT/CSV|*.txt;*.csv;*.tsv|" + L.T("All files") + "|*.*");
            var name = Ui.Input(); var cat = Ui.Combo(new[] { ("bad", L.T("Known-bad")), ("good", L.T("Known-good")), ("notable", L.T("Notable")) });
            var imp = Ui.Btn(L.T("Import"), () =>
            {
                try { var s = Store.Known.Import(path.Text.Trim().Trim('"'), name.Text, Ui.Val(cat)); Toast($"{L.T("Imported")} · {s.Count}"); Go("sets"); }
                catch (Exception e) { Toast(e.Message, true); }
            }, "primary", "plus");
            var lk = Ui.Input("", true, true); var lkOut = new StackPanel();
            var lkb = Ui.Btn(L.T("Look up"), () =>
            {
                lkOut.Children.Clear();
                foreach (System.Text.RegularExpressions.Match m in Engine.HexRe.Matches(lk.Text))
                {
                    var hit = Store.Known.Lookup(m.Value);
                    lkOut.Children.Add(Ui.HashLine(hit == null ? "—" : L.T(hit.Category), m.Value.ToLowerInvariant() + (hit == null ? "" : "   ←  " + hit.Label + " · " + hit.Set), hit == null ? null : hit.Category == "bad" ? Ui.BadBg : Ui.OkBg));
                }
            }, "plain", "eye");
            sp.Children.Add(Ui.Split(Ui.Card(L.T("Import"), "01", Ui.Tx(L.T("Each line may hold an MD5, SHA-1 or SHA-256 plus an optional label (NSRL CSV works)."), 12.5, false, Ui.Muted), ui,
                Ui.Cols(2, Ui.Field(L.T("Set name"), name), Ui.Field(L.T("Category"), cat)), Ui.Row(imp)),
                Ui.Card(L.T("Quick lookup"), "02", lk, Ui.Row(lkb), lkOut), 1, 1));
            sp.Children.Add(Ui.Section(L.T("Hash sets"), Store.Known.Sets.Count.ToString("D2")));
            if (Store.Known.Sets.Count == 0) sp.Children.Add(Ui.Card2Box(Ui.Tx(L.T("No hash sets yet."), 14, false, Ui.Muted), Ui.Card2, 24));
            foreach (var s in Store.Known.Sets)
            {
                var row = new DockPanel();
                var del = Ui.Btn(L.T("Delete"), () => { Store.Known.Delete(s.Id); Go("sets"); }, "small");
                DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
                var info = new StackPanel();
                info.Children.Add(Ui.Row(Ui.Tag(L.T(s.Category == "bad" ? "Known-bad" : s.Category == "good" ? "Known-good" : "Notable"), s.Category == "bad" ? Ui.Red : s.Category == "good" ? Ui.OkBg : Ui.Yellow, s.Category == "bad" ? Ui.White : null), Ui.Tx(s.Name, 16, true)));
                info.Children.Add(Ui.Tx($"{s.Count:N0} {L.T("hashes")} · {s.Created} · {s.Source}", 12, false, Ui.Muted, true));
                info.Children.Add(Ui.Tx("SHA-256 " + s.SourceSha256, 11.5, false, Ui.Muted, true));
                row.Children.Add(info);
                sp.Children.Add(new Border { Background = Ui.White, BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10), Child = row });
            }
            return sp;
        }
    }
}
