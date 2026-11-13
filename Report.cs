using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Basma
{
    /// <summary>Printable HTML documents. Fingerprint = SHA-256 of the UTF-8 HTML with the fingerprint value replaced by 64 zeros
    /// (same scheme as ISNAD, so either tool can verify the other's documents).</summary>
    public static class Report
    {
        const string Token = "@@FINGERPRINT@@";
        static readonly string Zero = new('0', 64);
        static string E(object s) => WebUtility.HtmlEncode(s?.ToString() ?? "");
        static string T(string en, string ar, string lang) => lang == "ar" ? ar : en;

        const string Css = @"@page{size:A4;margin:14mm 12mm 16mm}*{box-sizing:border-box}
body{margin:0;font-family:'Segoe UI',Tahoma,Arial,sans-serif;color:#111;background:#efeadf;font-size:12.5px;line-height:1.5}
.sheet{max-width:210mm;margin:16px auto;background:#fff;padding:14mm 12mm;box-shadow:0 0 0 1px #111}
.mono,.h{font-family:Consolas,'Cascadia Mono','Courier New',monospace}.h{font-size:10.5px;word-break:break-all;direction:ltr;unicode-bidi:embed;display:inline-block}
.top{display:flex;justify-content:space-between;align-items:flex-end;border-bottom:4px solid #111;padding-bottom:8px;margin-bottom:6px}
.brand{font-weight:800;font-size:13px}.brand b{background:#111;color:#ffd400;padding:2px 8px;margin-inline-end:6px;letter-spacing:.18em}
.tape{height:10px;background:repeating-linear-gradient(135deg,#ffd400 0 14px,#111 14px 28px);margin:8px 0 14px}
h1{font-size:22px;margin:4px 0 0}.caseno{font-family:Consolas,monospace;font-size:34px;font-weight:700;direction:ltr;display:inline-block}
h2{font-size:15px;margin:22px 0 8px;padding:4px 10px;background:#111;color:#fff;display:flex;gap:10px;align-items:center}
h2 .n{background:#ff4d00;color:#111;padding:0 8px;font-family:Consolas,monospace}h3{font-size:13.5px;margin:16px 0 6px;border-bottom:2px solid #111;padding-bottom:3px}
table{width:100%;border-collapse:collapse;margin:4px 0 8px}th,td{border:1px solid #111;padding:4px 6px;vertical-align:top;text-align:start}
th{background:#f3efe6;font-weight:700}tr{break-inside:avoid}.kv th{width:30%}
.stamp{display:inline-block;border:3px solid currentColor;padding:2px 10px;font-weight:800;transform:rotate(-2deg)}
.ok{color:#0a7a2f}.bad{color:#c1121f}.warn{color:#b35c00}.info{color:#334}
.f{border-inline-start:5px solid #999;padding:4px 8px;margin:4px 0;background:#faf8f3}.f.critical{border-color:#c1121f}.f.warning{border-color:#ff8a00}.f.info{border-color:#4a6fa5}
.sig{display:grid;grid-template-columns:1fr 1fr;gap:24px;margin-top:20px}.sig div{border-top:2px solid #111;padding-top:4px;min-height:60px}
.fp{margin-top:20px;border:2px dashed #111;padding:8px;font-size:11px}.small{font-size:11px;color:#333}
.box{display:inline-block;min-width:16px;height:16px;border:2px solid #111;text-align:center;line-height:12px;font-weight:800}
.btn{position:fixed;top:12px;inset-inline-end:12px;background:#ff4d00;color:#111;border:2px solid #111;font-weight:800;padding:8px 14px;cursor:pointer}
@media print{body{background:#fff}.sheet{box-shadow:none;margin:0;padding:0;max-width:none}.btn{display:none}}";

        static string Doc(string lang, string title, string body) =>
            $"<!doctype html><html lang=\"{lang}\" dir=\"{(lang == "ar" ? "rtl" : "ltr")}\"><head><meta charset=\"utf-8\"><title>{E(title)}</title><style>{Css}</style></head><body>" +
            $"<button class=\"btn\" onclick=\"window.print()\">{T("Print / Save PDF", "طباعة / حفظ PDF", lang)}</button><div class=\"sheet\">{body}</div></body></html>";

        static string Footer(string lang) =>
            $"<div class=\"fp\"><b>{T("Document fingerprint SHA-256", "بصمة هذا المستند SHA-256", lang)}:</b> <span class=\"h\">{Token}</span><br>" +
            $"<span class=\"small\">{T("Computed over this HTML file with the fingerprint value replaced by 64 zeros.", "تُحسب على ملف HTML بعد استبدال قيمة البصمة بـ 64 صفراً.", lang)} — BASMA v{App.Version} · {T("Dr. Mohammed Tawfik", "د. محمد توفيق", lang)}</span></div>";

        public static (string html, string fp) Finalize(string doc)
        {
            string fp = Fmt.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(doc.Replace(Token, Zero))));
            return (doc.Replace(Token, fp), fp);
        }

        public static (bool ok, string claimed, string actual, string reason) Verify(string path)
        {
            var doc = File.ReadAllText(path, new UTF8Encoding(false));
            int i = doc.IndexOf("class=\"fp\"", StringComparison.Ordinal);
            if (i < 0) return (false, null, null, "no fingerprint");
            var m = Regex.Match(doc.Substring(i), "<span class=\"h\">([0-9a-f]{64})</span>");
            if (!m.Success) return (false, null, null, "no fingerprint");
            string claimed = m.Groups[1].Value;
            string actual = Fmt.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(doc.Replace(claimed, Zero))));
            return (actual == claimed, claimed, actual, null);
        }

        public static string Save(string html, string folder, string stem)
        {
            Directory.CreateDirectory(folder);
            var p = Path.Combine(folder, stem + ".html");
            File.WriteAllText(p, html, new UTF8Encoding(false));
            return p;
        }

        static string Badge(string st, string lang)
        {
            string cls = st switch { "INTACT" or "MATCH" => "ok", "BROKEN" or "MISMATCH" => "bad", "WARNINGS" => "warn", _ => "info" };
            string lab = lang == "ar" ? st switch { "INTACT" => "سليمة", "BROKEN" => "مكسورة", "WARNINGS" => "تحذيرات", "MATCH" => "مطابق", "MISMATCH" => "غير مطابق", "BASELINE" => "أساس", _ => st } : st;
            return $"<span class=\"stamp {cls}\">{E(lab)}</span>";
        }

        static string Kv(IEnumerable<(string k, string v)> rows) => "<table class=\"kv\">" + string.Concat(rows.Select(r => $"<tr><th>{r.k}</th><td>{r.v}</td></tr>")) + "</table>";

        static string HashCells(IDictionary<string, string> h, string kind) =>
            string.Concat(Algos.All.Where(a => h != null && h.ContainsKey(a)).Select(a => $"<div><b>{(kind == "folder" && a == "sha256" ? "FOLDER-SHA256" : Algos.Label(a))}</b> <span class=\"h\">{E(h[a])}</span></div>"));

        static string Top(string title, string lang) =>
            $"<div class=\"top\"><div class=\"brand\"><b>BASMA</b>{E(title)}</div><div class=\"small\">{T("Generated (UTC)", "تاريخ الإصدار (UTC)", lang)}: <span class=\"mono\">{Fmt.NowUtc()}</span></div></div><div class=\"tape\"></div>";

        // ------------------------------------------------------------------ standalone hash / compare report
        public static (string html, string fp) HashReport(HashResult r, string examiner, string lang, string expected = null, string title = null)
        {
            var b = new StringBuilder();
            b.Append(Top(title ?? T("Hash report", "تقرير هاش", lang), lang));
            b.Append($"<h1 class=\"mono\" style=\"direction:ltr;text-align:start\">{E(r.Target)}</h1>");
            var rows = new List<(string, string)>
            {
                (T("Source type", "نوع المصدر", lang), E(r.TargetKind)),
                (T("Size", "الحجم", lang), $"<span class=\"mono\">{r.Size:N0} bytes ({Fmt.Bytes(r.Size)})</span>"),
                (T("Files", "الملفات", lang), r.FileCount.ToString()),
                (T("Started / finished (UTC)", "البداية / النهاية (UTC)", lang), $"<span class=\"mono\">{E(r.Started)} → {E(r.Finished)} · {r.Seconds:0.###} s</span>"),
                (T("Examiner", "المحقق", lang), E(examiner)),
                (T("Workstation", "جهاز الفحص", lang), E(Environment.MachineName + " · " + Environment.OSVersion)),
            };
            if (r.StartLba >= 0) rows.Add((T("Sector range", "نطاق القطاعات", lang), $"<span class=\"mono\">LBA {r.StartLba:N0} + {r.CountLba:N0} × {r.SectorSize} B</span>"));
            if (r.Parts != null) rows.Add((T("Segments (one stream)", "الأجزاء (تيار واحد)", lang), E(string.Join(", ", r.Parts))));
            if (r.Sig != null) rows.Add((T("File signature", "توقيع الملف", lang), E($"{r.Sig.Detected ?? "?"} · .{r.Sig.Ext} · {r.Sig.Status} · entropy {r.Entropy}")));
            rows.Add((T("Unreadable sectors", "قطاعات غير مقروءة", lang), r.BadSectors.Count == 0 ? T("none", "لا يوجد", lang) : $"<span class=\"bad\">{r.BadSectors.Count}: {E(string.Join(", ", r.BadSectors.Take(50)))}</span>"));
            b.Append(Kv(rows));
            b.Append($"<h2><span class=\"n\">#</span>{T("Hash values", "قيم الهاش", lang)}</h2>").Append(HashCells(r.H, r.TargetKind));
            if (!string.IsNullOrWhiteSpace(expected))
            {
                bool ok = r.H.Values.Contains(expected.Trim().ToLowerInvariant());
                b.Append($"<p>{T("Expected", "المتوقع", lang)}: <span class=\"h\">{E(expected.Trim())}</span> {Badge(ok ? "MATCH" : "MISMATCH", lang)}</p>");
            }
            if (r.Note == "e01-container") b.Append($"<div class=\"f info\">{T("This is an E01 container: its file hash is not the source-disk hash.", "هذا ملف E01 (حاوية): هاش الملف ليس هاش القرص الأصلي.", lang)}</div>");
            if (r.Rows != null)
            {
                b.Append($"<h2><span class=\"n\">≡</span>{T("Manifest", "بيان الملفات", lang)} · {r.Rows.Count}</h2><p class=\"small\">{T("Folder digest = SHA-256 over sorted 'path⇥size⇥SHA-256' lines.", "هاش المجلد = SHA-256 فوق الأسطر المرتبة «المسار⇥الحجم⇥SHA-256».", lang)}</p>");
                if (r.Stats != null) b.Append("<p class=\"small\">" + E(string.Join(" · ", r.Stats.Select(kv => kv.Key + " " + kv.Value))) + "</p>");
                b.Append("<table><tr><th>path</th><th>size</th><th>SHA-256</th><th>signature</th><th>entropy</th><th>known</th></tr>");
                foreach (var x in r.Rows.Take(3000))
                    b.Append($"<tr><td class=\"h\">{E(x.Path)}</td><td class=\"mono\">{x.Size}</td><td class=\"h\">{E(x.Get("sha256"))}</td><td class=\"{(x.SigStatus == "mismatch" ? "warn" : "")}\">{E(x.SigDetected)} {E(x.SigStatus)}</td><td class=\"{(x.HighEntropy ? "bad" : "")}\">{x.Entropy}</td><td class=\"{(x.Known?.Category == "bad" ? "bad" : "")}\">{E(x.Known?.Label)}</td></tr>");
                b.Append("</table>");
            }
            b.Append(Footer(lang));
            return Finalize(Doc(lang, title ?? "Hash report", b.ToString()));
        }

        public static (string html, string fp) CompareReport(string title, IEnumerable<(string k, string v)> summary, string verdict, IEnumerable<string> details, string examiner, string lang)
        {
            var b = new StringBuilder();
            b.Append(Top(title, lang));
            b.Append($"<p>{Badge(verdict, lang)}</p>");
            b.Append(Kv(summary.Select(s => (E(s.k), $"<span class=\"h\">{E(s.v)}</span>")).Append((T("Examiner", "المحقق", lang), E(examiner)))));
            var det = details.Take(5000).ToList();
            if (det.Count > 0) b.Append("<table>" + string.Concat(det.Select(d => $"<tr><td class=\"h\">{E(d)}</td></tr>")) + "</table>");
            b.Append(Footer(lang));
            return Finalize(Doc(lang, title, b.ToString()));
        }

        // ------------------------------------------------------------------ case documents
        public static (string html, string fp) CaseReport(CaseFile c, string lang)
        {
            var b = new StringBuilder();
            int n = 0;
            string Sec(string en, string ar) => $"<h2><span class=\"n\">{++n:D2}</span>{T(en, ar, lang)}</h2>";
            var info = c.Info;
            b.Append(Top(T("Chain of Custody & Forensic Verification Report", "تقرير سلسلة الحيازة والتحقق الجنائي", lang), lang));
            b.Append($"<div class=\"small\">{T("Case", "القضية", lang)}</div><div class=\"caseno\">{E(info.CaseNo)}</div><h1>{E(info.Title)}</h1>");
            b.Append(Sec("Case details", "بيانات القضية"));
            string status = lang == "ar" ? info.Status switch { "open" => "مفتوحة", "closed" => "مغلقة", "archived" => "مؤرشفة", _ => info.Status } : info.Status;
            b.Append(Kv(new[] { (T("Case number", "رقم القضية", lang), $"<span class=\"mono\">{E(info.CaseNo)}</span>"), (T("Title", "العنوان", lang), E(info.Title)), (T("Type", "النوع", lang), E(info.Type)),
                (T("Agency", "الجهة", lang), E(info.Agency)), (T("Lead examiner", "المحقق الرئيس", lang), E(info.Lead)), (T("Legal authority", "السلطة القانونية", lang), E(info.Authority)),
                (T("Reference", "مرجعها", lang), E(info.AuthorityRef)), (T("Opened", "تاريخ الفتح", lang), $"<span class=\"mono\">{E(info.Opened)}</span>"), (T("Status", "الحالة", lang), E(status)),
                (T("Description", "الوصف", lang), E(info.Description)) }));
            b.Append(Sec("Integrity summary", "ملخص النزاهة"));
            var files = c.VerifyFiles();
            b.Append(Kv(new[] {
                (T("Hash-chained ledger", "سجل النزاهة المتسلسل", lang), Badge(c.Log.Ok ? "INTACT" : "BROKEN", lang) + (c.Log.Ok ? "" : $" #{c.Log.BrokenAt} {E(c.Log.Reason)}")),
                (T("Entries", "عدد القيود", lang), $"<span class=\"mono\">{c.Log.Entries.Count}</span>"),
                (T("Chain head hash", "هاش رأس السلسلة", lang), $"<span class=\"h\">{c.Log.Head}</span>"),
                (T("Referenced files (manifests, attachments, reports)", "الملفات المرجعية (البيانات والمرفقات والتقارير)", lang), files.Count == 0 ? $"<span class=\"ok\">{T("all match their recorded hash", "كلها تطابق هاشها المسجل", lang)}</span>" : $"<span class=\"bad\">{E(string.Join(" · ", files))}</span>") }));
            b.Append(Sec("Evidence register", "سجل المضبوطات"));
            b.Append($"<table><tr><th>{T("Item", "الدليل", lang)}</th><th>{T("Media type", "نوع الوسيط", lang)}</th><th>{T("Description", "الوصف", lang)}</th><th>{T("Serial", "الرقم التسلسلي", lang)}</th><th>{T("Seized by", "ضبطه", lang)}</th><th>{T("Seized at", "وقت الضبط", lang)}</th><th>{T("Current holder", "الحائز الحالي", lang)}</th><th>{T("Continuity", "الاستمرارية", lang)}</th></tr>");
            foreach (var e in c.Items)
            {
                var co = c.ContinuityOf(e);
                b.Append($"<tr><td class=\"mono\"><b>{E(e.ItemNo)}</b></td><td>{E(e.Kind)}</td><td>{E(e.Description)}</td><td class=\"mono\">{E(e.Serial)}</td><td>{E(e.SeizedBy)}</td><td class=\"mono\">{E(e.SeizedUtc)}</td><td>{E(co.Holder)}<br><span class=\"small\">{E(co.Location)}</span></td><td>{Badge(co.Status, lang)}</td></tr>");
            }
            b.Append("</table>");
            b.Append(Sec("Item details", "تفاصيل كل دليل"));
            foreach (var e in c.Items)
            {
                var co = c.ContinuityOf(e);
                b.Append($"<h3><span class=\"mono\">{E(e.ItemNo)}</span> — {E(e.Description ?? e.Kind)}</h3>");
                b.Append(Kv(new[] { (T("Media type", "نوع الوسيط", lang), E(e.Kind)), (T("Make / model", "المصنّع / الطراز", lang), E($"{e.Make} {e.Model}")), (T("Serial", "الرقم التسلسلي", lang), $"<span class=\"mono\">{E(e.Serial)}</span>"),
                    (T("Capacity", "السعة", lang), E(e.Capacity)), (T("Condition / damage", "الحالة المادية والأضرار", lang), E(e.Condition)), (T("Seized from", "ضُبط من", lang), E(e.SeizedFrom)),
                    (T("Seizure location", "مكان الضبط", lang), E(e.SeizedLocation)), (T("Seal / bag no.", "رقم الختم/الكيس", lang), E(e.SealNo)),
                    (T("BIOS / true time / offset", "وقت BIOS / الحقيقي / الانحراف", lang), e.BiosTime == null ? "—" : $"<span class=\"mono\">{E(e.BiosTime)} / {E(e.TrueTime)} / {e.BiosOffset} s</span>"),
                    (T("Hash source", "مصدر الهاش", lang), $"<span class=\"h\">{E(e.SourcePath)}</span>") }));
                var hs = c.Hashes.Where(h => h.EvidenceId == e.Id).ToList();
                b.Append($"<b>{T("Hash records", "سجل الهاش", lang)}</b><table><tr><th>#</th><th>UTC</th><th>{T("Result", "النتيجة", lang)}</th><th>Hash</th></tr>");
                foreach (var h in hs)
                {
                    string diff = (h.Added?.Count ?? 0) + (h.Removed?.Count ?? 0) + (h.Modified?.Count ?? 0) > 0
                        ? $"<div class=\"f critical\">+{h.Added?.Count} / −{h.Removed?.Count} / Δ{h.Modified?.Count}: {E(string.Join(", ", (h.Modified ?? new()).Concat(h.Added ?? new()).Concat(h.Removed ?? new()).Take(20)))}</div>" : "";
                    string bad = h.BadSectors > 0 ? $"<div class=\"f warning\">{h.BadSectors} {T("unreadable sectors zero-filled", "قطاعات غير مقروءة استُبدلت بأصفار", lang)}</div>" : "";
                    b.Append($"<tr><td>#{h.Id} {E(h.Kind)}<br><span class=\"small\">{E(h.TargetKind)} · {E(h.Examiner)}</span></td><td class=\"mono\">{E(h.Finished)}</td><td>{Badge(h.Result, lang)}</td><td>{HashCells(h.H, h.TargetKind)}{(h.ManifestSha256 != null ? $"<div><b>MANIFEST</b> <span class=\"h\">{h.ManifestSha256}</span></div>" : "")}<div class=\"h small\">{E(h.Target)} · {h.Size:N0} B</div>{diff}{bad}</td></tr>");
                }
                b.Append("</table>");
                b.Append($"<b>{T("Chain of custody", "سلسلة الحيازة", lang)}</b>").Append(CustodyTable(c.Custody.Where(x => x.EvidenceId == e.Id), lang));
                b.Append($"<b>{T("Continuity findings", "نتائج تحليل الاستمرارية", lang)}</b> {Badge(co.Status, lang)}");
                foreach (var f in co.Findings) b.Append($"<div class=\"f {f.Level}\"><b>{f.Level.ToUpperInvariant()}</b> — {E(lang == "ar" ? f.Ar : f.En)}</div>");
            }
            b.Append(Sec("Scene procedure checklist", "قائمة إجراءات المسرح"));
            b.Append("<table>");
            foreach (var s in CustodyRules.Checklist)
            {
                c.Checks.TryGetValue(s.code, out var st);
                bool ok = st?.Done != null;
                b.Append($"<tr><td><span class=\"box\">{(ok ? "✓" : "")}</span></td><td>{E(lang == "ar" ? s.ar : s.en)}</td><td class=\"mono\">{E(ok ? st.Done : "")}</td><td>{E(ok ? st.By : "")}</td><td>{E(st?.Note)}</td></tr>");
            }
            b.Append("</table>");
            b.Append(Sec("Examiner notes", "ملاحظات المحقق"));
            foreach (var nn in c.Notes) b.Append($"<div class=\"f info\"><span class=\"mono small\">{E(nn.Created)} · {E(nn.Author)}</span><br>{E(nn.Body)}</div>");
            b.Append(Sec("Methodology and tools", "المنهجية والأدوات"));
            b.Append("<p>" + T("Hashes were computed by sequential read-only access with all selected algorithms in a single pass; split images are hashed as one stream; physical disks are read through \\\\.\\PhysicalDriveN with unreadable sectors zero-filled and logged; folder digest = SHA-256 over sorted 'path⇥size⇥SHA-256' lines. All times are UTC. Every action is an entry in an append-only ledger in which each entry's hash = SHA-256(previous hash ∥ entry); custody and hash entries are never edited — corrections are new entries.",
                "حُسبت قيم الهاش بقراءة متتابعة للقراءة فقط، وبكل الخوارزميات المختارة في مرور واحد. الصور المقسّمة تُحسب كتيار واحد، والأقراص الفعلية تُقرأ عبر \\\\.\\PhysicalDriveN، والقطاعات غير المقروءة تُستبدل بأصفار وتُسجَّل. هاش المجلد = SHA-256 فوق الأسطر المرتبة «المسار⇥الحجم⇥SHA-256». كل الأزمنة بالتوقيت العالمي UTC. وكل إجراء قيد في سجل لا يُحذف منه شيء، هاش كل قيد = SHA-256(هاش السابق ∥ القيد)، وقيود الحيازة والهاش لا تُعدّل؛ التصحيح يكون بقيد جديد.", lang) + "</p>");
            b.Append(Sec("Certification and signatures", "الإقرار والتوقيع"));
            b.Append("<p>" + T("I certify that the evidence described in this report was collected, preserved, transported and examined according to the procedures documented above, and that every transfer of custody is recorded with its date, time and parties.",
                "أقر بأن الأدلة الموصوفة في هذا التقرير جُمعت وحُفظت ونُقلت وفُحصت وفق الإجراءات الموثقة أعلاه، وأن كل انتقال للحيازة مسجل بتاريخه ووقته وأطرافه.", lang) + "</p>");
            b.Append($"<div class=\"sig\"><div>{T("Examiner", "المحقق", lang)}: {E(info.Lead)}<br>{T("Signature", "التوقيع", lang)}:</div><div>{T("Reviewer", "المراجع", lang)}:<br>{T("Signature", "التوقيع", lang)}:</div></div>");
            b.Append(Footer(lang));
            return Finalize(Doc(lang, info.CaseNo, b.ToString()));
        }

        static string CustodyTable(IEnumerable<CustodyEntry> evs, string lang)
        {
            var b = new StringBuilder($"<table><tr><th>#</th><th>UTC</th><th>{T("Action", "الإجراء", lang)}</th><th>{T("From", "من", lang)}</th><th>{T("To", "إلى", lang)}</th><th>{T("Location", "المكان", lang)}</th><th>{T("Purpose", "الغرض", lang)}</th><th>{T("Seal", "الختم", lang)}</th><th>{T("Signature", "التوقيع", lang)}</th></tr>");
            foreach (var x in evs.OrderBy(x => Fmt.ParseUtc(x.EventUtc) ?? DateTime.MaxValue).ThenBy(x => x.Id))
            {
                string seal = x.SealIntact == "yes" ? T("intact", "سليم", lang) : x.SealIntact == "no" ? T("BROKEN", "مكسور", lang) : "—";
                var a = CustodyRules.Actions.TryGetValue(x.Action, out var ac) ? (lang == "ar" ? ac.ar : ac.en) : x.Action;
                b.Append($"<tr><td class=\"mono\">{x.Id}</td><td class=\"mono\">{E(x.EventUtc)}</td><td>{E(a)}{(x.Corrects != null ? $" ↺#{x.Corrects}" : "")}</td><td>{E(x.From)}</td><td>{E(x.To)}</td><td>{E(x.Location)}</td><td>{E(x.Purpose)} <span class=\"small\">{E(x.Notes)}</span></td><td class=\"{(x.SealIntact == "no" ? "bad" : "")}\">{seal} {E(x.SealNo)}</td><td>{E(x.Signature)}{(string.IsNullOrEmpty(x.Witness) ? "" : $"<br><span class=\"small\">{T("Witness", "الشاهد", lang)}: {E(x.Witness)}</span>")}</td></tr>");
            }
            return b.Append("</table>").ToString();
        }

        public static (string html, string fp) EvidenceForm(CaseFile c, Evidence e, string lang)
        {
            var acq = c.Hashes.FirstOrDefault(h => h.EvidenceId == e.Id && h.Kind == "acquisition");
            var stored = c.Custody.Where(x => x.EvidenceId == e.Id).FirstOrDefault(x => CustodyRules.Actions.TryGetValue(x.Action, out var a) && a.kind == "storage");
            bool photos = c.Attachments.Any(a => a.EvidenceId == e.Id && a.Kind == "photo");
            string Cell(string l, string v, bool mono = false) => $"<th>{l}</th><td class=\"{(mono ? "mono" : "")}\">{(string.IsNullOrEmpty(v) ? "&nbsp;" : E(v))}</td>";
            var b = new StringBuilder(Top(T("Single Evidence Form", "نموذج الدليل المفرد", lang), lang));
            b.Append($"<table><tr>{Cell(T("Case number", "رقم القضية", lang), c.Info.CaseNo, true)}{Cell(T("Item", "الدليل", lang), e.ItemNo, true)}</tr></table>");
            b.Append($"<h3>{T("Section B: Evidence collection", "القسم ب: جمع الدليل", lang)}</h3><table><tr>{Cell(T("Date/time collected", "تاريخ/وقت الجمع", lang), e.SeizedUtc, true)}{Cell(T("Collected by", "جمعه", lang), e.SeizedBy)}</tr><tr>{Cell(T("Site address", "عنوان الموقع", lang), e.SeizedLocation)}{Cell(T("Seized from", "ضُبط من", lang), e.SeizedFrom)}</tr></table>");
            b.Append($"<h3>{T("Section C: Evidence details", "القسم ج: تفاصيل الدليل", lang)}</h3><table><tr>{Cell(T("Date/time stored", "تاريخ/وقت الإيداع", lang), stored?.EventUtc, true)}{Cell(T("Storage location", "مكان التخزين", lang), stored?.Location)}</tr>" +
                     $"<tr>{Cell(T("Device type", "نوع الوسيط", lang), e.Kind)}{Cell(T("Capacity", "السعة", lang), e.Capacity)}</tr><tr>{Cell(T("Manufacturer", "المصنّع", lang), e.Make)}{Cell(T("Model", "الطراز", lang), e.Model)}</tr>" +
                     $"<tr>{Cell(T("Serial no.", "الرقم التسلسلي", lang), e.Serial, true)}{Cell(T("Seal / bag no.", "رقم الختم/الكيس", lang), e.SealNo)}</tr>");
            foreach (var a in new[] { "md5", "sha1", "sha256" }) b.Append($"<tr><th>{Algos.Label(a)}</th><td colspan=\"3\"><span class=\"h\">{E(acq?.Get(a))}</span></td></tr>");
            b.Append($"<tr><th>{T("Condition / damage", "الحالة المادية والأضرار", lang)}</th><td colspan=\"3\">{E(e.Condition)}</td></tr><tr><th>{T("Digital image taken", "صور رقمية مأخوذة", lang)}</th><td colspan=\"3\"><span class=\"box\">{(photos ? "✓" : "")}</span> {T("Yes", "نعم", lang)} &nbsp; <span class=\"box\">{(photos ? "" : "✓")}</span> {T("No", "لا", lang)}</td></tr></table>");
            b.Append($"<h3>{T("Section D: Image details", "القسم د: تفاصيل الصورة الجنائية", lang)}</h3><table><tr>{Cell(T("Date/time imaged", "تاريخ/وقت التصوير", lang), acq?.Finished, true)}{Cell(T("Imaged by", "صوّره", lang), acq?.Examiner)}</tr>" +
                     $"<tr><th>{T("Source / image path", "المصدر/مسار الصورة", lang)}</th><td colspan=\"3\"><span class=\"h\">{E(acq?.Target)}</span></td></tr><tr>{Cell(T("Size (bytes)", "الحجم (بايت)", lang), acq?.Size.ToString("N0"), true)}{Cell(T("Unreadable sectors", "قطاعات غير مقروءة", lang), acq?.BadSectors.ToString(), true)}</tr></table>");
            b.Append($"<div class=\"sig\"><div>{T("Collected by", "جمعه", lang)}: {E(e.SeizedBy)}<br>{T("Signature", "التوقيع", lang)}:</div><div>{T("Witness", "الشاهد", lang)}:<br>{T("Signature", "التوقيع", lang)}:</div></div>");
            b.Append(Footer(lang));
            return Finalize(Doc(lang, e.ItemNo, b.ToString()));
        }

        // ------------------------------------------------------------------ complete chain-of-custody record
        const string CocCss = @"
.coc h2{margin-top:16px;break-after:avoid}.coc table{font-size:11.5px}.coc th{white-space:normal}
.cb{display:inline-block;width:13px;height:13px;border:1.6px solid #111;margin-inline-end:5px;vertical-align:-2px;text-align:center;line-height:10px;font-size:11px;font-weight:900}
.cb.on{background:#111;color:#ffd400}.opt{display:inline-block;margin-inline-end:16px;white-space:nowrap}
.line{display:inline-block;min-width:150px;border-bottom:1.4px solid #111;height:15px;vertical-align:bottom}
.sigcell{min-height:34px}.sigcell .typed{font-size:10.5px;color:#333}.wet{border-bottom:1px dotted #555;height:20px;margin-top:3px}
thead{display:table-header-group}.pb{break-before:page}
.law{font-size:11px;column-count:2;column-gap:18px}.law p{margin:0 0 5px}
.formid{font-family:Consolas,monospace;font-size:11px;border:2px solid #111;padding:1px 6px}
@page{@bottom-right{content:counter(page) ' / ' counter(pages);font-size:9px}}";

        static string Cb(bool on, string label) => $"<span class=\"opt\"><span class=\"cb{(on ? " on" : "")}\">{(on ? "✓" : "")}</span>{E(label)}</span>";
        static string Ln(string v, bool blank) => blank || string.IsNullOrWhiteSpace(v) ? "<span class=\"line\"></span>" : E(v);

        public static (string html, string fp) CustodyRecord(CaseFile c, IList<Evidence> items, string lang, bool blank)
        {
            var b = new StringBuilder($"<style>{CocCss}</style><div class=\"coc\">");
            if (blank || items == null || items.Count == 0) items = new List<Evidence> { new Evidence() };
            for (int ix = 0; ix < items.Count; ix++)
            {
                var e = items[ix];
                var info = blank ? new CaseInfo() : c.Info;
                var cu = blank ? new List<CustodyEntry>() : c.Custody.Where(x => x.EvidenceId == e.Id).OrderBy(x => Fmt.ParseUtc(x.EventUtc) ?? DateTime.MaxValue).ThenBy(x => x.Id).ToList();
                var hs = blank ? new List<HashRecord>() : c.Hashes.Where(h => h.EvidenceId == e.Id).ToList();
                var acq = hs.FirstOrDefault(h => h.Kind == "acquisition");
                bool Ck(string step) => !blank && c.Checks.TryGetValue(step, out var s) && s.Done != null;
                string CkInfo(string step) => !blank && c.Checks.TryGetValue(step, out var s) && s.Done != null ? $"{s.Done} · {s.By}{(string.IsNullOrEmpty(s.Note) ? "" : " · " + s.Note)}" : "";
                var first = cu.FirstOrDefault();
                int photos = blank ? 0 : c.Attachments.Count(a => a.EvidenceId == e.Id && a.Kind == "photo");
                int lives = blank ? 0 : c.Attachments.Count(a => a.Kind == "live");
                int n = 0;
                string Sec(string en, string ar) => $"<h2><span class=\"n\">{(char)('A' + n++)}</span>{T(en, ar, lang)}</h2>";

                if (ix > 0) b.Append("<div class=\"pb\"></div>");
                b.Append($"<div class=\"top\"><div class=\"brand\"><b>BASMA</b>{T("Chain of Custody Record — Digital Evidence", "سجل سلسلة حيازة الدليل الرقمي", lang)}</div><div><span class=\"formid\">BASMA-COC-02</span> <span class=\"small\">{T("Generated (UTC)", "تاريخ الإصدار (UTC)", lang)}: <span class=\"mono\">{(blank ? "" : Fmt.NowUtc())}</span></span></div></div><div class=\"tape\"></div>");
                b.Append($"<table><tr><th>{T("Case number", "رقم القضية", lang)}</th><td class=\"mono\" style=\"font-size:16px;font-weight:700\">{Ln(info.CaseNo, blank)}</td><th>{T("Item no.", "رقم الدليل", lang)}</th><td class=\"mono\" style=\"font-size:16px;font-weight:700\">{Ln(e.ItemNo, blank)}</td><th>{T("Seal / bag no.", "رقم الختم/الكيس", lang)}</th><td class=\"mono\">{Ln(e.SealNo, blank)}</td></tr></table>");
                b.Append($"<p class=\"small\">{T("Basis: ISO/IEC 27037 (identification, collection, acquisition, preservation), NIST SP 800-86, RFC 3227 (order of volatility), ACPO principles; course chapters 1, 3 and 4 (single evidence form and custody tag).", "الأساس: ISO/IEC 27037 (التعرّف، الجمع، الاستحواذ، الحفظ)، وNIST SP 800-86، وRFC 3227 (ترتيب التطاير)، ومبادئ ACPO؛ والفصول 1 و3 و4 من المقرر (نموذج الدليل المفرد وبطاقة الحيازة).", lang)}</p>");

                b.Append(Sec("Case and legal authority", "القضية والسلطة القانونية"));
                b.Append($"<table class=\"kv\"><tr><th>{T("Title", "العنوان", lang)}</th><td>{Ln(info.Title, blank)}</td></tr><tr><th>{T("Agency / unit", "الجهة / الوحدة", lang)}</th><td>{Ln(info.Agency, blank)}</td></tr><tr><th>{T("Lead examiner", "المحقق الرئيس", lang)}</th><td>{Ln(info.Lead, blank)}</td></tr>" +
                         $"<tr><th>{T("Legal authority", "السلطة القانونية", lang)}</th><td>{Cb(!blank && (info.Authority ?? "").Length > 0, T("warrant / court order", "مذكرة / أمر قضائي", lang))}{Cb(false, T("written consent", "موافقة كتابية", lang))}{Cb(false, T("organisational policy", "سياسة المؤسسة", lang))}<br>{Ln(info.Authority, blank)} · {T("ref.", "المرجع", lang)} {Ln(info.AuthorityRef, blank)}</td></tr>" +
                         $"<tr><th>{T("Scope of authority", "نطاق التفويض", lang)}</th><td><span class=\"line\" style=\"min-width:95%\"></span></td></tr><tr><th>{T("Case opened (UTC)", "فتح القضية (UTC)", lang)}</th><td class=\"mono\">{Ln(info.Opened, blank)}</td></tr></table>");

                b.Append(Sec("Exhibit identification", "تعريف الدليل"));
                b.Append($"<table class=\"kv\"><tr><th>{T("Media type", "نوع الوسيط", lang)}</th><td>{Ln(e.Kind, blank)}</td></tr><tr><th>{T("Make / model", "المصنّع / الطراز", lang)}</th><td>{Ln(blank ? null : $"{e.Make} {e.Model}".Trim(), blank)}</td></tr>" +
                         $"<tr><th>{T("Serial number", "الرقم التسلسلي", lang)}</th><td class=\"mono\">{Ln(e.Serial, blank)}</td></tr><tr><th>{T("Capacity", "السعة", lang)}</th><td>{Ln(e.Capacity, blank)}</td></tr>" +
                         $"<tr><th>{T("Description", "الوصف", lang)}</th><td>{Ln(e.Description, blank)}</td></tr><tr><th>{T("Condition, damage, scratches", "الحالة والأضرار والخدوش", lang)}</th><td>{Ln(e.Condition, blank)}</td></tr>" +
                         $"<tr><th>{T("Owner / seized from", "المالك / ضُبط من", lang)}</th><td>{Ln(e.SeizedFrom, blank)}</td></tr><tr><th>{T("Photographs", "الصور", lang)}</th><td>{Cb(photos > 0 || Ck("photo_scene"), T("taken", "التُقطت", lang))} {(photos > 0 ? photos + " " + T("attached", "مرفقة", lang) : "")}</td></tr></table>");

                b.Append(Sec("Seizure / collection", "الضبط والجمع"));
                b.Append($"<table class=\"kv\"><tr><th>{T("Date & time (UTC)", "التاريخ والوقت (UTC)", lang)}</th><td class=\"mono\">{Ln(e.SeizedUtc, blank)} {(blank || e.SeizedUtc == null ? "" : "· " + T("local", "محلي", lang) + " " + Fmt.LocalOf(e.SeizedUtc))}</td></tr>" +
                         $"<tr><th>{T("Location / address", "المكان / العنوان", lang)}</th><td>{Ln(e.SeizedLocation, blank)}</td></tr><tr><th>{T("Seized by (name)", "ضبطه (الاسم)", lang)}</th><td>{Ln(e.SeizedBy, blank)} &nbsp; {T("signature", "التوقيع", lang)} <span class=\"line\"></span></td></tr>" +
                         $"<tr><th>{T("Witness", "الشاهد", lang)}</th><td>{Ln(first?.Witness, blank)} &nbsp; {T("signature", "التوقيع", lang)} <span class=\"line\"></span></td></tr>" +
                         $"<tr><th>{T("System state found", "حالة النظام عند الوصول", lang)}</th><td>{Cb(false, T("powered on", "يعمل", lang))}{Cb(false, T("powered off", "مطفأ", lang))}{Cb(false, T("sleep / hibernate", "سكون / سبات", lang))}{Cb(false, T("not applicable", "لا ينطبق", lang))}</td></tr>" +
                         $"<tr><th>{T("Before power-off (chapter 4)", "قبل الإطفاء (الفصل 4)", lang)}</th><td>{Cb(Ck("photo_scene"), T("screen & scene photographed", "تصوير الشاشة والمسرح", lang))}{Cb(Ck("encryption"), T("encryption checked", "فحص التشفير", lang))}{Cb(Ck("memory"), T("RAM captured", "التقاط الذاكرة", lang))}{Cb(Ck("processes") || Ck("netstat"), T("processes & connections recorded", "توثيق العمليات والاتصالات", lang))}{Cb(Ck("sessions") || Ck("openfiles"), T("sessions & open files recorded", "توثيق الجلسات والملفات المفتوحة", lang))}{(lives > 0 ? $"<span class=\"small\">({lives} {T("live outputs hashed", "مخرجات حية مختومة بالهاش", lang)})</span>" : "")}</td></tr>" +
                         $"<tr><th>{T("Power-off method", "طريقة الإطفاء", lang)}</th><td>{Cb(Ck("power"), T("power cord / battery removed", "سحب القابس / نزع البطارية", lang))}{Cb(false, T("normal shutdown", "إطفاء عادي", lang))}{Cb(false, T("left running (live acquisition)", "تُرك يعمل (استحواذ حي)", lang))} <span class=\"small\">{E(CkInfo("power"))}</span></td></tr>" +
                         $"<tr><th>{T("BIOS clock (slide 15)", "ساعة BIOS (الشريحة 15)", lang)}</th><td class=\"mono\">BIOS {Ln(e.BiosTime, blank)} · {T("true", "الحقيقي", lang)} {Ln(e.TrueTime, blank)} · {T("drift", "الانحراف", lang)} {(blank || e.BiosOffset == null ? "<span class=\"line\" style=\"min-width:60px\"></span>" : $"{e.BiosOffset:+0;-0} s")}</td></tr>" +
                         $"<tr><th>{T("Other media / drives", "وسائط وأقراص أخرى", lang)}</th><td>{Cb(Ck("media"), T("removable media ejected, one form each", "إخراج الوسائط القابلة للإزالة، نموذج لكل منها", lang))}{Cb(Ck("drives"), T("all drives removed, even unconnected", "إزالة كل الأقراص حتى غير الموصولة", lang))}{Cb(Ck("label"), T("cables labelled, all angles photographed", "ترقيم الأسلاك وتصوير كل الزوايا", lang))}</td></tr></table>");

                b.Append(Sec("Order of volatility followed (RFC 3227)", "ترتيب التطاير المتبع (RFC 3227)"));
                b.Append($"<table><thead><tr><th>#</th><th>{T("Data", "البيانات", lang)}</th><th>{T("Collected", "جُمعت", lang)}</th><th>{T("When / by / note", "متى / بواسطة / ملاحظة", lang)}</th></tr></thead>");
                var vol = new (string en, string ar, string step)[]
                {
                    ("Registers, cache", "المسجلات والذاكرة المخبئية", null), ("Memory (RAM), routing/ARP/process tables", "الذاكرة RAM وجداول التوجيه وARP والعمليات", "memory"),
                    ("Network connections and sessions", "الاتصالات والجلسات الشبكية", "netstat"), ("Running processes, open files", "العمليات الجارية والملفات المفتوحة", "processes"),
                    ("Disk / storage media (acquisition)", "القرص ووسائط التخزين (الاستحواذ)", "hash_acq"), ("Remote logging and monitoring data", "السجلات والمراقبة البعيدة", null),
                    ("Physical configuration, topology", "التهيئة المادية وطوبولوجيا الشبكة", "label"), ("Archival media (backups, tapes)", "وسائط الأرشفة (النسخ الاحتياطية والأشرطة)", null),
                };
                for (int i = 0; i < vol.Length; i++)
                {
                    bool done = vol[i].step != null && Ck(vol[i].step);
                    string inf = vol[i].step != null ? CkInfo(vol[i].step) : "";
                    if (vol[i].step == "hash_acq" && acq != null) { done = true; if (inf == "") inf = $"{acq.Finished} · {acq.Examiner} · {Algos.Label("sha256")} {(acq.Get("sha256") ?? "")[..Math.Min(16, (acq.Get("sha256") ?? "").Length)]}…"; }
                    b.Append($"<tr><td class=\"mono\">{i + 1}</td><td>{E(lang == "ar" ? vol[i].ar : vol[i].en)}</td><td>{Cb(done, "")}</td><td class=\"small\">{E(inf)}</td></tr>");
                }
                b.Append("</table>");

                b.Append(Sec("Packaging, sealing and transport", "التحريز والختم والنقل"));
                b.Append($"<table class=\"kv\"><tr><th>{T("Packaging", "التغليف", lang)}</th><td>{Cb(Ck("package"), T("anti-static bag", "كيس مضاد للكهرباء الساكنة", lang))}{Cb(false, T("Faraday bag", "كيس فاراداي", lang))}{Cb(false, T("evidence box", "صندوق أدلة", lang))}{Cb(!blank && !string.IsNullOrEmpty(e.SealNo), T("tamper-evident seal", "ختم كاشف للعبث", lang))}</td></tr>" +
                         $"<tr><th>{T("Seal number(s)", "رقم/أرقام الختم", lang)}</th><td class=\"mono\">{Ln(blank ? null : string.Join(", ", cu.Select(x => x.SealNo).Where(x => !string.IsNullOrEmpty(x)).Distinct().DefaultIfEmpty(e.SealNo)), blank)}</td></tr>" +
                         $"<tr><th>{T("Transport", "النقل", lang)}</th><td>{Cb(Ck("transport"), T("locked vehicle, direct to lab, no stops", "مركبة مقفلة، مباشرة إلى المختبر دون توقف", lang))}{Cb(false, T("protected from heat, shock and magnets", "محمي من الحرارة والصدمات والمغناطيس", lang))}</td></tr></table>");

                b.Append(Sec("Acquisition and verification", "الاستحواذ والتحقق"));
                b.Append($"<table class=\"kv\"><tr><th>{T("Write protection", "الحماية من الكتابة", lang)}</th><td>{Cb(Ck("writeblock"), T("hardware write blocker", "حاجب كتابة عتادي", lang))}{Cb(false, T("software write protection", "حماية برمجية", lang))}{Cb(false, T("read-only media", "وسيط للقراءة فقط", lang))} {T("model", "الطراز", lang)} <span class=\"line\" style=\"min-width:110px\"></span></td></tr>" +
                         $"<tr><th>{T("Tool / version", "الأداة / الإصدار", lang)}</th><td>{(acq != null ? $"BASMA v{App.Version}" : "<span class=\"line\"></span>")}</td></tr>" +
                         $"<tr><th>{T("Method", "الطريقة", lang)}</th><td>{Cb(acq?.TargetKind == "disk", T("physical (whole disk)", "مادي (القرص كله)", lang))}{Cb(acq?.TargetKind == "volume", T("volume", "قسم", lang))}{Cb(acq?.TargetKind == "folder", T("logical (files)", "منطقي (ملفات)", lang))}{Cb(acq?.TargetKind == "split-image" || acq?.TargetKind == "file", T("existing image file", "ملف صورة موجود", lang))}</td></tr>" +
                         $"<tr><th>{T("Source", "المصدر", lang)}</th><td><span class=\"h\">{(acq != null ? E(acq.Target) : "<span class=\"line\" style=\"min-width:90%\"></span>")}</span>{(acq != null && acq.StartLba >= 0 ? $" · LBA {acq.StartLba}+{acq.CountLba}" : "")}</td></tr>" +
                         $"<tr><th>{T("Acquired (UTC) / by", "وقت الاستحواذ (UTC) / بواسطة", lang)}</th><td class=\"mono\">{Ln(acq?.Finished, blank || acq == null)} · {Ln(acq?.Examiner, blank || acq == null)}</td></tr>" +
                         $"<tr><th>{T("Size / unreadable sectors", "الحجم / قطاعات غير مقروءة", lang)}</th><td class=\"mono\">{(acq != null ? $"{acq.Size:N0} B · {acq.BadSectors}" : "<span class=\"line\"></span>")}</td></tr>");
                foreach (var a in new[] { "md5", "sha1", "sha256", "sha512" })
                    if (acq?.Get(a) != null || (a != "sha512" && acq == null))
                        b.Append($"<tr><th>{(acq?.TargetKind == "folder" && a == "sha256" ? "FOLDER-SHA256" : Algos.Label(a))}</th><td><span class=\"h\">{(acq?.Get(a) != null ? E(acq.Get(a)) : "<span class=\"line\" style=\"min-width:95%\"></span>")}</span></td></tr>");
                b.Append("</table>");
                b.Append($"<table><thead><tr><th>{T("Verification", "التحقق", lang)} #</th><th>UTC</th><th>{T("By", "بواسطة", lang)}</th><th>{T("Result", "النتيجة", lang)}</th><th>SHA-256</th></tr></thead>");
                var vers = hs.Where(h => h.Kind == "verification").ToList();
                foreach (var v in vers) b.Append($"<tr><td class=\"mono\">#{v.Id}</td><td class=\"mono\">{E(v.Finished)}</td><td>{E(v.Examiner)}</td><td>{Badge(v.Result, lang)}</td><td class=\"h\">{E(v.Get("sha256"))}</td></tr>");
                for (int i = vers.Count; i < 3; i++) b.Append("<tr><td>&nbsp;</td><td></td><td></td><td>" + Cb(false, T("match", "مطابق", lang)) + Cb(false, T("mismatch", "غير مطابق", lang)) + "</td><td></td></tr>");
                b.Append("</table>");

                b.Append(Sec("Chain of custody log", "سجل انتقال الحيازة"));
                b.Append($"<table><thead><tr><th>#</th><th>{T("Date & time (UTC)", "التاريخ والوقت (UTC)", lang)}</th><th>{T("Action", "الإجراء", lang)}</th><th>{T("Released by — name & signature", "المسلِّم — الاسم والتوقيع", lang)}</th><th>{T("Received by — name & signature", "المستلم — الاسم والتوقيع", lang)}</th><th>{T("Purpose / reason", "الغرض / السبب", lang)}</th><th>{T("Location / locker", "المكان / الخزانة", lang)}</th><th>{T("Seal intact · no.", "الختم سليم · رقمه", lang)}</th></tr></thead>");
                foreach (var x in cu)
                {
                    var act = CustodyRules.Actions.TryGetValue(x.Action ?? "", out var ac) ? (lang == "ar" ? ac.ar : ac.en) : x.Action;
                    string rel = $"<div class=\"sigcell\">{E(x.From)}<div class=\"wet\"></div></div>";
                    string rec = $"<div class=\"sigcell\">{E(x.To)}{(string.IsNullOrEmpty(x.Signature) ? "" : $"<div class=\"typed\">/s/ {E(x.Signature)}</div>")}<div class=\"wet\"></div>{(string.IsNullOrEmpty(x.Witness) ? "" : $"<div class=\"typed\">{T("witness", "الشاهد", lang)}: {E(x.Witness)}</div>")}</div>";
                    string seal = x.SealIntact == "yes" ? Cb(true, T("yes", "نعم", lang)) : x.SealIntact == "no" ? $"<b class=\"bad\">{T("NO", "لا", lang)}</b>" : "—";
                    b.Append($"<tr><td class=\"mono\">{x.Id}</td><td class=\"mono\">{E(x.EventUtc)}</td><td>{E(act)}{(x.Corrects != null ? $" ↺#{x.Corrects}" : "")}</td><td>{rel}</td><td>{rec}</td><td>{E(x.Purpose)} <span class=\"small\">{E(x.Notes)}</span></td><td>{E(x.Location)}</td><td>{seal} <span class=\"mono\">{E(x.SealNo)}</span></td></tr>");
                }
                for (int i = cu.Count; i < Math.Max(cu.Count + 6, blank ? 16 : 12); i++)
                    b.Append($"<tr><td class=\"mono\">{i + 1}</td><td><div class=\"sigcell\"></div></td><td></td><td><div class=\"sigcell\"><div class=\"wet\"></div></div></td><td><div class=\"sigcell\"><div class=\"wet\"></div></div></td><td></td><td></td><td>{Cb(false, T("yes", "نعم", lang))}{Cb(false, T("no", "لا", lang))}</td></tr>");
                b.Append("</table>");

                b.Append(Sec("Continuity verification", "التحقق من الاستمرارية"));
                if (blank) b.Append($"<p>{Cb(false, T("unbroken: every release matches the previous receipt, no unexplained gaps, seals intact, hashes match", "متصلة: كل تسليم يطابق الاستلام السابق، ولا فجوات غير مبررة، والأختام سليمة، والهاش مطابق", lang))}</p><p>{T("Reviewed by", "راجعه", lang)} <span class=\"line\"></span> {T("date", "التاريخ", lang)} <span class=\"line\"></span></p>");
                else
                {
                    var co = c.ContinuityOf(e);
                    b.Append($"<p>{Badge(co.Status, lang)} &nbsp; {T("current holder", "الحائز الحالي", lang)}: <b>{E(co.Holder)}</b> · {E(co.Location)}</p>");
                    foreach (var f in co.Findings) b.Append($"<div class=\"f {f.Level}\"><b>{f.Level.ToUpperInvariant()}</b> — {E(lang == "ar" ? f.Ar : f.En)}</div>");
                }

                b.Append(Sec("Final disposition", "التصرف النهائي"));
                var end = cu.LastOrDefault(x => x.Action == "released" || x.Action == "destroyed");
                b.Append($"<table class=\"kv\"><tr><th>{T("Disposition", "التصرف", lang)}</th><td>{Cb(end?.Action == "released", T("returned to owner", "أُعيد لصاحبه", lang))}{Cb(end?.Action == "destroyed", T("destroyed", "أُتلف", lang))}{Cb(false, T("retained as evidence", "محتفظ به كدليل", lang))}{Cb(false, T("transferred to another agency", "نُقل إلى جهة أخرى", lang))}</td></tr>" +
                         $"<tr><th>{T("Date (UTC) / recipient", "التاريخ (UTC) / المستلم", lang)}</th><td>{Ln(end?.EventUtc, end == null)} · {Ln(end?.To, end == null)}</td></tr><tr><th>{T("Authorised by", "بتفويض من", lang)}</th><td><span class=\"line\"></span> {T("signature", "التوقيع", lang)} <span class=\"line\"></span></td></tr></table>");

                b.Append(Sec("Principles followed", "المبادئ المتبعة"));
                b.Append("<div class=\"law\">" + string.Concat(new (string en, string ar)[]
                {
                    ("ACPO 1 — No action should change data that may be relied upon in court.", "ACPO 1 — لا يجوز لأي إجراء أن يغيّر بيانات قد يُعتمد عليها في المحكمة."),
                    ("ACPO 2 — If original data must be accessed, the person must be competent and able to explain the effect of each action.", "ACPO 2 — إن لزم الوصول إلى البيانات الأصلية، فليكن الشخص مؤهلاً وقادراً على شرح أثر كل إجراء."),
                    ("ACPO 3 — An audit trail of all processes must be kept so that an independent party can repeat them and reach the same result.", "ACPO 3 — يُحفظ سجل تدقيق لكل الإجراءات بحيث يكررها طرف مستقل ويصل إلى النتيجة نفسها."),
                    ("ACPO 4 — The officer in charge is responsible for ensuring the law and these principles are followed.", "ACPO 4 — المسؤول عن القضية مسؤول عن الالتزام بالقانون وبهذه المبادئ."),
                    ("ISO/IEC 27037 — identification, collection, acquisition and preservation; relevance, reliability and sufficiency.", "ISO/IEC 27037 — التعرّف والجمع والاستحواذ والحفظ؛ الصلة والموثوقية والكفاية."),
                    ("NIST SP 800-86 — collection, examination, analysis, reporting; hash at acquisition and before analysis.", "NIST SP 800-86 — الجمع والفحص والتحليل والتقرير؛ الهاش عند الاستحواذ وقبل التحليل."),
                    ("RFC 3227 — collect the most volatile evidence first; keep a precise record of who did what and when.", "RFC 3227 — اجمع الأكثر تطايراً أولاً؛ واحفظ سجلاً دقيقاً لمن فعل ماذا ومتى."),
                    ("Course rule — any gap in time or person in this log is a gap in the chain; corrections are new entries, never erasures.", "قاعدة المقرر — أي فجوة في الزمن أو الأشخاص في هذا السجل فجوة في السلسلة؛ والتصحيح قيد جديد، لا شطب."),
                }.Select(x => $"<p>☐ {E(lang == "ar" ? x.ar : x.en)}</p>")) + "</div>");

                b.Append(Sec("Certification and signatures", "الإقرار والتوقيعات"));
                b.Append($"<p>{T("We certify that the information in this record is complete and correct, that the exhibit was handled only by the persons listed, and that every transfer is recorded with its date, time and purpose.", "نقر بأن المعلومات في هذا السجل كاملة وصحيحة، وأن الدليل لم يتعامل معه إلا الأشخاص المذكورون، وأن كل انتقال مسجل بتاريخه ووقته وغرضه.", lang)}</p>");
                b.Append($"<table><thead><tr><th>{T("Role", "الدور", lang)}</th><th>{T("Name", "الاسم", lang)}</th><th>{T("Signature", "التوقيع", lang)}</th><th>{T("Date", "التاريخ", lang)}</th></tr></thead>");
                foreach (var (en, ar, nm) in new[] { ("Seizing officer", "ضابط الضبط", e.SeizedBy), ("Evidence custodian", "أمين الأدلة", (string)null), ("Forensic examiner", "المحقق الجنائي", info.Lead), ("Supervisor / reviewer", "المشرف / المراجع", (string)null) })
                    b.Append($"<tr><td><b>{T(en, ar, lang)}</b></td><td>{(blank ? "" : E(nm))}</td><td><div class=\"sigcell\"><div class=\"wet\"></div></div></td><td></td></tr>");
                b.Append("</table>");
                if (!blank) b.Append($"<p class=\"small\">{T("Case ledger head", "رأس سجل القضية", lang)}: <span class=\"h\">{c.Log.Head}</span> · {T("ledger", "السجل", lang)} {(c.Log.Ok ? T("intact", "سليم", lang) : T("BROKEN", "مكسور", lang))} · {c.Log.Entries.Count} {T("entries", "قيد", lang)}</p>");
            }
            b.Append("</div>").Append(Footer(lang));
            return Finalize(Doc(lang, blank ? "Blank chain of custody form" : $"{c.Info.CaseNo} chain of custody", b.ToString()));
        }

        public static (string html, string fp) CustodyTag(CaseFile c, Evidence e, string lang)
        {
            var evs = c.Custody.Where(x => x.EvidenceId == e.Id).OrderBy(x => x.EventUtc).ToList();
            var b = new StringBuilder(Top(T("Chain of Custody Tag", "بطاقة سلسلة الحيازة", lang), lang));
            b.Append($"<div class=\"caseno\" style=\"font-size:24px\">{E(e.ItemNo)}</div>");
            b.Append(Kv(new[] { (T("Case number", "رقم القضية", lang), $"<span class=\"mono\">{E(c.Info.CaseNo)}</span>"), (T("Description", "الوصف", lang), E(e.Description)),
                (T("Serial", "الرقم التسلسلي", lang), $"<span class=\"mono\">{E(e.Serial)}</span>"), (T("Owner", "المالك", lang), E(e.SeizedFrom)), (T("Seal / bag no.", "رقم الختم/الكيس", lang), E(e.SealNo)) }));
            b.Append($"<table><tr><th>{T("Date", "التاريخ", lang)}</th><th>UTC</th><th>{T("Action", "الإجراء", lang)}</th><th>{T("From → To", "من ← إلى", lang)}</th><th>{T("Location", "المكان", lang)}</th><th>{T("Signature", "التوقيع", lang)}</th></tr>");
            foreach (var x in evs)
                b.Append($"<tr><td class=\"mono\">{E(x.EventUtc?[..Math.Min(10, x.EventUtc.Length)])}</td><td class=\"mono\">{E(x.EventUtc?.Length >= 19 ? x.EventUtc[11..19] : "")}</td><td>{E(CustodyRules.Actions.TryGetValue(x.Action, out var a) ? (lang == "ar" ? a.ar : a.en) : x.Action)}</td><td>{E(x.From)} → {E(x.To)}</td><td>{E(x.Location)}</td><td>{E(x.Signature)}</td></tr>");
            for (int i = evs.Count; i < 8; i++) b.Append("<tr><td>&nbsp;</td><td></td><td></td><td></td><td></td><td></td></tr>");
            b.Append($"</table><p>{T("I acknowledge receipt of the item described above, that I am authorised to take possession of it, and that I am responsible for its custody from the time of receipt.", "أقر بأنني استلمت الدليل المبين أعلاه، وأنني مخوّل باستلامه، وأتحمل مسؤولية حفظه من تاريخ الاستلام.", lang)}</p>");
            b.Append($"<div class=\"sig\"><div>{T("Witness", "الشاهد", lang)}:</div><div>{T("Signature", "التوقيع", lang)}:</div></div>").Append(Footer(lang));
            return Finalize(Doc(lang, e.ItemNo, b.ToString()));
        }
    }
}
