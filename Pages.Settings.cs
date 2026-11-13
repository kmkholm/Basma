using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Basma
{
    public partial class MainWindow
    {
        FrameworkElement PageSettings()
        {
            Head(L.T("Settings"), L.T("Settings & about"));
            var ex = Ui.Input(Store.S.Examiner); var ag = Ui.Input(Store.S.Agency);
            var lang = Ui.Combo(new[] { ("ar", "العربية"), ("en", "English") }, Store.S.Lang);
            var algoRow = new WrapPanel(); var boxes = new Dictionary<string, CheckBox>();
            foreach (var a in Algos.All) { var cb = Ui.Chk(Algos.Label(a), Store.S.Algos.Contains(a)); cb.FontFamily = Ui.Mono; cb.IsEnabled = Algos.Supported(a); boxes[a] = cb; algoRow.Children.Add(cb); }
            var save = Ui.Btn(L.T("Save"), () =>
            {
                Store.S.Examiner = ex.Text.Trim(); Store.S.Agency = ag.Text.Trim(); Store.S.Lang = Ui.Val(lang);
                Store.S.Algos = boxes.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList();
                if (Store.S.Algos.Count == 0) Store.S.Algos = Algos.Default.ToList();
                Store.Save(); ApplyLang(); Toast(L.T("Saved")); Go("settings");
            }, "primary");
            var data = Ui.Sel(Store.Data, 13);
            var admin = Native.IsAdmin();
            var testOut = new StackPanel();
            var testBtn = Ui.Btn(L.T("Run self-test"), () =>
            {
                testOut.Children.Clear();
                var res = SelfTest.Run(null);
                int pass = res.Count(r => r.ok);
                testOut.Children.Add(Ui.Verdict(pass == res.Count ? "ok" : "bad", $"{pass}/{res.Count}", Ui.Tx(pass == res.Count ? L.T("All checks passed.") : L.T("Some checks failed."), 15, true)));
                foreach (var r in res) testOut.Children.Add(Ui.Tx((r.ok ? "✓  " : "✗  ") + r.name + (r.ok ? "" : "  —  " + r.detail), 12.5, !r.ok, r.ok ? Ui.Green : Ui.Red, true));
            }, "plain", "shield");
            var left = new StackPanel();
            left.Children.Add(Ui.Card(L.T("Settings"), "01", Ui.Field(L.T("Examiner name (stamped on every entry)"), ex), Ui.Field(L.T("Agency"), ag), Ui.Field(L.T("Language"), lang),
                Ui.Field(L.T("Default algorithms"), algoRow), Ui.Row(save)));
            left.Children.Add(Ui.Card(L.T("System"), "02",
                Ui.Field(L.T("Data folder"), data), Ui.Row(Ui.Btn(L.T("Open folder"), () => Store.OpenInShell(Store.Data), "small", "folder")),
                Ui.Verdict(admin ? "ok" : "warn", admin ? "✓" : "!", Ui.Tx(admin ? L.T("Running as administrator (disk access on)") : L.T("Not administrator — disk reading is off"), 15, true), admin ? new Border() : AdminButton()),
                Ui.Tx($"SHA3-256: {(Algos.Supported("sha3_256") ? L.T("supported") : L.T("not supported by this Windows version"))} · .NET {Environment.Version} · {Environment.OSVersion}", 12, false, Ui.Muted, true),
                Ui.Row(testBtn), testOut));
            var about = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            top.Children.Add(new Border { Background = Ui.Ink, Width = 110, Height = 110, Child = Logo(90) });
            var tt = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tt.Children.Add(Ui.Tx("بصمة  ·  BASMA", 30, true));
            tt.Children.Add(Ui.Tx($"v{App.Version} · MIT", 13, false, Ui.Muted, true));
            top.Children.Add(tt);
            about.Children.Add(top);
            about.Children.Add(Ui.Tx(L.T("A hash is the fingerprint of evidence: change one bit and the fingerprint changes completely. BASMA computes it for files, folders, split images, volumes and whole physical disks, compares sources, and keeps a hash-chained chain of custody that proves nothing was changed — without a server and without installing anything."), 13.5));
            var link = new Hyperlink(new Run("0000-0002-1227-387X")) { NavigateUri = new Uri("https://orcid.org/0000-0002-1227-387X") };
            link.RequestNavigate += (_, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } };
            var orcid = new TextBlock(); orcid.Inlines.Add(link);
            var mail = new Hyperlink(new Run("kmkhol01@gmail.com")) { NavigateUri = new Uri("mailto:kmkhol01@gmail.com") };
            mail.RequestNavigate += (_, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } };
            var em = new TextBlock(); em.Inlines.Add(mail);
            var kv = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); kv.ColumnDefinitions.Add(new ColumnDefinition());
            var rows = new (string k, FrameworkElement v)[]
            {
                (L.T("Developer"), Ui.Tx(L.T("Dr. Mohammed Tawfik"), 14, true)), ("", Ui.Tx(L.T("Department of Cyber Security, Ajloun National University, Jordan"), 13)),
                ("E-mail", em), ("ORCID", orcid), (L.T("Licence"), Ui.Tx("MIT", 13)),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                kv.RowDefinitions.Add(new RowDefinition());
                var k = Ui.Tx(rows[i].k, 13, true); k.Margin = new Thickness(0, 0, 0, 6);
                rows[i].v.Margin = new Thickness(0, 0, 0, 6);
                Grid.SetRow(k, i); Grid.SetRow(rows[i].v, i); Grid.SetColumn(rows[i].v, 1);
                kv.Children.Add(k); kv.Children.Add(rows[i].v);
            }
            about.Children.Add(kv);
            return Ui.Split(left, Ui.Card(L.T("About BASMA"), "03", about), 1, 1);
        }
    }
}
