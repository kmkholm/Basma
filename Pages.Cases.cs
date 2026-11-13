using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Basma
{
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- date helpers (local entry, UTC storage)
        static (FrameworkElement ui, Func<string> utc) DateField(string label, string utc, bool nowIfEmpty = true)
        {
            var d = Fmt.ParseUtc(utc);
            var box = Ui.Input(d != null ? d.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : nowIfEmpty ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "", true);
            var now = Ui.Btn(L.T("Now"), () => box.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "small");
            now.Margin = new Thickness(8, 0, 0, 0);
            var dp = new DockPanel(); DockPanel.SetDock(now, Dock.Right); dp.Children.Add(now); dp.Children.Add(box);
            return (Ui.Field(label, dp, L.T("your local time (yyyy-MM-dd HH:mm:ss); stored as UTC")), () =>
            {
                var t = box.Text.Trim();
                if (t == "") return null;
                return DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var x) ? Fmt.Utc(x) : null;
            });
        }

        // ================================================================ CASES
        FrameworkElement PageCases()
        {
            bool showNew = (arg as string) == "new";
            var sp = new StackPanel();
            var formHost = new StackPanel();
            Head(L.T("Cases & custody"), L.T("Case files"), Ui.Btn(L.T("New case"), () => { formHost.Children.Clear(); formHost.Children.Add(CaseForm(null)); }, "primary", "plus"));
            sp.Children.Add(formHost);
            if (showNew || string.IsNullOrWhiteSpace(Store.S.Examiner))
            {
                if (string.IsNullOrWhiteSpace(Store.S.Examiner)) sp.Children.Insert(0, Ui.Finding("warning", L.T("Set your examiner name in Settings first; it is stamped on every entry.")));
                formHost.Children.Add(CaseForm(null));
            }
            var cases = Store.AllCases();
            if (cases.Count == 0)
                sp.Children.Add(Ui.Card2Box(new StackPanel { Children = { Ui.Tx(L.T("No cases yet"), 22, true), Ui.Tx(L.T("Open a case: its number, title and the legal authority that permits the seizure."), 14, false, Ui.Muted) } }, Ui.Card2, 30));
            foreach (var c in cases)
            {
                var st = c.Stats();
                string worst = st.broken > 0 ? "BROKEN" : st.warnings > 0 ? "WARNINGS" : st.ev > 0 ? "INTACT" : null;
                var g = new Grid();
                foreach (var w in new[] { 230.0, -1, 220, 150 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                var a = new StackPanel(); a.Children.Add(Ui.Tx(c.Info.CaseNo, 20, true, Ui.Ink, true, false)); a.Children.Add(Ui.Tx($"{L.T(c.Info.Status)} · {c.Info.Opened}", 12, false, Ui.Muted));
                var b = new StackPanel(); b.Children.Add(Ui.Tx(c.Info.Title, 16, true)); b.Children.Add(Ui.Tx($"{c.Info.Authority} {c.Info.AuthorityRef} · {c.Info.Lead}", 12, false, Ui.Muted));
                var steps = new StackPanel { Orientation = Orientation.Horizontal };
                var stage = Stage(c);
                foreach (var s in stage) steps.Children.Add(new Border { Width = 24, Height = 14, Margin = new Thickness(0, 0, 3, 0), BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Background = s.done ? Ui.Ink : s.next ? Ui.Orange : Ui.White });
                var cc = new StackPanel(); cc.Children.Add(steps); cc.Children.Add(Ui.Tx($"{st.ev} {L.T("exhibits")} · {st.custody} {L.T("custody entries")}", 12, false, Ui.Muted));
                var d = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                if (worst != null) d.Children.Add(Ui.Stamp(worst));
                if (!c.Log.Ok) d.Children.Add(Ui.Tag(L.T("LEDGER BROKEN"), Ui.Red, Ui.White));
                Grid.SetColumn(b, 1); Grid.SetColumn(cc, 2); Grid.SetColumn(d, 3);
                g.Children.Add(a); g.Children.Add(b); g.Children.Add(cc); g.Children.Add(d);
                var btn = new Button { Content = g, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 6, 14) };
                var cf = c; btn.Click += (_, _) => Go("case", (cf, "ev"));
                sp.Children.Add(btn);
            }
            return sp;
        }

        static List<(string code, string label, bool done, bool next)> Stage(CaseFile c)
        {
            var st = c.Stats();
            var done = new (string code, string en, bool ok)[]
            {
                ("case", "Open case", true), ("checklist", "Live response", st.checks >= 6), ("evidence", "Register exhibits", st.ev > 0),
                ("custody", "Chain of custody", st.ev > 0 && c.Items.All(i => c.Custody.Any(x => x.EvidenceId == i.Id))),
                ("acquire", "Acquisition hash", st.ev > 0 && st.acquired >= st.ev), ("verify", "Verification", st.acquired > 0 && st.verified >= st.acquired),
                ("report", "Report", st.reports > 0),
            };
            string next = done.FirstOrDefault(x => !x.ok).code;
            return done.Select(x => (x.code, L.T(x.en), x.ok, x.code == next)).ToList();
        }

        FrameworkElement CaseForm(CaseFile existing)
        {
            var i = existing?.Info ?? new CaseInfo { CaseNo = $"CASE-{DateTime.Now:yyyy}-{(Store.AllCases().Count + 1):D3}", Lead = Store.S.Examiner, Agency = Store.S.Agency };
            var no = Ui.Input(i.CaseNo, true); no.IsReadOnly = existing != null;
            var title = Ui.Input(i.Title); var type = new ComboBox { IsEditable = true, Text = i.Type };
            foreach (var t in new[] { "Data theft", "Online fraud", "Intrusion", "Online extortion", "Insider incident", "Ransomware", "Other" }) type.Items.Add(L.T(t));
            var agency = Ui.Input(i.Agency); var lead = Ui.Input(i.Lead); var auth = Ui.Input(i.Authority); var aref = Ui.Input(i.AuthorityRef, true);
            var (openUi, open) = DateField(L.T("Opened"), i.Opened);
            var status = Ui.Combo(new[] { ("open", L.T("open")), ("closed", L.T("closed")), ("archived", L.T("archived")) }, i.Status);
            var desc = Ui.Input(i.Description, false, true);
            var save = Ui.Btn(existing == null ? L.T("Create case") : L.T("Save"), () =>
            {
                try
                {
                    var n = new CaseInfo { CaseNo = no.Text.Trim(), Title = title.Text, Type = type.Text, Agency = agency.Text, Lead = lead.Text, Authority = auth.Text, AuthorityRef = aref.Text, Opened = open(), Status = Ui.Val(status), Description = desc.Text };
                    if (string.IsNullOrWhiteSpace(n.CaseNo)) { Toast(L.T("Case number is required."), true); return; }
                    if (existing == null) { var c = CaseFile.Create(Store.CasesDir, n, Store.Actor); Toast(L.T("Saved")); Go("case", (c, "ev")); }
                    else { existing.UpdateInfo(n, Store.Actor); Toast(L.T("Saved")); Go("case", (existing, "info")); }
                }
                catch (Exception e) { Toast(e.Message, true); }
            }, "primary");
            return Ui.Card(existing == null ? L.T("New case") : L.T("Case details"), existing == null ? "+" : "i",
                Ui.Cols(3, Ui.Field(L.T("Case number") + " *", no), Ui.Field(L.T("Title"), title), Ui.Field(L.T("Case type"), type),
                    Ui.Field(L.T("Agency"), agency), Ui.Field(L.T("Lead examiner"), lead), openUi,
                    Ui.Field(L.T("Legal authority"), auth, L.T("search warrant / written authorisation / owner consent")), Ui.Field(L.T("Reference no."), aref), Ui.Field(L.T("Status"), status)),
                Ui.Field(L.T("Description"), desc), Ui.Row(save));
        }

        // ================================================================ CASE
        FrameworkElement PageCase((CaseFile c, string tab) a)
        {
            var c = new CaseFile(a.c.Folder); // always reload from the ledger
            arg = (c, a.tab);
            var tab = a.tab ?? "ev";
            Head(L.T("Case"), c.Info.Title ?? c.Info.CaseNo, Ui.Btn(L.T("Cases"), () => Go("cases"), "plain", "back"), Ui.Btn(L.T("New exhibit"), () => Go("ev", (c, 0)), "primary", "plus"));
            var sp = new StackPanel();
            var hd = new DockPanel();
            var stamp = Ui.Stamp(c.Info.Status == "open" ? "WARNINGS" : "COMPUTED"); ((TextBlock)stamp.Child).Text = L.T(c.Info.Status);
            DockPanel.SetDock(stamp, Dock.Right); hd.Children.Add(stamp);
            var hl = new StackPanel(); hl.Children.Add(Ui.Tx(c.Info.CaseNo, 50, true, Ui.Ink, true, false));
            hl.Children.Add(Ui.Tx($"{c.Info.Authority} · {c.Info.AuthorityRef} · {c.Info.Lead} · {c.Info.Opened}", 13, false, Ui.Muted));
            hd.Children.Add(hl);
            sp.Children.Add(hd);
            if (!c.Log.Ok) sp.Children.Add(Ui.Verdict("bad", "✗", Ui.Tx(L.T("The case ledger is BROKEN") + $" — #{c.Log.BrokenAt} ({c.Log.Reason})", 16, true), Ui.Tx(L.T("Someone edited or deleted an entry in ledger.jsonl outside BASMA."), 13)));
            var stg = Stage(c);
            var flow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 16, 0, 0) };
            for (int i = 0; i < stg.Count; i++)
            {
                var s = new StackPanel { Margin = new Thickness(10) };
                s.Children.Add(Ui.Tx((i + 1).ToString("D2"), 24, true, Ui.Ink, true)); s.Children.Add(Ui.Tx(stg[i].label, 13.5, true)); s.Children.Add(Ui.Tx(stg[i].done ? "✓" : "", 12));
                flow.Children.Add(new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(0, 0, 2, 0), Background = stg[i].done ? Ui.Yellow : stg[i].next ? Ui.Orange : Ui.CardBg, Child = s });
            }
            sp.Children.Add(new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Child = flow });
            var nx = stg.FirstOrDefault(x => x.next);
            if (nx.code != null)
            {
                string t2 = nx.code switch { "checklist" => "check", "report" => "rep", _ => "ev" };
                sp.Children.Add(new Border
                {
                    Background = Ui.Yellow, BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 10, 0, 0),
                    Child = Ui.Row(Ui.Tag(L.T("NEXT STEP"), Ui.White, Ui.Red), Ui.Tx(nx.label, 15, true), new Border { Width = 14 }, Ui.Btn(L.T("Go"), () => { if (nx.code == "evidence") Go("ev", (c, 0)); else Go("case", (c, t2)); }, "dark"))
                });
            }
            var tabs = new WrapPanel { Margin = new Thickness(0, 18, 0, 16) };
            foreach (var (k, lab) in new[] { ("ev", L.T("Exhibits")), ("check", L.T("Scene & live response")), ("tl", L.T("Timeline")), ("notes", L.T("Notes & attachments")), ("rep", L.T("Report & export")), ("led", L.T("Ledger")), ("info", L.T("Case details")) })
            {
                var rb = new RadioButton { Content = lab, Style = (Style)Application.Current.Resources["Chip"], GroupName = "ctab", IsChecked = k == tab, FontSize = 14 };
                var kk = k; rb.Checked += (_, _) => Go("case", (c, kk));
                tabs.Children.Add(rb);
            }
            sp.Children.Add(tabs);
            sp.Children.Add(tab switch { "check" => TabCheck(c), "tl" => TabTimeline(c), "notes" => TabNotes(c), "rep" => TabReport(c), "led" => TabLedger(c), "info" => CaseForm(c), _ => TabExhibits(c) });
            return sp;
        }

        FrameworkElement TabExhibits(CaseFile c)
        {
            if (c.Items.Count == 0)
                return Ui.Card2Box(new StackPanel { Children = { Ui.Tx(L.T("No exhibits recorded in this case"), 20, true), Ui.Tx(L.T("Register every storage medium as a separate item: disk, USB stick, phone, forensic image…"), 14, false, Ui.Muted), new Border { Height = 10 }, Ui.Btn(L.T("New exhibit"), () => Go("ev", (c, 0)), "primary", "plus") } }, Ui.Card2, 30);
            var wrap = new WrapPanel();
            foreach (var e in c.Items)
            {
                var co = c.ContinuityOf(e);
                var acq = c.Hashes.FirstOrDefault(h => h.EvidenceId == e.Id && h.Kind == "acquisition");
                var ver = c.Hashes.LastOrDefault(h => h.EvidenceId == e.Id && h.Kind == "verification");
                var s = new StackPanel { Width = 330 };
                var hd = new DockPanel { Background = Ui.Card2 };
                var st = Ui.Stamp(co.Status, 12); DockPanel.SetDock(st, Dock.Right); hd.Children.Add(st);
                hd.Children.Add(Ui.Tx(e.ItemNo, 26, true, Ui.Ink, true));
                s.Children.Add(new Border { Child = hd, Padding = new Thickness(12, 8, 12, 8), BorderBrush = Ui.Ink, BorderThickness = new Thickness(0, 0, 0, 2) });
                var bd = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
                bd.Children.Add(Ui.Tx($"{e.Kind} {(string.IsNullOrEmpty(e.Capacity) ? "" : "· " + e.Capacity)}", 14, true));
                bd.Children.Add(Ui.Tx(e.Description, 13));
                bd.Children.Add(Ui.Tx($"{e.Make} {e.Model} {(string.IsNullOrEmpty(e.Serial) ? "" : "S/N " + e.Serial)}", 12, false, Ui.Muted, true));
                bd.Children.Add(Ui.Tx($"{L.T("Holder")}: {co.Holder ?? "—"} · {co.Location}", 13));
                s.Children.Add(bd);
                var ft = new WrapPanel { Margin = new Thickness(12, 0, 12, 8) };
                ft.Children.Add(Ui.Tag($"{c.Custody.Count(x => x.EvidenceId == e.Id)} {L.T("custody entries")}"));
                ft.Children.Add(acq == null ? Ui.Tag(L.T("no acquisition hash"), Ui.Yellow) : Ui.Tag("SHA-256 " + (acq.Get("sha256") ?? "").Substring(0, Math.Min(12, (acq.Get("sha256") ?? "").Length)) + "…"));
                if (ver != null) ft.Children.Add(Ui.Tag(L.T(ver.Result), ver.Result == "MATCH" ? Ui.OkBg : Ui.Red, ver.Result == "MATCH" ? null : Ui.White));
                s.Children.Add(ft);
                var btn = new Button { Content = s, Padding = new Thickness(0), Margin = new Thickness(0, 0, 18, 18), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var ee = e; btn.Click += (_, _) => Go("ev", (c, ee.Id));
                wrap.Children.Add(btn);
            }
            return wrap;
        }

        FrameworkElement TabCheck(CaseFile c)
        {
            var list = new StackPanel();
            int i = 0;
            foreach (var s in CustodyRules.Checklist)
            {
                i++;
                c.Checks.TryGetValue(s.code, out var st);
                bool done = st?.Done != null;
                var note = Ui.Input(st?.Note ?? ""); note.Width = 230; note.FontSize = 12.5; note.Padding = new Thickness(5, 3, 5, 3);
                var box = new Button { Content = done ? "✓" : " ", Width = 34, Height = 30, Padding = new Thickness(0), Margin = new Thickness(0, 0, 12, 0), Background = done ? Ui.Ink : Ui.White, Foreground = Ui.Yellow, FontSize = 16 };
                var code = s.code;
                box.Click += (_, _) => { c.SetCheck(code, !done, Store.Actor, note.Text, Store.Actor); Go("case", (c, "check")); };
                note.LostFocus += (_, _) => { if ((st?.Note ?? "") != note.Text) c.SetCheck(code, done, st?.By ?? Store.Actor, note.Text, Store.Actor); };
                var d = new DockPanel();
                DockPanel.SetDock(box, Dock.Left); DockPanel.SetDock(note, Dock.Right);
                d.Children.Add(box); d.Children.Add(note);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(Ui.Tx($"{i:D2}  {(L.Ar ? s.ar : s.en)}", 13.5, true));
                if (done) t.Children.Add(Ui.Tx($"{st.Done} · {st.By}", 11.5, false, Ui.Muted));
                d.Children.Add(t);
                list.Children.Add(new Border { Background = done ? Ui.OkBg : Ui.White, BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 6), Child = d });
            }
            var boxes = new StackPanel();
            var checks = new Dictionary<string, CheckBox>();
            foreach (var cmd in Live.Commands)
            {
                var cb = Ui.Chk($"{cmd.cmd}  —  {(L.Ar ? cmd.ar : cmd.en)}", true);
                checks[cmd.code] = cb; boxes.Children.Add(cb);
            }
            var job = new StackPanel(); var res = new StackPanel();
            Button run = null;
            run = Ui.Btn(L.T("Capture live state"), () =>
            {
                var sel = checks.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList();
                var tmp = Path.Combine(Path.GetTempPath(), "basma_live_" + Guid.NewGuid().ToString("N")[..8]);
                run.IsEnabled = false;
                RunJob(job, p => Live.Capture(tmp, sel, p), outs =>
                {
                    var auto = new Dictionary<string, string> { ["tasklist"] = "processes", ["netstat"] = "netstat", ["net_session"] = "sessions", ["openfiles"] = "openfiles" };
                    foreach (var o in outs)
                    {
                        var at = c.Attach(o.file, "live", null, o.cmd, Store.Actor);
                        if (o.exit == 0 && auto.TryGetValue(o.code, out var step)) c.SetCheck(step, true, Store.Actor, $"BASMA live: {o.cmd} · SHA-256 {at.Sha256[..16]}…", Store.Actor);
                    }
                    try { Directory.Delete(tmp, true); } catch { }
                    Toast(L.T("Captured") + $" · {outs.Count}");
                    Go("case", (c, "check"));
                }, () => run.IsEnabled = true);
            }, "primary", "play");
            var lives = c.Attachments.Where(x => x.Kind == "live").Reverse().ToList();
            var liveList = new StackPanel();
            foreach (var a in lives)
            {
                var full = Path.Combine(c.Folder, a.File);
                var ex = new Expander { Header = Ui.Tx($"{a.Caption}  ·  {a.Created}", 13, true, Ui.Ink, true), Margin = new Thickness(0, 0, 0, 6) };
                var inner = new StackPanel();
                inner.Children.Add(Ui.HashLine("SHA-256", a.Sha256));
                string preview = "";
                try { preview = File.ReadAllText(full, Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)); } catch { try { preview = File.ReadAllText(full); } catch { } }
                var pv = Ui.Sel(preview.Length > 6000 ? preview[..6000] + "\n…" : preview, 12); pv.TextWrapping = TextWrapping.NoWrap; pv.MaxHeight = 300;
                inner.Children.Add(Ui.Card2Box(new ScrollViewer { Content = pv, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 }, Ui.Ink == null ? null : new SolidColorBrush(Color.FromRgb(250, 248, 243)), 8));
                inner.Children.Add(Ui.Row(Ui.Btn(L.T("Show in folder"), () => Store.OpenInShell(full), "small", "folder")));
                ex.Content = inner;
                liveList.Children.Add(ex);
            }
            return Ui.Split(Ui.Card(L.T("Scene procedure checklist (Chapter 4)"), "01", list),
                new StackPanel { Children = { Ui.Card(L.T("Capture live state"), "02", Ui.Finding("warning", L.T("These commands run on THIS machine (the examiner machine or a machine you are authorised to examine). Each output is saved byte-for-byte, SHA-256 hashed and recorded in the case.")), boxes, Ui.Row(run), job, res),
                    Ui.Card(L.T("Live response outputs"), lives.Count.ToString(), liveList) } }, 1.25, 1);
        }

        FrameworkElement TabTimeline(CaseFile c)
        {
            var items = new List<(string utc, string text, string item, string type, string raw)>();
            items.Add((c.Info.Opened, L.T("Case opened") + " " + c.Info.CaseNo, null, "case", null));
            foreach (var e in c.Items)
            {
                foreach (var x in c.Custody.Where(x => x.EvidenceId == e.Id)) items.Add((x.EventUtc, $"{CustodyRules.ActionLabel(x.Action)}: {x.From} → {x.To} @ {x.Location}", e.ItemNo, "custody", null));
                foreach (var h in c.Hashes.Where(h => h.EvidenceId == e.Id)) items.Add((h.Finished, $"{L.T(h.Kind)} → {L.T(h.Result)} (SHA-256 {(h.Get("sha256") ?? "")[..Math.Min(16, (h.Get("sha256") ?? "").Length)]}…)", e.ItemNo, "hash", null));
                var acq = c.Hashes.FirstOrDefault(h => h.EvidenceId == e.Id && h.Kind == "acquisition" && h.ManifestFile != null);
                if (acq != null)
                {
                    try
                    {
                        foreach (var r in Engine.ReadManifest(Path.Combine(c.Folder, acq.ManifestFile)))
                            foreach (var (t, lab) in new[] { (r.Created, L.T("File created")), (r.Modified, L.T("File modified")) })
                            {
                                if (t == null) continue;
                                var cor = e.BiosOffset is int o && o != 0 ? CustodyRules.Corrected(t, o) : null;
                                items.Add((cor ?? t, $"{lab}: {r.Path}", e.ItemNo, "file", cor != null ? t : null));
                            }
                    }
                    catch { }
                }
            }
            foreach (var n in c.Notes) items.Add((n.Created, L.T("Note") + ": " + (n.Body.Length > 90 ? n.Body[..90] + "…" : n.Body), null, "note", null));
            foreach (var k in c.Checks.Values.Where(k => k.Done != null)) items.Add((k.Done, L.T("Scene step") + ": " + (L.Ar ? CustodyRules.Checklist.FirstOrDefault(x => x.code == k.Step).ar : CustodyRules.Checklist.FirstOrDefault(x => x.code == k.Step).en), null, "check", null));
            foreach (var a in c.Attachments) items.Add((a.Created, $"{L.T("Attachment")} ({L.T(a.Kind)}): {a.Caption ?? a.Name}", null, "attach", null));
            items = items.Where(x => x.utc != null).OrderBy(x => x.utc, StringComparer.Ordinal).ToList();
            var host = new ContentControl();
            var chips = new WrapPanel();
            void Show(string f)
            {
                var sel = items.Where(x => f == "" || x.type == f).ToList();
                host.Content = Ui.Table(new[] { L.T("Time (UTC)"), L.T("Item"), L.T("Event"), L.T("Raw time before drift correction") },
                    sel.Select(x => new GridRow(null, x.utc, x.item ?? "", x.text, x.raw ?? "")), new[] { 0, 3 }, null, new double[] { 170, 70, -1, 170 });
            }
            foreach (var (k, lab) in new[] { ("", L.T("All")), ("custody", L.T("Custody")), ("hash", L.T("Hash")), ("file", L.T("File times")), ("check", L.T("Procedures")), ("note", L.T("Notes")), ("attach", L.T("Attachments")) })
            {
                var rb = new RadioButton { Content = $"{lab} · {items.Count(x => k == "" || x.type == k)}", Style = (Style)Application.Current.Resources["Chip"], GroupName = "tl", IsChecked = k == "" };
                var kk = k; rb.Checked += (_, _) => Show(kk);
                chips.Children.Add(rb);
            }
            Show("");
            var drift = c.Items.Where(e => e.BiosOffset is int o && Math.Abs(o) >= 60).Select(e => $"{e.ItemNo}: {e.BiosOffset} s").ToList();
            return Ui.Card(L.T("Timeline"), items.Count.ToString(), drift.Count > 0 ? Ui.Finding("info", L.T("File times are corrected for BIOS clock drift") + ": " + string.Join(" · ", drift)) : null, chips, host);
        }

        FrameworkElement TabNotes(CaseFile c)
        {
            var body = Ui.Input("", false, true);
            var add = Ui.Btn(L.T("Add note"), () => { c.AddNote(body.Text, Store.Actor); Go("case", (c, "notes")); }, "primary", "plus");
            var notes = new StackPanel();
            foreach (var n in c.Notes.AsEnumerable().Reverse()) notes.Children.Add(Ui.Finding("info", $"#{n.Id} · {n.Created} · {n.Author}\n{n.Body}"));
            var (pu, pth) = PathPicker(L.T("File to attach (copied into the case and hashed)"), "", true, false);
            var kind = Ui.Combo(new[] { ("photo", L.T("photo")), ("document", L.T("document")), ("other", L.T("other")) });
            var evc = Ui.Combo(new[] { ("", L.T("— whole case —")) }.Concat(c.Items.Select(e => (e.Id.ToString(), e.ItemNo))));
            var cap = Ui.Input();
            var att = Ui.Btn(L.T("Attach"), () =>
            {
                try { c.Attach(pth.Text.Trim().Trim('"'), Ui.Val(kind), int.TryParse(Ui.Val(evc), out var id) ? id : null, cap.Text, Store.Actor); Toast(L.T("Saved")); Go("case", (c, "notes")); }
                catch (Exception e) { Toast(e.Message, true); }
            }, "primary", "plus");
            var list = Ui.Table(new[] { L.T("Time (UTC)"), L.T("Type"), L.T("Item"), L.T("Caption"), "SHA-256" },
                c.Attachments.Where(a => a.Kind != "live").Select(a => new GridRow(a, a.Created, L.T(a.Kind), c.Items.FirstOrDefault(e => e.Id == a.EvidenceId)?.ItemNo ?? "", a.Caption ?? a.Name, a.Sha256)),
                new[] { 0, 4 }, r => Store.OpenInShell(Path.Combine(c.Folder, ((Attachment)r.Tag).File)), new double[] { 170, 100, 70, -1, -2 });
            return Ui.Split(Ui.Card(L.T("Examiner notes"), c.Notes.Count.ToString(), Ui.Tx(L.T("What did you see? What did you do? Why? Notes cannot be edited or deleted."), 12.5, false, Ui.Muted), body, Ui.Row(add), notes),
                Ui.Card(L.T("Photos & attachments"), c.Attachments.Count(a => a.Kind != "live").ToString(), pu, Ui.Cols(3, Ui.Field(L.T("Type"), kind), Ui.Field(L.T("Item"), evc), Ui.Field(L.T("Caption"), cap)), Ui.Row(att), list), 1, 1.2);
        }

        FrameworkElement TabReport(CaseFile c)
        {
            var lang = Ui.Combo(new[] { ("ar", "العربية"), ("en", "English") }, Store.S.Lang);
            var res = new StackPanel();
            var gen = Ui.Btn(L.T("Generate report"), () =>
            {
                try
                {
                    var (html, fp) = Report.CaseReport(c, Ui.Val(lang));
                    var p = Report.Save(html, Path.Combine(c.Folder, "reports"), $"report_{Ui.Val(lang)}_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                    c.LogReport(p, "case-report", fp, Store.Actor);
                    Store.OpenInShell(p);
                    Go("case", (c, "rep"));
                }
                catch (Exception e) { Toast(e.Message, true); }
            }, "primary", "print");
            var reps = Ui.Table(new[] { L.T("Time (UTC)"), L.T("Kind"), L.T("Fingerprint"), L.T("File") }, c.Reports.AsEnumerable().Reverse().Select(r => new GridRow(r, r.ts, r.kind, r.fp, r.file)),
                new[] { 0, 2, 3 }, r => Store.OpenInShell(Path.Combine(c.Folder, (((string ts, string file, string kind, string fp))r.Tag).file)), new double[] { 170, 120, -2, -2 });
            var expRes = new StackPanel();
            var exp = Ui.Btn(L.T("Build court bundle"), () =>
            {
                try
                {
                    var outDir = Path.Combine(c.Folder, "export", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
                    Directory.CreateDirectory(outDir);
                    File.Copy(Path.Combine(c.Folder, "ledger.jsonl"), Path.Combine(outDir, "ledger_copy.jsonl"));
                    File.WriteAllText(Path.Combine(outDir, "case_state.json"), System.Text.Json.JsonSerializer.Serialize(new { c.Info, c.Items, c.Custody, c.Hashes, c.Notes, Checks = c.Checks.Values, c.Attachments, ledgerHead = c.Log.Head, ledgerOk = c.Log.Ok }, Js.Pretty));
                    var lines = Directory.EnumerateFiles(c.Folder, "*", SearchOption.AllDirectories)
                        .Where(f => !f.StartsWith(Path.Combine(c.Folder, "export") + Path.DirectorySeparatorChar) || f.StartsWith(outDir))
                        .Select(f => $"{Fmt.Hex(SHA256.HashData(File.ReadAllBytes(f)))}  {Path.GetRelativePath(c.Folder, f).Replace('\\', '/')}").OrderBy(l => l[66..], StringComparer.Ordinal).ToList();
                    var man = Path.Combine(outDir, "BUNDLE_MANIFEST.txt");
                    File.WriteAllText(man, $"# BASMA case bundle manifest\n# case: {c.Info.CaseNo}\n# created_utc: {Fmt.NowUtc()}\n# ledger_head: {c.Log.Head}\n# format: sha256  relative/path\n" + string.Join("\n", lines) + "\n", new UTF8Encoding(false));
                    var msha = Fmt.Hex(SHA256.HashData(File.ReadAllBytes(man)));
                    c.Log.Append("case.export", Store.Actor, new { folder = Path.GetRelativePath(c.Folder, outDir), manifest_sha256 = msha, files = lines.Count });
                    expRes.Children.Clear();
                    expRes.Children.Add(Ui.HashLine("MANIFEST", msha));
                    expRes.Children.Add(Ui.Row(Ui.Tx($"{lines.Count} {L.T("files")} · {outDir}", 12, false, Ui.Muted, true), Ui.Btn(L.T("Open folder"), () => Store.OpenInShell(outDir), "small", "folder")));
                }
                catch (Exception e) { Toast(e.Message, true); }
            }, "yellow", "box");
            var files = c.VerifyFiles();
            var cocAll = Ui.Btn(L.T("Chain of custody records — all exhibits"), () =>
            {
                try
                {
                    var (html, fp) = Report.CustodyRecord(c, c.Items, Ui.Val(lang), false);
                    var p = Report.Save(html, Path.Combine(c.Folder, "forms"), $"custody_records_all_{Ui.Val(lang)}_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                    c.LogReport(p, "custody-records", fp, Store.Actor);
                    Store.OpenInShell(p); Go("case", (c, "rep"));
                }
                catch (Exception e) { Toast(e.Message, true); }
            }, "primary", "print");
            var cocBlank = Ui.Btn(L.T("Blank custody form"), () =>
            {
                var (html, _) = Report.CustodyRecord(null, null, Ui.Val(lang), true);
                Store.OpenInShell(Report.Save(html, Path.Combine(c.Folder, "forms"), $"blank_custody_{Ui.Val(lang)}_{DateTime.UtcNow:yyyyMMddTHHmmssZ}"));
            }, "plain", "print");
            return Ui.Split(Ui.Card(L.T("Final report"), "01", Ui.Field(L.T("Report language"), lang), Ui.Row(gen, cocAll, cocBlank), res, Ui.Tx(L.T("Issued reports"), 14, true), reps),
                new StackPanel { Children = {
                    Ui.Card(L.T("Court bundle"), "02", Ui.Tx(L.T("Creates a folder with a copy of the ledger, the case state and BUNDLE_MANIFEST.txt holding the SHA-256 of every file in the case."), 12.5, false, Ui.Muted), Ui.Row(exp), expRes),
                    Ui.Card(L.T("Referenced files"), "03", files.Count == 0 ? Ui.Finding("info", L.T("Every manifest, attachment and report still matches its recorded hash.")) : Ui.Finding("critical", string.Join("\n", files)),
                        Ui.Row(Ui.Btn(L.T("Open case folder"), () => Store.OpenInShell(c.Folder), "plain", "folder"))) } }, 1.2, 1);
        }

        FrameworkElement TabLedger(CaseFile c)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Verdict(c.Log.Ok ? "ok" : "bad", c.Log.Ok ? L.T("INTACT") : L.T("BROKEN"),
                Ui.Tx($"{L.T("Entries")}: {c.Log.Entries.Count}" + (c.Log.Ok ? "" : $" · {L.T("broken at entry")} #{c.Log.BrokenAt} ({c.Log.Reason})"), 15, true),
                Ui.Tx(L.T("Head hash") + ":", 12, false, Ui.Muted), Ui.Sel(c.Log.Head, 12.5)));
            sp.Children.Add(Ui.Tx(L.T("Each entry's hash = SHA-256(previous hash ∥ entry). Editing or deleting any line of ledger.jsonl breaks the chain from that line on."), 12.5, false, Ui.Muted));
            var detail = new StackPanel();
            sp.Children.Add(Ui.Table(new[] { "#", L.T("Time (UTC)"), L.T("Actor"), L.T("Action"), "hash", "prev" },
                c.Log.Entries.AsEnumerable().Reverse().Select(e => new GridRow(e, e.seq.ToString(), e.ts, e.actor, e.type, e.hash, e.prev)), new[] { 0, 1, 3, 4, 5 },
                r =>
                {
                    var e = ((int seq, string ts, string actor, string type, System.Text.Json.JsonElement data, string prev, string hash, string body))r.Tag;
                    detail.Children.Clear();
                    var pv = Ui.Sel(System.Text.Json.JsonSerializer.Serialize(e.data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = Js.O.Encoder }), 12);
                    detail.Children.Add(Ui.Card($"#{e.seq} · {e.type}", "{}", pv));
                }, new double[] { 50, 170, 140, 140, -1, -1 }));
            sp.Children.Add(Ui.Tx(L.T("Double-click an entry to see its content."), 12, false, Ui.Muted));
            sp.Children.Add(detail);
            return Ui.Card(L.T("Case ledger"), "⛓", sp);
        }

        // ================================================================ EVIDENCE
        FrameworkElement PageEvidence((CaseFile c, int id) a)
        {
            var c = new CaseFile(a.c.Folder);
            arg = (c, a.id);
            var e = c.Items.FirstOrDefault(x => x.Id == a.id);
            if (e == null) return NewEvidence(c);
            var co = c.ContinuityOf(e);
            Head(c.Info.CaseNo + " · " + L.T("Exhibit"), e.Description ?? e.Kind ?? e.ItemNo, Ui.Btn(c.Info.CaseNo, () => Go("case", (c, "ev")), "plain", "back"));
            var left = new StackPanel(); var right = new StackPanel();
            // ---- hashing
            var hashes = c.Hashes.Where(h => h.EvidenceId == e.Id).ToList();
            var acq = hashes.FirstOrDefault(h => h.Kind == "acquisition");
            var (pu, pth) = PathPicker(L.T("Source to hash (file, folder, .001 or \\\\.\\PhysicalDriveN)"), e.SourcePath ?? "", true, true);
            var disks = SafeDisks();
            var fill = Ui.Combo(new[] { ("", L.T("— or choose a physical disk —")) }.Concat(disks.Select(d => (d.Path, d.Title))));
            fill.SelectionChanged += (_, _) => { if (Ui.Val(fill) is string v && v != "") pth.Text = v; };
            var split = Ui.Chk(L.T("Split image (.001, .002 …) hashed as one stream"), e.Split); split.IsEnabled = acq == null;
            var algoRow = new WrapPanel(); var algoBoxes = new Dictionary<string, CheckBox>();
            foreach (var al in Algos.All) { var cb = Ui.Chk(Algos.Label(al), Store.S.Algos.Contains(al) && Algos.Supported(al)); cb.FontFamily = Ui.Mono; cb.IsEnabled = acq == null && Algos.Supported(al); algoBoxes[al] = cb; algoRow.Children.Add(cb); }
            var job = new StackPanel();
            Button go = null;
            go = Ui.Btn(acq == null ? L.T("Acquisition hash") : L.T("Verify now"), () =>
            {
                string p = pth.Text.Trim().Trim('"');
                if (p == "") { Toast(L.T("Choose a source first."), true); return; }
                var algos = acq == null ? algoBoxes.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList() : acq.H.Keys.ToList();
                if (algos.Count == 0) { Toast(L.T("Choose at least one algorithm."), true); return; }
                if (acq == null && (p != e.SourcePath || split.IsChecked != e.Split)) { e.SourcePath = p; e.Split = split.IsChecked == true; c.UpdateEvidence(e, Store.Actor); }
                string kind = acq == null ? "acquisition" : "verification";
                long st = acq?.StartLba ?? 0, cnt = acq?.CountLba ?? -1;
                if (st < 0) st = 0;
                go.IsEnabled = false;
                RunJob(job, pr => HashAny(p, e.Split ? "split" : "auto", algos, pr, st, cnt), r =>
                {
                    var rec = c.AddHash(r, kind, e.Id, Store.Actor);
                    Store.AddHistory(r, kind, rec.Result, null);
                    Toast($"{L.T(kind)}: {L.T(rec.Result)}", rec.Result == "MISMATCH");
                    Go("ev", (c, e.Id));
                }, () => go.IsEnabled = true);
            }, "primary", acq == null ? "hash" : "shield");
            var recs = new StackPanel();
            foreach (var h in hashes.AsEnumerable().Reverse())
            {
                var hs = new StackPanel();
                var top = new DockPanel();
                var stp = Ui.Stamp(h.Result, 13); DockPanel.SetDock(stp, Dock.Right); top.Children.Add(stp);
                top.Children.Add(Ui.Tx($"#{h.Id} · {L.T(h.Kind)} · {Fmt.Ltr(h.Finished)} · {Fmt.Ltr(Fmt.Dur(h.Seconds))}" + (h.ComparedTo != null ? $" · vs #{h.ComparedTo}" : "") + $" · {h.Examiner}", 13, true));
                hs.Children.Add(top);
                foreach (var al in Algos.All.Where(h.H.ContainsKey)) hs.Children.Add(Ui.HashLine(h.TargetKind == "folder" && al == "sha256" ? "FOLDER" : Algos.Label(al), h.Get(al), h.Result == "MATCH" ? Ui.OkBg : h.Result == "MISMATCH" ? Ui.BadBg : null));
                hs.Children.Add(Ui.Tx($"{h.Target} · {L.T(h.TargetKind)} · {h.Size:N0} B" + (h.StartLba >= 0 ? $" · LBA {h.StartLba}+{h.CountLba}" : "") + (h.FileCount > 1 ? $" · {h.FileCount} {L.T("files")}" : ""), 11.5, false, Ui.Muted, true));
                if (h.BadSectors > 0) hs.Children.Add(Ui.Finding("warning", $"{h.BadSectors} {L.T("unreadable sectors were zero-filled")}"));
                if (h.Stats != null) hs.Children.Add(StatsTags(h.Stats));
                int nd = (h.Added?.Count ?? 0) + (h.Removed?.Count ?? 0) + (h.Modified?.Count ?? 0);
                if (nd > 0) hs.Children.Add(Ui.Finding("critical", $"+{h.Added?.Count} −{h.Removed?.Count} Δ{h.Modified?.Count}: " + string.Join(", ", (h.Modified ?? new()).Concat(h.Added ?? new()).Concat(h.Removed ?? new()).Take(15))));
                if (h.ManifestFile != null)
                {
                    var mf = Path.Combine(c.Folder, h.ManifestFile);
                    hs.Children.Add(Ui.Row(Ui.Btn(L.T("View manifest"), () => { try { var rows = Engine.ReadManifest(mf); recs.Children.Insert(0, Ui.Card(L.T("File manifest") + $" #{h.Id}", rows.Count.ToString(), ManifestView(rows))); } catch (Exception ex) { Toast(ex.Message, true); } }, "small", "eye"),
                        Ui.Btn(L.T("Show CSV"), () => Store.OpenInShell(mf), "small", "folder")));
                }
                recs.Children.Add(new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(2), Background = Ui.White, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = hs });
            }
            left.Children.Add(Ui.Card((acq == null ? L.T("Acquisition hash") : L.T("Verify now")) + " · " + L.T("Hash records"), "01", pu, fill, split, acq == null ? algoRow : Ui.Tx(L.T("Verification uses the same algorithms and range as the acquisition."), 12.5, false, Ui.Muted), Ui.Row(go), job, recs));
            // ---- custody
            var evs = c.Custody.Where(x => x.EvidenceId == e.Id).OrderBy(x => x.EventUtc, StringComparer.Ordinal).ThenBy(x => x.Id).ToList();
            var table = Ui.Table(new[] { "#", L.T("Time (UTC)"), L.T("Action"), L.T("From"), L.T("To"), L.T("Location"), L.T("Purpose"), L.T("Seal"), L.T("Signature") },
                evs.Select(x => new GridRow(x, x.Id.ToString(), x.EventUtc, CustodyRules.ActionLabel(x.Action) + (x.Corrects != null ? $" ↺#{x.Corrects}" : ""), x.From, x.To, x.Location, x.Purpose + (string.IsNullOrEmpty(x.Notes) ? "" : " — " + x.Notes),
                    (x.SealIntact == "yes" ? L.T("intact") : x.SealIntact == "no" ? L.T("BROKEN") : "—") + " " + x.SealNo, x.Signature + (string.IsNullOrEmpty(x.Witness) ? "" : $" ({L.T("witness")}: {x.Witness})"))),
                new[] { 0, 1 }, null, new double[] { 44, 170, 150, 160, 160, 170, 240, 140, 190 });
            var act = Ui.Combo(CustodyRules.Actions.Select(k => (k.Key, L.Ar ? k.Value.ar : k.Value.en)), co.Holder == null ? "seized" : "transferred");
            var from = Ui.Input(co.Holder ?? ""); var to = Ui.Input(); var loc = Ui.Input(co.Location ?? ""); var pur = Ui.Input();
            var (whenUi, when) = DateField(L.T("Date & time"), null);
            var seal = Ui.Combo(new[] { ("yes", L.T("Seal intact")), ("no", L.T("Seal BROKEN")), ("", L.T("Not applicable")) });
            var sealNo = Ui.Input(e.SealNo ?? "", true); var wit = Ui.Input(); var notes = Ui.Input(); var sig = Ui.Input(Store.S.Examiner);
            var corr = Ui.Combo(evs.Select(x => (x.Id.ToString(), "#" + x.Id)));
            var corrF = Ui.Field(L.T("Corrects entry #"), corr); corrF.Visibility = Visibility.Collapsed;
            act.SelectionChanged += (_, _) => corrF.Visibility = Ui.Val(act) == "correction" ? Visibility.Visible : Visibility.Collapsed;
            var confirm = Ui.Chk(L.T("I confirm this entry is correct; it cannot be edited or deleted once saved"));
            var add = Ui.Btn(L.T("Add custody entry"), () =>
            {
                if (string.IsNullOrWhiteSpace(sig.Text) || confirm.IsChecked != true) { Toast(L.T("Type the signature and tick the confirmation."), true); return; }
                try
                {
                    c.AddCustody(new CustodyEntry { EvidenceId = e.Id, Action = Ui.Val(act), From = from.Text, To = to.Text, EventUtc = when(), Location = loc.Text, Purpose = pur.Text, SealIntact = Ui.Val(seal), SealNo = sealNo.Text, Witness = wit.Text, Notes = notes.Text, Signature = sig.Text,
                        Corrects = Ui.Val(act) == "correction" && int.TryParse(Ui.Val(corr), out var ci) ? ci : null }, Store.Actor);
                    Toast(L.T("Saved")); Go("ev", (c, e.Id));
                }
                catch (Exception ex) { Toast(ex.Message, true); }
            }, "primary", "plus");
            var custodyCard = (Ui.Card(L.T("Chain of custody"), evs.Count.ToString(), table,
                new Border { Height = 14 },
                Ui.Tx(L.T("New entry"), 15, true),
                Ui.Cols(3, Ui.Field(L.T("Action"), act), Ui.Field(L.T("From (releasing)"), from), Ui.Field(L.T("To (receiving)"), to), whenUi, Ui.Field(L.T("Location"), loc), Ui.Field(L.T("Purpose"), pur),
                    Ui.Field(L.T("Seal"), seal), Ui.Field(L.T("Seal / bag no."), sealNo), Ui.Field(L.T("Witness"), wit)),
                corrF, Ui.Field(L.T("Notes"), notes), Ui.Field(L.T("Signature (full name)"), sig), confirm, Ui.Row(add)));
            custodyCard.Margin = new Thickness(0, 4, 6, 22);
            // ---- right column
            var vstat = co.Status == "INTACT" ? "ok" : co.Status == "BROKEN" ? "bad" : "warn";
            var fs = new List<UIElement> { Ui.Verdict(vstat, Ui.StatusOf(co.Status).text, Ui.Tx($"{L.T("Holder")}: {co.Holder ?? "—"}", 14, true), Ui.Tx($"{L.T("Location")}: {co.Location ?? "—"}", 13)) };
            fs.AddRange(co.Findings.Select(f => (UIElement)Ui.Finding(f.Level, f.Text)));
            right.Children.Add(Ui.Card(L.T("Continuity analysis"), "A", fs.ToArray()));
            FrameworkElement FormBtn(string label, string type, string lang) => Ui.Btn(Fmt.Ltr(label), () =>
            {
                var (html, fp) = type == "custody" ? Report.CustodyTag(c, e, lang) : type == "record" ? Report.CustodyRecord(c, new List<Evidence> { e }, lang, false) : Report.EvidenceForm(c, e, lang);
                var p = Report.Save(html, Path.Combine(c.Folder, "forms"), $"{type}_{e.ItemNo}_{lang}_{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                c.LogReport(p, type + "-form", fp, Store.Actor);
                Store.OpenInShell(p);
            }, "plain", "print");
            var rec1 = FormBtn("سجل سلسلة الحيازة الكامل (AR)", "record", "ar"); var rec2 = FormBtn("Complete Chain of Custody Record (EN)", "record", "en");
            ((Button)rec1).Background = ((Button)rec2).Background = Ui.Orange;
            right.Children.Add(Ui.Card(L.T("Printable forms"), "B", rec1, rec2, Ui.Tx(L.T("ISO/IEC 27037 · NIST SP 800-86 · RFC 3227 · ACPO — with signature lines"), 11.5, false, Ui.Muted), new Border { Height = 8 },
                FormBtn("نموذج الدليل المفرد (AR)", "evidence", "ar"), FormBtn("Single Evidence Form (EN)", "evidence", "en"),
                FormBtn("بطاقة سلسلة الحيازة (AR)", "custody", "ar"), FormBtn("Chain of Custody Tag (EN)", "custody", "en")));
            var kv = new Grid();
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); kv.ColumnDefinitions.Add(new ColumnDefinition());
            var rowsKv = new (string k, string v)[] { (L.T("Item no."), e.ItemNo), (L.T("Media type"), e.Kind), (L.T("Make / model"), $"{e.Make} {e.Model}"), (L.T("Serial"), e.Serial), (L.T("Capacity"), e.Capacity),
                (L.T("Seized from"), e.SeizedFrom), (L.T("Seizure location"), e.SeizedLocation), (L.T("Seized by"), e.SeizedBy), (L.T("Seized at"), e.SeizedUtc), (L.T("Seal / bag no."), e.SealNo),
                (L.T("Condition"), e.Condition), (L.T("BIOS time"), e.BiosTime), (L.T("True time"), e.TrueTime), (L.T("Clock drift"), e.BiosOffset == null ? null : $"{e.BiosOffset:+0;-0} s"), (L.T("Status"), L.T(e.Status)) };
            for (int i = 0; i < rowsKv.Length; i++)
            {
                kv.RowDefinitions.Add(new RowDefinition());
                var k = Ui.Tx(rowsKv[i].k, 12.5, true); var v = Ui.Sel(rowsKv[i].v ?? "—", 12.5, false);
                k.Margin = v.Margin = new Thickness(0, 0, 0, 5);
                Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                kv.Children.Add(k); kv.Children.Add(v);
            }
            var editHost = new StackPanel();
            right.Children.Add(Ui.Card(L.T("Item details"), "C", kv, Ui.Row(Ui.Btn(L.T("Edit"), () => { editHost.Children.Clear(); editHost.Children.Add(EvidenceForm(c, e)); }, "plain")), editHost));
            var ph = c.Attachments.Where(x => x.EvidenceId == e.Id).ToList();
            var thumbs = new WrapPanel();
            foreach (var p in ph)
            {
                var full = Path.Combine(c.Folder, p.File);
                FrameworkElement img;
                try { img = p.Kind == "photo" ? new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(full)), Width = 150, Height = 100, Stretch = Stretch.UniformToFill } : Ui.Tx(Path.GetExtension(p.Name).ToUpperInvariant(), 16, true); }
                catch { img = Ui.Tx(p.Name, 12); }
                var b = new Button { Content = new StackPanel { Children = { img, Ui.Tx(p.Caption ?? p.Name, 11.5) } }, Width = 170, Padding = new Thickness(6) };
                b.Click += (_, _) => Store.OpenInShell(full);
                thumbs.Children.Add(b);
            }
            right.Children.Add(Ui.Card(L.T("Photos & attachments"), ph.Count.ToString(), thumbs, Ui.Row(Ui.Btn(L.T("Add in the case tab"), () => Go("case", (c, "notes")), "small"))));
            var sp = new StackPanel();
            var hd = new DockPanel();
            var stmp = Ui.Stamp(co.Status, 18); DockPanel.SetDock(stmp, Dock.Right); hd.Children.Add(stmp);
            var ht = new StackPanel(); ht.Children.Add(Ui.Tx(L.T("Item no."), 12, true, Ui.Muted)); ht.Children.Add(Ui.Tx(e.ItemNo, 54, true, Ui.Ink, true, false));
            ht.Children.Add(Ui.Tx($"{e.Kind} · {e.Capacity} · {e.Make} {e.Model} · S/N {e.Serial}", 13, false, Ui.Muted));
            hd.Children.Add(ht);
            sp.Children.Add(hd); sp.Children.Add(new Border { Height = 18 });
            sp.Children.Add(Ui.Split(left, right, 1.45, 1));
            sp.Children.Add(custodyCard);
            return sp;
        }

        FrameworkElement NewEvidence(CaseFile c)
        {
            Head(c.Info.CaseNo + " · " + L.T("Exhibit"), L.T("New exhibit"), Ui.Btn(c.Info.CaseNo, () => Go("case", (c, "ev")), "plain", "back"));
            return EvidenceForm(c, null);
        }

        FrameworkElement EvidenceForm(CaseFile c, Evidence e)
        {
            bool isNew = e == null;
            e ??= new Evidence { SeizedBy = Store.S.Examiner };
            var no = Ui.Input(e.ItemNo ?? "", true); var kind = new ComboBox { IsEditable = true, Text = e.Kind };
            foreach (var k in new[] { "Magnetic HDD", "SSD", "NVMe SSD", "USB flash drive", "Memory card", "Mobile phone", "Laptop", "Desktop PC", "Optical disc CD/DVD/Blu-ray", "Tape DAT/DLT", "RAID set", "Forensic image E01/dd", "RAM dump", "Cloud export", "Document/other" }) kind.Items.Add(L.T(k));
            var cap = Ui.Input(e.Capacity); var make = Ui.Input(e.Make); var model = Ui.Input(e.Model); var serial = Ui.Input(e.Serial, true); var desc = Ui.Input(e.Description); var cond = Ui.Input(e.Condition);
            var from = Ui.Input(e.SeizedFrom); var loc = Ui.Input(e.SeizedLocation); var seal = Ui.Input(e.SealNo, true); var by = Ui.Input(e.SeizedBy);
            var (atUi, at) = DateField(L.T("Seized at"), e.SeizedUtc);
            var (biosUi, bios) = DateField(L.T("BIOS time read"), e.BiosTime, false);
            var (trueUi, truth) = DateField(L.T("True time at reading"), e.TrueTime, false);
            var (srcUi, src) = PathPicker(L.T("Hash source (file, folder, .001 or \\\\.\\PhysicalDriveN)"), e.SourcePath ?? "", true, true);
            var disks = SafeDisks();
            var fill = Ui.Combo(new[] { ("", L.T("— or choose a physical disk —")) }.Concat(disks.Select(d => (d.Path, d.Title))));
            fill.SelectionChanged += (_, _) => { if (Ui.Val(fill) is string v && v != "") { src.Text = v; var d = disks.First(x => x.Path == v); if (make.Text == "") make.Text = d.Vendor; if (model.Text == "") model.Text = d.Product; if (serial.Text == "") serial.Text = d.Serial; if (cap.Text == "") cap.Text = Fmt.Bytes(d.Size); } };
            var split = Ui.Chk(L.T("Split image (.001, .002 …) hashed as one stream"), e.Split);
            var seizure = Ui.Chk(L.T("Create the first 'Seized' custody entry"), true); var sig = Ui.Input(Store.S.Examiner); var wit = Ui.Input();
            var save = Ui.Btn(isNew ? L.T("Register exhibit") : L.T("Save"), () =>
            {
                try
                {
                    var n = new Evidence
                    {
                        Id = e.Id, ItemNo = no.Text.Trim(), Kind = kind.Text, Capacity = cap.Text, Make = make.Text, Model = model.Text, Serial = serial.Text, Description = desc.Text, Condition = cond.Text,
                        SeizedFrom = from.Text, SeizedLocation = loc.Text, SealNo = seal.Text, SeizedBy = by.Text, SeizedUtc = at(), BiosTime = bios(), TrueTime = truth(), SourcePath = src.Text.Trim().Trim('"'),
                        Split = split.IsChecked == true, Status = e.Status ?? "in custody"
                    };
                    if (isNew)
                    {
                        CustodyEntry first = seizure.IsChecked == true && !string.IsNullOrWhiteSpace(n.SeizedBy)
                            ? new CustodyEntry { Action = "seized", From = n.SeizedFrom, To = n.SeizedBy, EventUtc = n.SeizedUtc ?? Fmt.NowUtc(), Location = n.SeizedLocation, SealIntact = string.IsNullOrEmpty(n.SealNo) ? "" : "yes", SealNo = n.SealNo, Signature = sig.Text, Witness = wit.Text }
                            : null;
                        var r = c.AddEvidence(n, Store.Actor, first);
                        Toast(L.T("Saved")); Go("ev", (c, r.Id));
                    }
                    else { if (string.IsNullOrWhiteSpace(n.ItemNo)) n.ItemNo = e.ItemNo; c.UpdateEvidence(n, Store.Actor); Toast(L.T("Saved")); Go("ev", (c, e.Id)); }
                }
                catch (Exception ex) { Toast(ex.Message, true); }
            }, "primary");
            var sp = new StackPanel();
            sp.Children.Add(Ui.Card(L.T("Identification"), "01", Ui.Cols(3, Ui.Field(L.T("Item no."), no, L.T("empty = automatic (E001…)")), Ui.Field(L.T("Media type"), kind), Ui.Field(L.T("Capacity"), cap),
                Ui.Field(L.T("Make"), make), Ui.Field(L.T("Model"), model), Ui.Field(L.T("Serial number"), serial)), Ui.Field(L.T("Description"), desc), Ui.Field(L.T("Physical condition, damage, scratches"), cond)));
            sp.Children.Add(Ui.Card(L.T("Seizure"), "02", Ui.Cols(3, Ui.Field(L.T("Seized from (owner/holder)"), from), Ui.Field(L.T("Seizure location"), loc), Ui.Field(L.T("Seal / bag no."), seal), Ui.Field(L.T("Seized by"), by), atUi),
                isNew ? (UIElement)new StackPanel { Children = { seizure, Ui.Cols(2, Ui.Field(L.T("Officer signature (type name)"), sig), Ui.Field(L.T("Witness"), wit)) } } : null));
            sp.Children.Add(Ui.Card(L.T("BIOS clock (slide 15)"), "03", Ui.Cols(2, biosUi, trueUi), Ui.Tx(L.T("The drift is computed automatically and used to correct file times in the timeline."), 12.5, false, Ui.Muted)));
            sp.Children.Add(Ui.Card(L.T("Hash source"), "04", fill, srcUi, split, Ui.Row(save)));
            return sp;
        }
    }
}
