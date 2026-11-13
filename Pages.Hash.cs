using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Basma
{
    public partial class MainWindow
    {
        /// <summary>Filter controls shared by the folder and drive modes.</summary>
        sealed class FilterUi
        {
            public TextBox Patterns = Ui.Input("", true), MaxMb = Ui.Input("", true);
            public CheckBox Hidden = Ui.Chk(L.T("Skip hidden files and folders")), System = Ui.Chk(L.T("Skip system files and folders"));
            public FrameworkElement Root;
            public FilterUi()
            {
                Root = new StackPanel { Children = { Ui.Cols(2, Ui.Field(L.T("Only these file types (empty = all)"), Patterns, "*.docx;*.pdf;*.jpg"), Ui.Field(L.T("Skip files larger than (MB, empty = no limit)"), MaxMb)), Ui.Row(Hidden, System) } };
            }
            public Engine.Filter Get() => Engine.Filter.Parse(Patterns.Text, Hidden.IsChecked == true, System.IsChecked == true, long.TryParse(MaxMb.Text, out var m) ? m : 0);
        }

        // ================================================================ HASH
        FrameworkElement PageHash(string preset)
        {
            Head(L.T("Hash"), L.T("Hash any file, folder, image or disk"), Ui.Btn(L.T("Reports folder"), () => Store.OpenInShell(Store.ReportsDir), "plain", "folder"));
            var sp = new StackPanel();
            string mode = "file";
            long presetStart = -1, presetCount = -1; string presetDisk = null, presetDrive = null;
            if (preset != null)
            {
                var parts = preset.Split('|');
                if (parts[0] == "range" && parts.Length == 4) { mode = "disk"; presetDisk = parts[1]; long.TryParse(parts[2], out presetStart); long.TryParse(parts[3], out presetCount); }
                else if (parts[0] == "drive" && parts.Length == 2) { mode = "drive"; presetDrive = parts[1]; }
                else if (Disks.IsDevicePath(preset)) { mode = preset.Contains("PhysicalDrive") ? "disk" : "volume"; presetDisk = preset; }
            }
            var srcHost = new ContentControl();
            var filter = new FilterUi();
            var filterCard = new Border { Child = new StackPanel { Children = { Ui.Tx(L.T("Filters"), 14, true), filter.Root } }, Margin = new Thickness(0, 6, 0, 0) };
            var (fileUi, fileBox) = PathPicker(L.T("File path"), "", true, false);
            var (dirUi, dirBox) = PathPicker(L.T("Folder path"), "", false, true);
            var (splitUi, splitBox) = PathPicker(L.T("First segment (.001)"), "", true, false, L.T("Split image") + "|*.001|" + L.T("All files") + "|*.*");
            var textBox = Ui.Input("", true, true);
            // drive (all files)
            var drives = DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch { return false; } }).Select(d => (d.RootDirectory.FullName, $"{d.Name}  {SafeVol(d)}  ·  {L.T(d.DriveType.ToString())}")).ToList();
            var driveCombo = Ui.Combo(drives, presetDrive);
            var driveUi = new StackPanel { Children = { Ui.Field(L.T("Drive / volume"), driveCombo), Ui.Finding("info", L.T("Hashes every file on the drive and builds a manifest (path, size, hashes, MAC times, signature, entropy, known hash sets). This is the logical view; use 'Physical disk' for a bit-for-bit hash of the whole device including deleted and unallocated space.")) } };
            // disk
            var disks = SafeDisks();
            var diskCombo = Ui.Combo(disks.Select(d => (d.Path, d.Title + (d.Removable ? "  ·  " + L.T("removable") : ""))), presetDisk);
            var whole = new RadioButton { Content = L.T("Whole disk"), IsChecked = presetStart < 0, Style = (Style)Application.Current.Resources["Chip"], GroupName = "rng" };
            var range = new RadioButton { Content = L.T("Sector range"), IsChecked = presetStart >= 0, Style = (Style)Application.Current.Resources["Chip"], GroupName = "rng" };
            var lbaStart = Ui.Input(presetStart >= 0 ? presetStart.ToString() : "0", true); var lbaCount = Ui.Input(presetCount > 0 ? presetCount.ToString() : "1", true);
            var rangeBox = Ui.Cols(2, Ui.Field(L.T("Start LBA"), lbaStart), Ui.Field(L.T("Sector count"), lbaCount, L.T("LBA 0 = MBR / protective MBR · LBA 1 = GPT header")));
            rangeBox.Visibility = presetStart >= 0 ? Visibility.Visible : Visibility.Collapsed;
            range.Checked += (_, _) => rangeBox.Visibility = Visibility.Visible; whole.Checked += (_, _) => rangeBox.Visibility = Visibility.Collapsed;
            var diskUi = new StackPanel();
            if (!Native.IsAdmin()) { diskUi.Children.Add(Ui.Finding("warning", L.T("Reading a disk directly needs administrator rights. The list works; hashing will ask you to restart as administrator."))); diskUi.Children.Add(AdminButton()); }
            diskUi.Children.Add(Ui.Field(L.T("Physical disk"), diskCombo));
            diskUi.Children.Add(Ui.Row(whole, range)); diskUi.Children.Add(rangeBox);
            diskUi.Children.Add(Ui.Finding("info", L.T("BASMA opens the device read-only, but Windows itself may write to a mounted disk. For evidence, connect it through a hardware write blocker.")));
            // volume (raw)
            var vols = DriveInfo.GetDrives().Select(d => (@"\\.\" + d.Name.Substring(0, 2), d.Name.Substring(0, 2) + "  " + SafeVol(d))).ToList();
            var volCombo = Ui.Combo(vols, presetDisk);
            var volUi = new StackPanel { Children = { Ui.Field(L.T("Volume (raw, sector by sector)"), volCombo), Ui.Finding("warning", L.T("A mounted, live volume changes while you read it, so its hash is a moment-in-time value. Hash the physical disk through a write blocker for evidence.")) } };
            if (!Native.IsAdmin()) volUi.Children.Add(AdminButton());

            var chips = new WrapPanel();
            var map = new Dictionary<string, FrameworkElement> { ["file"] = fileUi, ["folder"] = dirUi, ["drive"] = driveUi, ["split"] = splitUi, ["disk"] = diskUi, ["volume"] = volUi, ["text"] = Ui.Field(L.T("Text (UTF-8)"), textBox) };
            void Show(string k) { mode = k; srcHost.Content = map[k]; filterCard.Visibility = k == "folder" || k == "drive" ? Visibility.Visible : Visibility.Collapsed; }
            foreach (var (k, lab) in new[] { ("file", L.T("File")), ("folder", L.T("Folder (all files)")), ("drive", L.T("Drive / USB (all files)")), ("split", L.T("Split image .001…")), ("disk", L.T("Physical disk (raw)")), ("volume", L.T("Volume (raw)")), ("text", L.T("Text")) })
            {
                var rb = new RadioButton { Content = lab, Style = (Style)Application.Current.Resources["Chip"], GroupName = "src", IsChecked = k == mode };
                rb.Checked += (_, _) => Show(k);
                chips.Children.Add(rb);
            }
            Show(mode);

            var algoBoxes = new Dictionary<string, CheckBox>();
            var algoRow = new WrapPanel();
            foreach (var a in Algos.All)
            {
                var cb = Ui.Chk(Algos.Label(a), Store.S.Algos.Contains(a)); cb.FontFamily = Ui.Mono;
                if (!Algos.Supported(a)) { cb.IsEnabled = false; cb.IsChecked = false; cb.ToolTip = L.T("Not supported by this Windows version"); }
                algoBoxes[a] = cb; algoRow.Children.Add(cb);
            }
            var twice = Ui.Chk(L.T("Second pass: read the source again and confirm the same hash"));
            var expected = Ui.Input("", true);
            var jobHost = new StackPanel(); var resHost = new StackPanel();
            Button run = null;
            run = Ui.Btn(L.T("Compute hash"), () =>
            {
                var algos = algoBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                if (algos.Count == 0) { Toast(L.T("Choose at least one algorithm."), true); return; }
                string exp = expected.Text.Trim();
                if (mode == "text") { resHost.Children.Clear(); resHost.Children.Add(HashResultView(Engine.HashText(textBox.Text, algos), exp, null)); return; }
                string path = mode switch { "file" => fileBox.Text, "folder" => dirBox.Text, "drive" => Ui.Val(driveCombo), "split" => splitBox.Text, "disk" => Ui.Val(diskCombo), "volume" => Ui.Val(volCombo), _ => null };
                if (string.IsNullOrWhiteSpace(path)) { Toast(L.T("Choose a source first."), true); return; }
                path = path.Trim().Trim('"');
                long st = 0, cnt = -1;
                if (mode == "disk" && range.IsChecked == true && (!long.TryParse(lbaStart.Text, out st) || !long.TryParse(lbaCount.Text, out cnt) || cnt <= 0)) { Toast(L.T("Enter a valid sector range."), true); return; }
                var flt = mode == "folder" || mode == "drive" ? filter.Get() : null;
                bool two = twice.IsChecked == true;
                resHost.Children.Clear(); run.IsEnabled = false;
                RunJob(jobHost, p =>
                {
                    var r1 = HashAny(path, mode, algos, p, st, cnt, flt);
                    HashResult r2 = null;
                    if (two) { p.Done = 0; p.FilesDone = 0; p.Current = L.T("second pass") + " · " + path; r2 = HashAny(path, mode, algos, p, st, cnt, flt); }
                    return (r1, r2);
                }, res =>
                {
                    var verdict = Verdict(res.r1, exp, res.r2);
                    var rep = SaveHashReport(res.r1, exp, Store.S.Lang);
                    Store.AddHistory(res.r1, "quick", verdict, rep);
                    resHost.Children.Add(HashResultView(res.r1, exp, res.r2, rep));
                }, () => run.IsEnabled = true);
            }, "primary", "play");

            sp.Children.Add(Ui.Card(L.T("Source"), "01", chips, srcHost, filterCard));
            sp.Children.Add(Ui.Card(L.T("Algorithms and checks"), "02", algoRow, twice, Ui.Field(L.T("Expected hash (optional) — any algorithm, detected by length"), expected), Ui.Row(run), jobHost));
            sp.Children.Add(resHost);
            return sp;
        }

        public static void ExportChecksum(HashResult r, string algo)
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                FileName = CaseFile.Safe(Path.GetFileName(r.Target.TrimEnd('\\', '/'))) + "." + (algo == "sha256" ? "sha256" : algo),
                Filter = $"{Algos.Label(algo)} checksum|*.{algo}|" + L.T("All files") + "|*.*"
            };
            if (d.ShowDialog() != true) return;
            File.WriteAllText(d.FileName, Forensics.ChecksumText(r, algo), new System.Text.UTF8Encoding(false));
            Toast(L.T("Saved"));
            Store.OpenInShell(d.FileName);
        }
    }
}
