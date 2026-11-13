using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Basma
{
    public sealed class CaseInfo
    {
        public string CaseNo, Title, Type, Agency, Lead, Authority, AuthorityRef, Opened, Status = "open", Description;
    }

    public sealed class Evidence
    {
        public int Id; public string ItemNo, Kind, Description, Make, Model, Serial, Capacity, Condition, SeizedFrom, SeizedLocation, SeizedBy,
            SeizedUtc, SealNo, BiosTime, TrueTime, SourceKind, SourcePath, Status = "in custody";
        public int? BiosOffset; public bool Split;
    }

    public sealed class CustodyEntry
    {
        public int Id, EvidenceId; public int? Corrects;
        public string Action, From, To, EventUtc, Location, Purpose, SealIntact, SealNo, Notes, Signature, Witness, Created;
    }

    public sealed class HashRecord
    {
        public int Id; public int? EvidenceId, ComparedTo;
        public string Kind, Target, TargetKind, Started, Finished, Examiner, Result, ManifestFile, ManifestSha256, Note;
        public long Size; public int FileCount; public double Seconds; public int BadSectors; public long StartLba = -1, CountLba = -1;
        public Dictionary<string, string> H = new();
        public Dictionary<string, int> Stats;
        public List<string> Added, Removed, Modified;
        public string Get(string a) => H != null && H.TryGetValue(a, out var v) ? v : null;
    }

    public sealed class NoteEntry { public int Id; public string Author, Body, Created; }
    public sealed class CheckState { public string Step, Done, By, Note; }
    public sealed class Attachment { public int Id; public int? EvidenceId; public string Kind, Name, File, Sha256, Caption, Created; public long Size; }

    public sealed class Finding { public string Level, Code, Ar, En; public int? EntryId; public string Text => L.Ar ? Ar : En; }
    public sealed class Continuity { public string Status, Holder, Location; public bool Ended; public List<Finding> Findings = new(); }

    /// <summary>Append-only, hash-chained event log. Line format (fixed-length prefix):
    /// {"prev":"&lt;64 hex&gt;","hash":"&lt;64 hex&gt;","body":&lt;json&gt;}  with hash = SHA-256(prev + body).</summary>
    public sealed class Ledger
    {
        public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
        const int Prefix = 156; // {"prev":"(9)+64+","hash":"(10)+64+","body":(9)
        public readonly string Path;
        public readonly List<(int seq, string ts, string actor, string type, JsonElement data, string prev, string hash, string body)> Entries = new();
        public bool Ok = true; public int BrokenAt = -1; public string Reason;
        public string Head => Entries.Count == 0 ? Genesis : Entries[^1].hash;

        public Ledger(string path) { Path = path; Load(); }

        static string H(string prev, string body) => Fmt.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(prev + body)));

        public void Load()
        {
            Entries.Clear(); Ok = true; BrokenAt = -1; Reason = null;
            if (!File.Exists(Path)) return;
            string prev = Genesis; int n = 0;
            foreach (var raw in File.ReadAllLines(Path, Encoding.UTF8))
            {
                if (raw.Length == 0) continue;
                n++;
                string line = raw.TrimStart('﻿');
                try
                {
                    if (line.Length < Prefix + 2 || !line.StartsWith("{\"prev\":\"", StringComparison.Ordinal)) throw new FormatException("bad line format");
                    string p = line.Substring(9, 64), h = line.Substring(83, 64), body = line.Substring(Prefix, line.Length - Prefix - 1);
                    if (Ok && p != prev) { Ok = false; BrokenAt = n; Reason = "prev"; }
                    if (Ok && H(p, body) != h) { Ok = false; BrokenAt = n; Reason = "hash"; }
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    int seq = root.GetProperty("seq").GetInt32();
                    if (Ok && seq != n) { Ok = false; BrokenAt = n; Reason = "sequence"; }
                    Entries.Add((seq, root.GetProperty("ts").GetString(), root.GetProperty("actor").GetString(), root.GetProperty("type").GetString(),
                        root.GetProperty("data").Clone(), p, h, body));
                    prev = h;
                }
                catch (Exception e) { if (Ok) { Ok = false; BrokenAt = n; Reason = "parse: " + e.Message; } }
            }
        }

        public void Append(string type, string actor, object data)
        {
            int seq = Entries.Count + 1;
            string ts = Fmt.NowUtc();
            string body = "{\"seq\":" + seq.ToString(CultureInfo.InvariantCulture) + ",\"ts\":" + JsonSerializer.Serialize(ts) + ",\"actor\":" + Js.S(actor ?? "") +
                          ",\"type\":" + Js.S(type) + ",\"data\":" + Js.S(data) + "}";
            string prev = Head, h = H(prev, body);
            string line = "{\"prev\":\"" + prev + "\",\"hash\":\"" + h + "\",\"body\":" + body + "}";
            File.AppendAllText(Path, line + "\n", new UTF8Encoding(false));
            using var doc = JsonDocument.Parse(body);
            Entries.Add((seq, ts, actor, type, doc.RootElement.GetProperty("data").Clone(), prev, h, body));
        }
    }

    /// <summary>A case = a folder with ledger.jsonl; the state is rebuilt by replaying the ledger.</summary>
    public sealed class CaseFile
    {
        public readonly string Folder;
        public readonly Ledger Log;
        public CaseInfo Info = new();
        public List<Evidence> Items = new();
        public List<CustodyEntry> Custody = new();
        public List<HashRecord> Hashes = new();
        public List<NoteEntry> Notes = new();
        public Dictionary<string, CheckState> Checks = new();
        public List<Attachment> Attachments = new();
        public List<(string ts, string file, string kind, string fp)> Reports = new();

        public CaseFile(string folder) { Folder = folder; Directory.CreateDirectory(folder); Log = new Ledger(System.IO.Path.Combine(folder, "ledger.jsonl")); Replay(); }

        public static string Safe(string s) => string.Concat((s ?? "case").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_')).Trim('_');

        T D<T>(JsonElement e) => e.Deserialize<T>(Js.O);

        public void Replay()
        {
            Info = new(); Items.Clear(); Custody.Clear(); Hashes.Clear(); Notes.Clear(); Checks.Clear(); Attachments.Clear(); Reports.Clear();
            foreach (var e in Log.Entries)
            {
                switch (e.type)
                {
                    case "case.create": case "case.update": Info = D<CaseInfo>(e.data); break;
                    case "evidence.add": Items.Add(D<Evidence>(e.data)); break;
                    case "evidence.update": { var ev = D<Evidence>(e.data); int i = Items.FindIndex(x => x.Id == ev.Id); if (i >= 0) Items[i] = ev; break; }
                    case "custody.add": Custody.Add(D<CustodyEntry>(e.data)); break;
                    case "hash.add": Hashes.Add(D<HashRecord>(e.data)); break;
                    case "note.add": Notes.Add(D<NoteEntry>(e.data)); break;
                    case "check.set": { var c = D<CheckState>(e.data); Checks[c.Step] = c; break; }
                    case "attach.add": Attachments.Add(D<Attachment>(e.data)); break;
                    case "report.issue":
                        Reports.Add((e.ts, e.data.GetProperty("file").GetString(), e.data.GetProperty("kind").GetString(), e.data.GetProperty("fingerprint").GetString()));
                        break;
                }
            }
        }

        void Do(string type, object data, string actor) { Log.Append(type, actor, data); Replay(); }

        public static CaseFile Create(string root, CaseInfo info, string actor)
        {
            var folder = System.IO.Path.Combine(root, Safe(info.CaseNo));
            if (File.Exists(System.IO.Path.Combine(folder, "ledger.jsonl"))) throw new InvalidOperationException(L.T("A case with this number already exists."));
            var c = new CaseFile(folder);
            if (string.IsNullOrEmpty(info.Opened)) info.Opened = Fmt.NowUtc();
            c.Do("case.create", info, actor);
            return c;
        }

        public void UpdateInfo(CaseInfo info, string actor) { info.CaseNo = Info.CaseNo; Do("case.update", info, actor); }

        public Evidence AddEvidence(Evidence e, string actor, CustodyEntry seizure)
        {
            e.Id = Items.Count == 0 ? 1 : Items.Max(x => x.Id) + 1;
            if (string.IsNullOrWhiteSpace(e.ItemNo)) e.ItemNo = $"E{e.Id:D3}";
            if (Items.Any(x => string.Equals(x.ItemNo, e.ItemNo, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(L.T("This item number is already used in the case."));
            e.BiosOffset = CustodyRules.BiosOffset(e.BiosTime, e.TrueTime);
            Do("evidence.add", e, actor);
            if (seizure != null) { seizure.EvidenceId = e.Id; AddCustody(seizure, actor); }
            return e;
        }

        public void UpdateEvidence(Evidence e, string actor) { e.BiosOffset = CustodyRules.BiosOffset(e.BiosTime, e.TrueTime); Do("evidence.update", e, actor); }

        public void AddCustody(CustodyEntry c, string actor)
        {
            if (!CustodyRules.Actions.ContainsKey(c.Action ?? "")) throw new ArgumentException("action");
            if (Fmt.ParseUtc(c.EventUtc) == null) throw new ArgumentException(L.T("A valid date and time is required."));
            c.Id = Custody.Count == 0 ? 1 : Custody.Max(x => x.Id) + 1;
            c.Created = Fmt.NowUtc();
            Do("custody.add", c, actor);
            var st = c.Action switch { "released" => "released", "destroyed" => "destroyed", "stored" or "returned" => "in storage", "checked_out" => "checked out", "court" => "in court", _ => null };
            var ev = Items.FirstOrDefault(x => x.Id == c.EvidenceId);
            if (st != null && ev != null && ev.Status != st) { ev.Status = st; Do("evidence.update", ev, actor); }
        }

        public HashRecord AddHash(HashResult r, string kind, int? evidenceId, string actor)
        {
            var rec = new HashRecord
            {
                Id = Hashes.Count == 0 ? 1 : Hashes.Max(x => x.Id) + 1, EvidenceId = evidenceId, Kind = kind, Target = r.Target, TargetKind = r.TargetKind,
                Started = r.Started, Finished = r.Finished, Seconds = r.Seconds, Size = r.Size, FileCount = r.FileCount, H = r.H, Examiner = actor,
                BadSectors = r.BadSectors.Count, StartLba = r.StartLba, CountLba = r.CountLba, Note = r.Note, Stats = r.Stats
            };
            if (r.Rows != null)
            {
                var mf = System.IO.Path.Combine(Folder, "manifests", $"{kind}_{evidenceId?.ToString() ?? "x"}_{rec.Id}_{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv");
                rec.ManifestSha256 = Engine.WriteManifest(r, mf);
                rec.ManifestFile = System.IO.Path.GetRelativePath(Folder, mf);
            }
            if (kind == "verification")
            {
                var b = Hashes.FirstOrDefault(x => x.EvidenceId == evidenceId && x.Kind == "acquisition") ?? throw new InvalidOperationException(L.T("Compute the acquisition hash first."));
                rec.ComparedTo = b.Id;
                var common = new[] { "md5", "sha1", "sha256", "sha384", "sha512", "sha3_256", "crc32" }.Where(a => b.Get(a) != null && rec.Get(a) != null).ToList();
                rec.Result = common.Count > 0 && common.All(a => b.Get(a) == rec.Get(a)) ? "MATCH" : "MISMATCH";
                if (r.Rows != null && b.ManifestFile != null)
                {
                    try
                    {
                        var d = Engine.Diff(Engine.ReadManifest(System.IO.Path.Combine(Folder, b.ManifestFile)), r.Rows);
                        rec.Added = d.OnlyB.Take(200).ToList(); rec.Removed = d.OnlyA.Take(200).ToList();
                        rec.Modified = d.Modified.Concat(d.Moved.Select(m => m.from + " → " + m.to)).Take(200).ToList();
                    }
                    catch { }
                }
            }
            else rec.Result = kind == "acquisition" ? "BASELINE" : "COMPUTED";
            Do("hash.add", rec, actor);
            return rec;
        }

        public void AddNote(string body, string actor)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            Do("note.add", new NoteEntry { Id = Notes.Count + 1, Author = actor, Body = body.Trim(), Created = Fmt.NowUtc() }, actor);
        }

        public void SetCheck(string step, bool done, string by, string note, string actor) =>
            Do("check.set", new CheckState { Step = step, Done = done ? Fmt.NowUtc() : null, By = done ? by : null, Note = note }, actor);

        public Attachment Attach(string src, string kind, int? evidenceId, string caption, string actor)
        {
            var dir = System.IO.Path.Combine(Folder, "attachments");
            Directory.CreateDirectory(dir);
            var dst = System.IO.Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}_{System.IO.Path.GetFileName(src)}");
            string hs = Fmt.Hex(SHA256.HashData(File.ReadAllBytes(src)));
            File.Copy(src, dst);
            string hd = Fmt.Hex(SHA256.HashData(File.ReadAllBytes(dst)));
            if (hs != hd) { File.Delete(dst); throw new IOException("copy verification failed"); }
            var a = new Attachment { Id = Attachments.Count + 1, EvidenceId = evidenceId, Kind = kind, Name = System.IO.Path.GetFileName(src), File = System.IO.Path.GetRelativePath(Folder, dst), Sha256 = hd, Size = new FileInfo(dst).Length, Caption = caption, Created = Fmt.NowUtc() };
            Do("attach.add", a, actor);
            return a;
        }

        public void LogReport(string file, string kind, string fp, string actor) =>
            Do("report.issue", new { file = System.IO.Path.GetRelativePath(Folder, file), kind, fingerprint = fp, sha256 = Fmt.Hex(SHA256.HashData(File.ReadAllBytes(file))) }, actor);

        public Continuity ContinuityOf(Evidence e) => CustodyRules.Analyze(e, Custody.Where(x => x.EvidenceId == e.Id).ToList(), Hashes.Where(x => x.EvidenceId == e.Id).ToList());

        /// <summary>Verifies every file referenced by the ledger (manifests, attachments, reports) still has its recorded hash.</summary>
        public List<string> VerifyFiles()
        {
            var bad = new List<string>();
            void Chk(string rel, string sha) { if (rel == null || sha == null) return; var p = System.IO.Path.Combine(Folder, rel); if (!File.Exists(p)) bad.Add(rel + " (missing)"); else if (Fmt.Hex(SHA256.HashData(File.ReadAllBytes(p))) != sha) bad.Add(rel + " (changed)"); }
            foreach (var h in Hashes) Chk(h.ManifestFile, h.ManifestSha256);
            foreach (var a in Attachments) Chk(a.File, a.Sha256);
            foreach (var e in Log.Entries.Where(x => x.type == "report.issue")) Chk(e.data.GetProperty("file").GetString(), e.data.GetProperty("sha256").GetString());
            return bad;
        }

        public (int ev, int custody, int acquired, int verified, int mismatches, int broken, int warnings, int checks, int reports) Stats()
        {
            var cont = Items.Select(ContinuityOf).ToList();
            return (Items.Count, Custody.Count,
                Items.Count(i => Hashes.Any(h => h.EvidenceId == i.Id && h.Kind == "acquisition")),
                Items.Count(i => Hashes.Any(h => h.EvidenceId == i.Id && h.Kind == "verification" && h.Result == "MATCH")),
                Hashes.Count(h => h.Result == "MISMATCH"), cont.Count(c => c.Status == "BROKEN"), cont.Count(c => c.Status == "WARNINGS"),
                Checks.Values.Count(c => c.Done != null), Reports.Count);
        }
    }

    public static class CustodyRules
    {
        // code → (ar, en, kind)
        public static readonly Dictionary<string, (string ar, string en, string kind)> Actions = new()
        {
            ["seized"] = ("ضبط", "Seized", "start"), ["collected"] = ("جمع", "Collected", "start"),
            ["received"] = ("استلام", "Received", "transfer"), ["packaged"] = ("تحريز وختم", "Packaged & sealed", "other"),
            ["transferred"] = ("تسليم", "Transferred", "transfer"), ["transported"] = ("نقل", "Transported", "other"),
            ["stored"] = ("إيداع في الخزنة", "Stored in locker", "storage"), ["checked_out"] = ("سحب من الخزنة", "Checked out", "transfer"),
            ["returned"] = ("إعادة إلى الخزنة", "Returned to storage", "storage"), ["imaged"] = ("تصوير جنائي", "Forensically imaged", "exam"),
            ["analyzed"] = ("فحص وتحليل", "Examined / analysed", "exam"), ["seal_opened"] = ("فتح الختم", "Seal opened", "exam"),
            ["resealed"] = ("إعادة الختم", "Resealed", "other"), ["court"] = ("تقديم للمحكمة", "Presented in court", "other"),
            ["released"] = ("تسليم نهائي لصاحبه", "Released to owner", "end"), ["destroyed"] = ("إتلاف", "Destroyed", "end"),
            ["correction"] = ("تصحيح قيد سابق", "Correction of earlier entry", "other"),
        };
        public static string ActionLabel(string code) => Actions.TryGetValue(code ?? "", out var a) ? (L.Ar ? a.ar : a.en) : code;

        public static readonly (string code, string ar, string en)[] Checklist =
        {
            ("authority","التحقق من السلطة القانونية (مذكرة/تفويض) ونطاقها","Verify legal authority (warrant/authorisation) and its scope"),
            ("photo_scene","تصوير المسرح والشاشة قبل لمس أي شيء","Photograph the scene and the screen before touching anything"),
            ("encryption","فحص وجود تشفير كامل للقرص (BitLocker/VeraCrypt)","Check for full-disk encryption (BitLocker/VeraCrypt)"),
            ("memory","التقاط الذاكرة RAM بأداة جنائية إلى وسيط خارجي","Capture RAM with a forensic tool to external media"),
            ("processes","توثيق العمليات الجارية (Task Manager / tasklist)","Record running processes (Task Manager / tasklist)"),
            ("netstat","توثيق الاتصالات الشبكية (netstat -ano)","Record network connections (netstat -ano)"),
            ("sessions","توثيق الجلسات البعيدة (net session)","Record remote sessions (net session)"),
            ("openfiles","توثيق الملفات المفتوحة (openfiles)","Record open shared files (openfiles)"),
            ("power","سحب القابس / نزع البطارية وتوثيق الوقت","Pull the plug / remove battery and record the time"),
            ("label","وضع علامات على الأسلاك والمنافذ وتصوير كل الزوايا","Label cables/ports and photograph all angles"),
            ("bios","تسجيل وقت BIOS مقابل الوقت الحقيقي","Record BIOS time against true time"),
            ("media","إخراج الوسائط القابلة للإزالة ونموذج حيازة لكل منها","Eject removable media; one custody form each"),
            ("drives","إزالة كل الأقراص، حتى غير الموصولة","Remove all drives, even unconnected ones"),
            ("package","التحريز في أكياس مضادة للكهرباء الساكنة / فاراداي وختمها","Package in anti-static / Faraday bags and seal"),
            ("transport","النقل المباشر إلى المختبر دون توقف","Transport directly to the lab without stops"),
            ("writeblock","توصيل الدليل عبر حاجب كتابة","Connect evidence through a write blocker"),
            ("hash_acq","حساب الهاش عند الاستحواذ (الأصل والنسخة)","Hash at acquisition (original and image)"),
            ("verify","التحقق من تطابق الهاش قبل التحليل","Verify hash match before analysis"),
        };

        public static int? BiosOffset(string bios, string truth)
        {
            var b = Fmt.ParseUtc(bios); var t = Fmt.ParseUtc(truth);
            return b == null || t == null ? null : (int)Math.Round((b.Value - t.Value).TotalSeconds);
        }

        public static string Corrected(string ts, int? off)
        {
            var d = Fmt.ParseUtc(ts);
            return d == null || off == null ? null : Fmt.Utc(d.Value.AddSeconds(-off.Value));
        }

        static Finding F(string lvl, string code, string ar, string en, int? id = null) => new() { Level = lvl, Code = code, Ar = ar, En = en, EntryId = id };

        public static Continuity Analyze(Evidence ev, List<CustodyEntry> events, List<HashRecord> hashes)
        {
            var c = new Continuity();
            var evs = events.OrderBy(e => Fmt.ParseUtc(e.EventUtc) ?? DateTime.MaxValue).ThenBy(e => e.Id).ToList();
            string holder = null, location = null; CustodyEntry prev = null; bool stored = false, ended = false;
            if (evs.Count == 0) c.Findings.Add(F("warning", "no_events", "لا توجد أي قيود في سلسلة الحيازة لهذا الدليل.", "No chain-of-custody entries recorded for this item."));
            for (int i = 0; i < evs.Count; i++)
            {
                var e = evs[i];
                var kind = Actions.TryGetValue(e.Action ?? "", out var a) ? a.kind : "other";
                string lab = a.ar ?? e.Action;
                var t = Fmt.ParseUtc(e.EventUtc);
                if (t == null) c.Findings.Add(F("critical", "bad_time", $"القيد #{e.Id} ({lab}) بلا تاريخ/وقت صالح.", $"Entry #{e.Id} ({e.Action}) has no valid date/time.", e.Id));
                if (i == 0 && kind != "start") c.Findings.Add(F("warning", "first_not_seizure", $"أول قيد هو «{lab}» وليس ضبطاً أو جمعاً؛ بداية السلسلة غير موثقة.", $"First entry is '{e.Action}', not a seizure/collection; the origin is undocumented.", e.Id));
                if (ended && e.Action != "correction") c.Findings.Add(F("critical", "after_end", $"القيد #{e.Id} جاء بعد تسليم الدليل نهائياً أو إتلافه.", $"Entry #{e.Id} occurs after the item was released/destroyed.", e.Id));
                if (prev != null && t != null && Fmt.ParseUtc(prev.EventUtc) is DateTime pt)
                {
                    double gap = (t.Value - pt).TotalHours;
                    if (gap > 24 && !stored && holder != null)
                        c.Findings.Add(F("warning", "long_hold", $"بقي الدليل مع «{holder}» نحو {gap:0} ساعة خارج الخزنة دون قيد (بين #{prev.Id} و#{e.Id}).",
                            $"Item stayed with '{holder}' for ~{gap:0} h outside storage with no entry (between #{prev.Id} and #{e.Id}).", e.Id));
                }
                string frm = (e.From ?? "").Trim(), to = (e.To ?? "").Trim();
                if (kind == "transfer")
                {
                    if (frm == "" || to == "") c.Findings.Add(F("critical", "transfer_missing_party", $"القيد #{e.Id} ({lab}) ينقص فيه المسلِّم أو المستلم.", $"Entry #{e.Id} ({e.Action}) is missing the releasing or receiving person.", e.Id));
                    else if (holder != null && !string.Equals(frm, holder, StringComparison.CurrentCultureIgnoreCase))
                        c.Findings.Add(F("critical", "continuity_break", $"انقطاع: القيد #{e.Id} يقول إن «{frm}» سلّم الدليل، لكن الحائز المسجل قبله كان «{holder}».",
                            $"Break: entry #{e.Id} says '{frm}' released the item, but the recorded holder was '{holder}'.", e.Id));
                }
                if (string.Equals(e.SealIntact, "no", StringComparison.OrdinalIgnoreCase)) c.Findings.Add(F("critical", "seal_broken", $"الختم لم يكن سليماً عند القيد #{e.Id}.", $"Seal reported NOT intact at entry #{e.Id}.", e.Id));
                if (string.IsNullOrWhiteSpace(e.Signature) && e.Action != "correction") c.Findings.Add(F("warning", "no_signature", $"القيد #{e.Id} غير موقّع.", $"Entry #{e.Id} is unsigned.", e.Id));
                if (to != "") holder = to; else if (kind == "start" && frm != "") holder = frm;
                if (!string.IsNullOrWhiteSpace(e.Location)) location = e.Location;
                stored = kind == "storage";
                if (kind == "end") ended = true;
                prev = e;
            }
            var acq = hashes.Where(h => h.Kind == "acquisition").ToList();
            var ver = hashes.Where(h => h.Kind == "verification").ToList();
            if (acq.Count == 0) c.Findings.Add(F("warning", "no_acq_hash", "لم يُحسب هاش الاستحواذ لهذا الدليل بعد.", "No acquisition hash has been computed for this item."));
            foreach (var v in ver.Where(v => v.Result == "MISMATCH"))
                c.Findings.Add(F("critical", "hash_mismatch", $"التحقق #{v.Id} بتاريخ {v.Finished}: الهاش لا يطابق هاش الاستحواذ.", $"Verification #{v.Id} at {v.Finished}: hash does NOT match acquisition."));
            foreach (var h in hashes.Where(h => h.BadSectors > 0))
                c.Findings.Add(F("warning", "bad_sectors", $"الهاش #{h.Id}: {h.BadSectors} قطاعاً غير مقروء استُبدلت بأصفار؛ قد يختلف الهاش في قراءة لاحقة.", $"Hash #{h.Id}: {h.BadSectors} unreadable sectors were zero-filled; a later read may differ."));
            if (acq.Count > 0 && ver.Count == 0) c.Findings.Add(F("info", "not_verified", "لم يُجرَ تحقق لاحق من الهاش منذ الاستحواذ.", "No later hash verification since acquisition."));
            if (ev.BiosOffset is int off && Math.Abs(off) >= 60)
                c.Findings.Add(F("info", "clock_drift", $"ساعة BIOS منحرفة {off / 60.0:+0.0;-0.0} دقيقة عن الوقت الحقيقي؛ تُصحح أزمنة الملفات في الخط الزمني.", $"BIOS clock is off by {off / 60.0:+0.0;-0.0} min from true time; file times are corrected in the timeline."));
            c.Status = c.Findings.Any(f => f.Level == "critical") ? "BROKEN" : c.Findings.Any(f => f.Level == "warning") ? "WARNINGS" : "INTACT";
            c.Holder = holder; c.Location = location; c.Ended = ended;
            return c;
        }
    }
}
