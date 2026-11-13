using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Basma
{
    public partial class MainWindow
    {
        static readonly (string key, string label)[] ToolList =
        {
            ("image", "Imaging (dd)"), ("search", "Keyword & pattern search"), ("carve", "Signature scan & carving"), ("strings", "Strings"),
            ("entropy", "Entropy map"), ("wipe", "Wipe verification"), ("checksum", "Verify checksum file"), ("usb", "USB device history"),
            ("hex", "Hex viewer & interpreters"), ("coc", "Blank custody form"),
        };

        FrameworkElement PageTools(string preset)
        {
            Head(L.T("Analysis tools"), L.T("Forensic tools for files, images and disks"));
            string tool = "image", path = null; long off = 0;
            if (preset != null)
            {
                var p = preset.Split('|');
                if (p.Length >= 2) { tool = p[0]; path = p[1]; if (p.Length >= 3) long.TryParse(p[2], out off); }
                else if (ToolList.Any(t => t.key == p[0])) tool = p[0];
                else { tool = "hex"; path = preset; }
            }
            var sp = new StackPanel();
            var host = new ContentControl();
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            FrameworkElement Build(string k) => k switch
            {
                "search" => ToolSearch(path), "carve" => ToolCarve(path), "strings" => ToolStrings(path), "entropy" => ToolEntropy(path), "wipe" => ToolWipe(path),
                "checksum" => ToolChecksum(), "usb" => ToolUsb(), "hex" => ToolsClassic(path, off), "coc" => ToolBlankCoc(), _ => ToolImage(path)
            };
            foreach (var (k, lab) in ToolList)
            {
                var rb = new RadioButton { Content = L.T(lab), Style = (Style)Application.Current.Resources["Chip"], GroupName = "tool", IsChecked = k == tool };
                var kk = k; rb.Checked += (_, _) => { try { host.Content = Build(kk); } catch (Exception e) { host.Content = Ui.Finding("critical", e.Message); } };
                chips.Children.Add(rb);
            }
            try { host.Content = Build(tool); } catch (Exception e) { host.Content = Ui.Finding("critical", e.Message); }
            sp.Children.Add(chips); sp.Children.Add(host);
            return sp;
        }

        /// <summary>Path box + file button + a list of physical disks and raw volumes to pick from.</summary>
        (FrameworkElement ui, Func<string> get) SourcePicker(string label, string value)
        {
            var (ui, box) = PathPicker(label, value ?? "", true, false);
            var items = new List<(string, string)> { ("", L.T("— or choose a disk / volume —")) };
            items.AddRange(SafeDisks().Select(d => (d.Path, d.Title)));
            items.AddRange(DriveInfo.GetDrives().Select(d => (@"\\.\" + d.Name.Substring(0, 2), L.T("Volume") + " " + d.Name.Substring(0, 2) + "  " + SafeVol(d))));
            var c = Ui.Combo(items);
            c.SelectionChanged += (_, _) => { if (Ui.Val(c) is string v && v != "") box.Text = v; };
            var sp = new StackPanel { Children = { ui, c } };
            if (!Native.IsAdmin()) sp.Children.Add(Ui.Tx(L.T("Disks and raw volumes need administrator rights; files work without them."), 12, false, Ui.Muted));
            return (sp, () => box.Text.Trim().Trim('"'));
        }

        static long ParseMb(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? (long)(v * 1048576) : 0;

        // ---------------------------------------------------------------- imaging
        FrameworkElement ToolImage(string path)
        {
            var (su, src) = SourcePicker(L.T("Source (disk, volume or file)"), path);
            var dest = Ui.Input("", true);
            var browse = Ui.Btn(L.T("Save as…"), () =>
            {
                var d = new Microsoft.Win32.SaveFileDialog { FileName = "image.dd", Filter = "Raw image|*.dd;*.raw;*.img|" + L.T("All files") + "|*.*" };
                if (d.ShowDialog() == true) dest.Text = d.FileName;
            }, "small", "file");
            browse.Margin = new Thickness(10, 0, 0, 0);
            var dd = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); dd.Children.Add(browse); dd.Children.Add(dest);
            var split = Ui.Combo(new[] { ("0", L.T("One file")), ("650", "650 MB"), ("2048", "2 GB"), ("4095", L.T("4 GB (FAT32 limit)")), ("custom", L.T("Custom (MB)")) });
            var custom = Ui.Input("", true);
            var algo = new Dictionary<string, CheckBox> { ["md5"] = Ui.Chk("MD5", true), ["sha1"] = Ui.Chk("SHA-1", true), ["sha256"] = Ui.Chk("SHA-256", true) };
            var verify = Ui.Chk(L.T("Verify: re-read the image and compare hashes"), true);
            var notes = Ui.Input();
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Create image"), () =>
            {
                string s = src(), d = dest.Text.Trim().Trim('"');
                if (s == "" || d == "") { Toast(L.T("Choose the source and the destination."), true); return; }
                long splitB = Ui.Val(split) == "custom" ? ParseMb(custom.Text) : long.Parse(Ui.Val(split)) * 1048576;
                var algos = algo.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList();
                if (algos.Count == 0) algos.Add("sha256");
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.Image(s, d, splitB, algos, verify.IsChecked == true, p, Store.Actor, notes.Text), r =>
                {
                    res.Children.Add(Ui.Verdict(r.Verified == false ? "bad" : "ok", r.Verified == true ? "✓" : r.Verified == false ? "✗" : "dd",
                        Ui.Tx(r.Verified == true ? L.T("Image created and verified: image hash = source hash.") : r.Verified == false ? L.T("Image does NOT match the source hash.") : L.T("Image created (not verified)."), 16, true),
                        Ui.Tx($"{r.Size:N0} B · {Fmt.Dur(r.Seconds)} · {r.Files.Count} {L.T("files")} · {L.T("unreadable sectors")} {r.Bad.Count}", 13)));
                    var lines = new StackPanel();
                    foreach (var kv in r.SourceHash) lines.Children.Add(Ui.HashLine(L.T("source") + " " + Algos.Label(kv.Key), kv.Value, r.Verified == true ? Ui.OkBg : null));
                    if (r.ImageHash != null) foreach (var kv in r.ImageHash) lines.Children.Add(Ui.HashLine(L.T("image") + " " + Algos.Label(kv.Key), kv.Value, r.Verified == true ? Ui.OkBg : Ui.BadBg));
                    res.Children.Add(Ui.Card(L.T("Image"), "dd", lines, Ui.Sel(string.Join("\n", r.Files), 12),
                        Ui.Row(Ui.Btn(L.T("Open log"), () => Store.OpenInShell(r.Log), "primary", "file"), Ui.Btn(L.T("Show in folder"), () => Store.OpenInShell(r.Files[0]), "plain", "folder"),
                            Ui.Btn(L.T("Hash the image"), () => Go("hash", null), "plain", "hash"))));
                }, () => go.IsEnabled = true);
            }, "primary", "box");
            return Ui.Card(L.T("Imaging (dd)"), "01",
                Ui.Tx(L.T("Bit-for-bit copy of a disk, volume or file to a raw image (like dd / FTK 'Raw'), hashing the source while reading, optionally split into segments, then re-reading the image to prove it matches. Unreadable sectors are zero-filled and listed in the log."), 13, false, Ui.Muted),
                su, Ui.Field(L.T("Destination image file"), dd, L.T("Must be on a DIFFERENT disk, with enough free space.")),
                Ui.Cols(2, Ui.Field(L.T("Split into segments"), split), Ui.Field(L.T("Custom segment size (MB)"), custom)),
                Ui.Row(algo.Values.ToArray()), verify, Ui.Field(L.T("Notes for the log (case, item, write blocker…)"), notes), Ui.Row(go), job, res);
        }

        // ---------------------------------------------------------------- search
        FrameworkElement ToolSearch(string path)
        {
            var (su, src) = SourcePicker(L.T("Source (file, image or disk)"), path);
            var kw = Ui.Input("", false, true); kw.MinHeight = 60;
            var hex = Ui.Input("", true);
            var presets = Forensics.Presets.ToDictionary(p => p.key, p => Ui.Chk(L.T(p.label)));
            var utf16 = Ui.Chk(L.T("Also search UTF-16 (Windows Unicode text)"), true);
            var from = Ui.Input("", true); var len = Ui.Input("", true);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Search"), () =>
            {
                string s = src();
                if (s == "") { Toast(L.T("Choose a source first."), true); return; }
                var kws = kw.Text.Split('\n').Select(x => x.Trim()).Where(x => x != "").ToList();
                var hx = hex.Text.Split(';', ',').Select(x => x.Trim()).Where(x => x != "").ToList();
                var pr = presets.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList();
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.Search(s, kws, hx, pr, utf16.IsChecked == true, ParseMb(from.Text), ParseMb(len.Text), p), hits =>
                {
                    res.Children.Add(Ui.Verdict(hits.Count > 0 ? "warn" : "ok", hits.Count.ToString("N0"), Ui.Tx(L.T("hits"), 16, true),
                        Ui.Tx(string.Join(" · ", hits.GroupBy(h => h.Pattern).Select(g => $"{g.Key}: {g.Count()}")), 13)));
                    res.Children.Add(Ui.Table(new[] { L.T("Offset (hex)"), L.T("Offset"), L.T("Sector"), L.T("Pattern"), L.T("Match"), L.T("Context") },
                        hits.Select(h => new GridRow(h, $"0x{h.Offset:X}", h.Offset.ToString("N0"), (h.Offset / 512).ToString("N0"), h.Pattern, h.Text, h.Context)), new[] { 0, 1, 2, 4, 5 },
                        r => Go("tools", $"hex|{s}|{((Forensics.Hit)r.Tag).Offset}"), new double[] { 110, 110, 90, 150, -1, -2 }));
                    res.Children.Add(Ui.Row(Ui.Tx(L.T("Double-click a hit to open it in the hex viewer."), 12, false, Ui.Muted),
                        Ui.Btn(L.T("Export CSV"), () => SaveText("search_hits.csv", "offset,sector,pattern,match,context\n" + string.Concat(hits.Select(h => $"{h.Offset},{h.Offset / 512},\"{h.Pattern.Replace("\"", "\"\"")}\",\"{h.Text.Replace("\"", "\"\"")}\",\"{h.Context.Replace("\"", "\"\"")}\"\n"))), "small")));
                }, () => go.IsEnabled = true);
            }, "primary", "eye");
            return Ui.Card(L.T("Keyword & pattern search"), "02",
                Ui.Tx(L.T("Physical search across the raw bytes (chapter 4, slide 22): finds text even in deleted files, slack and unallocated space — ASCII and UTF-16, hex byte patterns, and ready patterns for e-mails, URLs, IPs, wallets, cards and phone numbers."), 13, false, Ui.Muted),
                su, Ui.Field(L.T("Keywords (one per line)"), kw), Ui.Field(L.T("Hex patterns (separate with ;)"), hex, "FF D8 FF ; 50 4B 03 04"),
                Ui.Tx(L.T("Ready patterns"), 13, true), Ui.Row(presets.Values.ToArray()), utf16,
                Ui.Cols(2, Ui.Field(L.T("Start at (MB, optional)"), from), Ui.Field(L.T("Length (MB, empty = to the end)"), len)), Ui.Row(go), job, res);
        }

        static void SaveText(string name, string text)
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = name, Filter = L.T("All files") + "|*.*" };
            if (d.ShowDialog() != true) return;
            File.WriteAllText(d.FileName, text, new UTF8Encoding(true));
            Toast(L.T("Saved"));
        }

        // ---------------------------------------------------------------- carving
        FrameworkElement ToolCarve(string path)
        {
            var (su, src) = SourcePicker(L.T("Source (image file or disk)"), path);
            var kinds = Forensics.CarveKinds.ToDictionary(k => k, k => Ui.Chk(L.T(k), k.Contains("JPEG") || k.Contains("PNG") || k.Contains("PDF") || k.Contains("ZIP")));
            var extract = Ui.Chk(L.T("Extract (carve) JPEG, PNG, GIF, PDF and ZIP files"), true);
            var (ou, outDir) = PathPicker(L.T("Output folder for carved files"), Path.Combine(Store.ReportsDir, "carved_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")), false, true);
            var maxMb = Ui.Input("30", true);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Scan"), () =>
            {
                string s = src(), od = outDir.Text.Trim().Trim('"');
                if (s == "") { Toast(L.T("Choose a source first."), true); return; }
                var sel = kinds.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList();
                if (sel.Count == 0) { Toast(L.T("Choose at least one file type."), true); return; }
                long max = Math.Max(1, ParseMb(maxMb.Text)); bool ex = extract.IsChecked == true;
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.Carve(s, sel, ex, od, max, p), found =>
                {
                    int carved = found.Count(f => f.File != null);
                    res.Children.Add(Ui.Verdict("warn", found.Count.ToString("N0"), Ui.Tx(L.T("signatures found") + $" · {carved} {L.T("files carved")}", 16, true),
                        Ui.Tx(string.Join(" · ", found.GroupBy(f => f.Kind).Select(g => $"{L.T(g.Key)}: {g.Count()}")), 13)));
                    res.Children.Add(Ui.Table(new[] { L.T("Offset (hex)"), L.T("Sector"), L.T("Type"), L.T("Size"), L.T("Carved file"), "SHA-256" },
                        found.Select(f => new GridRow(f, $"0x{f.Offset:X}", (f.Offset / 512).ToString("N0"), L.T(f.Kind), f.Size > 0 ? Fmt.Bytes(f.Size) : "—", f.File == null ? "" : Path.GetFileName(f.File), f.Sha256 ?? "")),
                        new[] { 0, 1, 4, 5 }, r => { var f = (Forensics.Carved)r.Tag; if (f.File != null) Store.OpenInShell(f.File); else Go("tools", $"hex|{s}|{f.Offset}"); }, new double[] { 120, 100, 170, 90, -1, -2 }));
                    if (carved > 0) res.Children.Add(Ui.Row(Ui.Btn(L.T("Open output folder"), () => Store.OpenInShell(od), "plain", "folder")));
                }, () => go.IsEnabled = true);
            }, "primary", "eye");
            return Ui.Card(L.T("Signature scan & carving"), "03",
                Ui.Tx(L.T("Data carving (chapter 3): finds files by their header signatures regardless of the file system, so deleted files in unallocated space can be recovered. JPEG, PNG, GIF, PDF and ZIP/Office are cut from header to footer."), 13, false, Ui.Muted),
                su, Ui.Row(kinds.Values.ToArray()), extract, ou, Ui.Field(L.T("Maximum file size (MB)"), maxMb), Ui.Row(go), job, res);
        }

        // ---------------------------------------------------------------- strings
        FrameworkElement ToolStrings(string path)
        {
            var (su, src) = SourcePicker(L.T("Source (file, image or disk)"), path);
            var min = Ui.Input("6", true); var utf16 = Ui.Chk(L.T("Also UTF-16LE strings"), true);
            var from = Ui.Input("", true); var len = Ui.Input(path != null && Disks.IsDevicePath(path) ? "256" : "", true);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Extract strings"), () =>
            {
                string s = src();
                if (s == "") { Toast(L.T("Choose a source first."), true); return; }
                int m = int.TryParse(min.Text, out var mm) ? Math.Max(3, mm) : 6;
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.Strings(s, m, utf16.IsChecked == true, ParseMb(from.Text), ParseMb(len.Text), p), list =>
                {
                    var filter = Ui.Input("");
                    var tableHost = new ContentControl();
                    void Show()
                    {
                        var f = filter.Text.Trim();
                        var sel = f == "" ? list : list.Where(x => x.Text.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
                        tableHost.Content = Ui.Table(new[] { L.T("Offset (hex)"), L.T("Encoding"), L.T("Text") }, sel.Take(50000).Select(x => new GridRow(x, $"0x{x.Offset:X}", x.Enc, x.Text)), new[] { 0, 2 }, null, new double[] { 120, 90, -1 });
                    }
                    filter.TextChanged += (_, _) => Show();
                    Show();
                    res.Children.Add(Ui.Verdict("ok", list.Count.ToString("N0"), Ui.Tx(L.T("strings"), 16, true)));
                    res.Children.Add(Ui.Field(L.T("Filter"), filter));
                    res.Children.Add(tableHost);
                    res.Children.Add(Ui.Row(Ui.Btn(L.T("Export TXT"), () => SaveText("strings.txt", string.Concat(list.Select(x => $"0x{x.Offset:X}\t{x.Enc}\t{x.Text}\n"))), "small")));
                }, () => go.IsEnabled = true);
            }, "primary", "eye");
            return Ui.Card(L.T("Strings"), "04", Ui.Tx(L.T("Readable text runs inside any binary — executables, memory dumps, images, disks (like the 'strings' utility)."), 13, false, Ui.Muted),
                su, Ui.Cols(3, Ui.Field(L.T("Minimum length"), min), Ui.Field(L.T("Start at (MB, optional)"), from), Ui.Field(L.T("Length (MB, empty = to the end)"), len)), utf16, Ui.Row(go), job, res);
        }

        // ---------------------------------------------------------------- entropy map
        FrameworkElement ToolEntropy(string path)
        {
            var (su, src) = SourcePicker(L.T("Source (file, image or disk)"), path);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Build entropy map"), () =>
            {
                string s = src();
                if (s == "") { Toast(L.T("Choose a source first."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.EntropyMap(s, p), m =>
                {
                    var canvas = new Canvas { Height = 120, FlowDirection = FlowDirection.LeftToRight, Background = Ui.White, ClipToBounds = true };
                    var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Height = 120, FlowDirection = FlowDirection.LeftToRight };
                    foreach (var e in m.e)
                    {
                        Brush c = e >= 7.9 ? Ui.Red : e >= 7.5 ? Ui.Orange : e >= 6 ? Ui.Yellow : e >= 1 ? new SolidColorBrush(Color.FromRgb(0xC9, 0xC3, 0xB5)) : Ui.White;
                        var col = new Grid();
                        col.Children.Add(new Rectangle { Fill = c, VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(2, 120 * e / 8) });
                        grid.Children.Add(col);
                    }
                    var runs = new List<(long from, long to, double avg)>();
                    for (int i = 0; i < m.e.Count; i++)
                    {
                        if (m.e[i] < 7.9) continue;
                        int j = i; while (j + 1 < m.e.Count && m.e[j + 1] >= 7.9) j++;
                        runs.Add((i * m.block, (j + 1) * m.block, m.e.Skip(i).Take(j - i + 1).Average()));
                        i = j;
                    }
                    res.Children.Add(Ui.Card(L.T("Entropy map"), m.e.Count.ToString(),
                        new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Child = grid },
                        Ui.Row(Ui.Tag(L.T("≥ 7.9 encrypted / compressed"), Ui.Red, Ui.White), Ui.Tag("7.5–7.9", Ui.Orange), Ui.Tag("6–7.5", Ui.Yellow), Ui.Tag(L.T("< 1 empty / zeros"))),
                        Ui.Tx($"{L.T("block")} {Fmt.Bytes(m.block)} · {L.T("average")} {m.e.DefaultIfEmpty(0).Average():0.00} {L.T("bits/byte")}", 12.5, false, Ui.Muted),
                        runs.Count == 0 ? Ui.Finding("info", L.T("No region with entropy ≥ 7.9.")) :
                        Ui.Table(new[] { L.T("From (hex)"), L.T("To (hex)"), L.T("Size"), L.T("Average entropy") }, runs.Select(r => new GridRow(r, $"0x{r.from:X}", $"0x{r.to:X}", Fmt.Bytes(r.to - r.from), r.avg.ToString("0.000"))), new[] { 0, 1 }, r => Go("tools", $"hex|{s}|{(((long from, long to, double avg))r.Tag).from}"))));
                }, () => go.IsEnabled = true);
            }, "primary", "eye");
            return Ui.Card(L.T("Entropy map"), "05", Ui.Tx(L.T("Shows where the data is random (encrypted containers, compressed archives) and where it is empty — useful to spot hidden VeraCrypt volumes or wiped areas."), 13, false, Ui.Muted), su, Ui.Row(go), job, res);
        }

        // ---------------------------------------------------------------- wipe verification
        FrameworkElement ToolWipe(string path)
        {
            var (su, src) = SourcePicker(L.T("Target drive or file to check"), path);
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Check"), () =>
            {
                string s = src();
                if (s == "") { Toast(L.T("Choose a source first."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.WipeCheck(s, p), r =>
                {
                    long tot = r.ZeroSectors + r.FfSectors + r.NonZeroSectors;
                    bool clean = r.NonZeroSectors == 0;
                    res.Children.Add(Ui.Verdict(clean ? "ok" : "bad", clean ? "0x00" : "≠0", Ui.Tx(clean ? L.T("Forensically clean: every sector is zero (or 0xFF).") : L.T("NOT clean: sectors with data remain."), 16, true),
                        Ui.Tx($"{tot:N0} {L.T("sectors")} · 0x00 {r.ZeroSectors:N0} · 0xFF {r.FfSectors:N0} · {L.T("data")} {r.NonZeroSectors:N0} ({(tot > 0 ? 100.0 * r.NonZeroSectors / tot : 0):0.###}%)", 13)));
                    if (r.FirstNonZero.Count > 0) res.Children.Add(Ui.Finding("warning", L.T("First sectors with data (LBA)") + ": " + string.Join(", ", r.FirstNonZero)));
                    if (r.Bad.Count > 0) res.Children.Add(Ui.Finding("warning", $"{r.Bad.Count} {L.T("unreadable sectors")}"));
                }, () => go.IsEnabled = true);
            }, "primary", "shield");
            return Ui.Card(L.T("Wipe verification"), "06", Ui.Tx(L.T("Before copying evidence to a target drive it must be forensically wiped (chapter 4, slide 29: dd if=/dev/zero). This read-only check proves that every sector is empty, so no residue from an earlier case can mix with the new evidence."), 13, false, Ui.Muted),
                su, Ui.Row(go), job, res);
        }

        // ---------------------------------------------------------------- checksum file
        FrameworkElement ToolChecksum()
        {
            var (fu, file) = PathPicker(L.T("Checksum file (.md5 / .sha1 / .sha256 — md5sum or BSD format)"), "", true, false, "Checksums|*.md5;*.sha1;*.sha256;*.sha512;*.txt|" + L.T("All files") + "|*.*");
            var job = new StackPanel(); var res = new StackPanel();
            Button go = null;
            go = Ui.Btn(L.T("Verify"), () =>
            {
                string f = file.Text.Trim().Trim('"');
                if (!File.Exists(f)) { Toast(L.T("Choose a source first."), true); return; }
                res.Children.Clear(); go.IsEnabled = false;
                RunJob(job, p => Forensics.VerifyChecksums(f, p), items =>
                {
                    int ok = items.Count(i => i.Status == "OK");
                    res.Children.Add(Ui.Verdict(ok == items.Count && items.Count > 0 ? "ok" : "bad", $"{ok}/{items.Count}", Ui.Tx(ok == items.Count ? L.T("All files match.") : L.T("Some files failed or are missing."), 16, true),
                        Ui.Tx(string.Join(" · ", items.GroupBy(i => i.Status).Select(g => $"{g.Key}: {g.Count()}")), 13)));
                    res.Children.Add(Ui.Table(new[] { L.T("Status"), L.T("File"), L.T("Algorithm"), L.T("Expected"), L.T("Actual") },
                        items.Select(i => new GridRow(i, i.Status, i.Path, Algos.Label(i.Algo), i.Expected, i.Actual ?? "")), new[] { 1, 3, 4 }, null, new double[] { 90, -2, 90, -2, -2 }));
                }, () => go.IsEnabled = true);
            }, "primary", "shield");
            return Ui.Card(L.T("Verify checksum file"), "07", Ui.Tx(L.T("Checks every file listed in a checksum file (like 'sha256sum -c'). BASMA exports these files from any hash result, so a copy can be verified anywhere — even on Linux."), 13, false, Ui.Muted), fu, Ui.Row(go), job, res);
        }

        // ---------------------------------------------------------------- USB history
        FrameworkElement ToolUsb()
        {
            List<Forensics.UsbDev> list;
            try { list = Forensics.UsbHistory(); }
            catch (Exception e) { return Ui.Finding("critical", e.Message); }
            return Ui.Card(L.T("USB device history"), list.Count.ToString(),
                Ui.Tx(L.T("USB storage devices ever connected to THIS computer, from the registry (HKLM\\SYSTEM\\CurrentControlSet\\Enum\\USBSTOR) — the trace a USB stick leaves even after it is gone (chapter 4, USB). The time is the last write of the device key, an approximate last connection."), 13, false, Ui.Muted),
                list.Count == 0 ? Ui.Finding("info", L.T("No USB storage devices recorded.")) :
                Ui.Table(new[] { L.T("Vendor"), L.T("Product"), L.T("Revision"), L.T("Serial"), L.T("Friendly name"), L.T("Key last written (UTC)") },
                    list.Select(u => new GridRow(u, u.Vendor, u.Product, u.Revision, u.Serial, u.Friendly, u.LastWrite)), new[] { 3, 5 }, null, new double[] { 110, -1, 70, -2, -2, 170 }),
                Ui.Row(Ui.Btn(L.T("Export CSV"), () => SaveText("usb_history.csv", "vendor,product,revision,serial,friendly,last_write_utc\n" + string.Concat(list.Select(u => $"\"{u.Vendor}\",\"{u.Product}\",\"{u.Revision}\",\"{u.Serial}\",\"{u.Friendly}\",{u.LastWrite}\n"))), "small")));
        }

        // ---------------------------------------------------------------- blank custody form
        FrameworkElement ToolBlankCoc()
        {
            FrameworkElement B(string lab, string lang) => Ui.Btn(Fmt.Ltr(lab), () =>
            {
                var (html, _) = Report.CustodyRecord(null, null, lang, true);
                var p = Report.Save(html, Store.ReportsDir, $"blank_chain_of_custody_{lang}_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                Store.OpenInShell(p);
            }, "primary", "print");
            return Ui.Card(L.T("Blank custody form"), "10",
                Ui.Tx(L.T("A complete printable chain-of-custody record for field use: case and legal authority, exhibit identification, seizure (system state, actions before power-off, BIOS clock), order of volatility (RFC 3227), packaging and seals, acquisition and verification, a transfer log with signature lines, continuity review, final disposition, principles (ACPO, ISO/IEC 27037, NIST SP 800-86) and certifications."), 13, false, Ui.Muted),
                Ui.Row(B("سجل سلسلة الحيازة — فارغ (AR)", "ar"), B("Chain of Custody Record — blank (EN)", "en")),
                Ui.Tx(L.T("For a filled record of an exhibit, open the exhibit page in a case."), 12.5, false, Ui.Muted));
        }
    }
}
