using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Basma
{
    public sealed class Settings
    {
        public string Examiner = "", Agency = "", Lang = "ar";
        public List<string> Algos = Basma.Algos.Default.ToList();
    }

    public sealed class HistoryItem { public string Ts, Kind, Target, TargetKind, Sha256, Md5, Result, Report; public long Size; public double Seconds; }

    public static class Store
    {
        public static string Data;
        public static Settings S = new();
        public static KnownSets Known;
        public static string CasesDir => Path.Combine(Data, "cases");
        public static string ReportsDir => Path.Combine(Data, "reports");
        static string SettingsFile => Path.Combine(Data, "settings.json");
        static string HistoryFile => Path.Combine(Data, "history.jsonl");

        public static void Init(string dataOverride)
        {
            Data = dataOverride;
            if (Data == null)
            {
                var exeDir = AppContext.BaseDirectory;
                var portable = Path.Combine(exeDir, "BasmaData");
                try { Directory.CreateDirectory(portable); File.WriteAllText(Path.Combine(portable, ".w"), "1"); File.Delete(Path.Combine(portable, ".w")); Data = portable; }
                catch { Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Basma"); }
            }
            Data = Path.GetFullPath(Data);
            Directory.CreateDirectory(Data); Directory.CreateDirectory(CasesDir); Directory.CreateDirectory(ReportsDir);
            try { if (File.Exists(SettingsFile)) S = Js.D<Settings>(File.ReadAllText(SettingsFile)) ?? new(); } catch { S = new(); }
            S.Algos ??= Algos.Default.ToList();
            Known = new KnownSets(Path.Combine(Data, "hashsets"));
        }

        public static void Save() => File.WriteAllText(SettingsFile, JsonSerializer.Serialize(S, Js.Pretty));

        public static string Actor => string.IsNullOrWhiteSpace(S.Examiner) ? Environment.UserName : S.Examiner;

        public static List<CaseFile> AllCases()
        {
            var list = new List<CaseFile>();
            foreach (var d in Directory.GetDirectories(CasesDir))
                if (File.Exists(Path.Combine(d, "ledger.jsonl"))) { try { list.Add(new CaseFile(d)); } catch { } }
            return list.OrderByDescending(c => c.Log.Entries.LastOrDefault().ts).ToList();
        }

        public static void AddHistory(HashResult r, string kind, string result, string report)
        {
            var h = new HistoryItem { Ts = Fmt.NowUtc(), Kind = kind, Target = r.Target, TargetKind = r.TargetKind, Sha256 = r.Get("sha256"), Md5 = r.Get("md5"), Result = result, Report = report, Size = r.Size, Seconds = r.Seconds };
            File.AppendAllText(HistoryFile, Js.S(h) + "\n", new UTF8Encoding(false));
        }

        public static List<HistoryItem> History(int n = 200)
        {
            if (!File.Exists(HistoryFile)) return new();
            var res = new List<HistoryItem>();
            foreach (var l in File.ReadLines(HistoryFile)) { try { res.Add(Js.D<HistoryItem>(l)); } catch { } }
            return res.AsEnumerable().Reverse().Take(n).ToList();
        }

        public static void OpenInShell(string path)
        {
            try
            {
                if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                else if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>Volatile-state capture (Chapter 4: before power-off). Read-only commands; outputs saved byte-for-byte.</summary>
    public static class Live
    {
        public static readonly (string code, string cmd, string ar, string en)[] Commands =
        {
            ("tasklist","tasklist /v","العمليات الجارية","Running processes"),
            ("netstat","netstat -ano","الاتصالات الشبكية مع رقم العملية","Network connections with PID"),
            ("net_session","net session","الجلسات البعيدة (تحتاج صلاحيات المسؤول)","Remote sessions (needs admin)"),
            ("openfiles","openfiles /query /fo table","الملفات المشتركة المفتوحة","Open shared files"),
            ("query_user","query user","المستخدمون المسجلون","Logged-on users"),
            ("ipconfig","ipconfig /all","إعدادات الشبكة","Network configuration"),
            ("arp","arp -a","جدول ARP","ARP cache"),
            ("route","route print","جدول التوجيه","Routing table"),
            ("dns_cache","ipconfig /displaydns","ذاكرة DNS المؤقتة","DNS resolver cache"),
            ("net_use","net use","محركات الشبكة الموصولة","Mapped network drives"),
            ("net_share","net share","المجلدات المشاركة","Shared folders"),
            ("schtasks","schtasks /query /fo LIST","المهام المجدولة","Scheduled tasks"),
            ("systeminfo","systeminfo","معلومات النظام والوقت","System info and time"),
        };

        public static List<(string code, string cmd, string file, int exit)> Capture(string folder, IEnumerable<string> codes, Prog p)
        {
            Directory.CreateDirectory(folder);
            var sel = Commands.Where(c => codes.Contains(c.code)).ToList();
            p.FilesTotal = sel.Count;
            var res = new List<(string, string, string, int)>();
            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            foreach (var c in sel)
            {
                if (p.Cancel) break;
                p.Current = c.cmd;
                string started = Fmt.NowUtc();
                var psi = new ProcessStartInfo("cmd.exe", "/c " + c.cmd) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                byte[] outp; int code;
                var sw = Stopwatch.StartNew();
                using (var pr = Process.Start(psi))
                {
                    var ms = new MemoryStream();
                    var errTask = pr.StandardError.BaseStream.CopyToAsync(new MemoryStream());
                    var outTask = pr.StandardOutput.BaseStream.CopyToAsync(ms);
                    if (!pr.WaitForExit(90000)) { try { pr.Kill(true); } catch { } }
                    outTask.Wait(5000);
                    outp = ms.ToArray(); code = pr.HasExited ? pr.ExitCode : -1;
                }
                var header = Encoding.UTF8.GetBytes($"# BASMA live capture\n# command: {c.cmd}\n# started_utc: {started}\n# host: {Environment.MachineName}\n# exit_code: {code}\n# duration_s: {sw.Elapsed.TotalSeconds:0.00}\n# ---- raw output follows (bytes as captured) ----\n");
                var file = Path.Combine(folder, $"{stamp}_{c.code}.txt");
                File.WriteAllBytes(file, header.Concat(outp).ToArray());
                res.Add((c.code, c.cmd, file, code));
                p.FilesDone++;
            }
            return res;
        }
    }
}
