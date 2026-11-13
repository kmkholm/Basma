using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Basma
{
    public partial class App : Application
    {
        public const string Version = "1.1.0";
        public static string DataArg = "";

        protected override void OnStartup(StartupEventArgs e)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var args = e.Args;
            string Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string data = Arg("--data");
            DataArg = data != null ? $"--data \"{data}\"" : "";
            try { Store.Init(data); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "BASMA"); Shutdown(1); return; }
            if (args.Contains("--demo"))
            {
                try { Demo.Build(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(Store.Data, "demo_error.txt"), ex.ToString()); Shutdown(3); }
                return;
            }
            if (Arg("--selftest") is string log)
            {
                var r = SelfTest.Run(log);
                Shutdown(r.All(x => x.ok) ? 0 : 1);
                return;
            }
            if (Arg("--snap") is string dir)
            {
                try { Snap(dir, Arg("--lang") ?? "ar"); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "snap_error.txt"), ex.ToString()); Shutdown(2); }
                return;
            }
            DispatcherUnhandledException += (_, ex) => { MessageBox.Show(ex.Exception.Message, "BASMA"); ex.Handled = true; };
            base.OnStartup(e);
            new MainWindow().Show();
        }

        /// <summary>Renders every page to PNG without showing a window (for visual checks), and lists untranslated strings.</summary>
        static void Snap(string dir, string lang)
        {
            Directory.CreateDirectory(dir);
            Store.S.Lang = lang;
            var w = new MainWindow();
            w.Content = null;
            void Shot(string name, string page, object arg = null) { w.Go(page, arg); w.ApplyLang(); w.SnapPng(Path.Combine(dir, $"{lang}_{name}.png"), 1440); }
            foreach (var p in new[] { "home", "hash", "disks", "compare", "tools", "sets", "cases", "settings" }) Shot(p, p);
            foreach (var t in new[] { "image", "search", "carve", "strings", "entropy", "wipe", "checksum", "usb", "hex", "coc" }) Shot("tool_" + t, "tools", t);
            var c = Store.AllCases().FirstOrDefault(x => x.Items.Count > 0);
            if (c != null)
            {
                Report.Save(Report.CustodyRecord(c, c.Items, lang, false).html, dir, $"{lang}_coc_record");
                Report.Save(Report.CustodyRecord(null, null, lang, true).html, dir, $"{lang}_coc_blank");
            }
            if (c != null)
            {
                foreach (var t in new[] { "ev", "check", "tl", "notes", "rep", "led", "info" }) Shot("case_" + t, "case", (c, t));
                foreach (var ev in c.Items) Shot("ev_" + ev.ItemNo, "ev", (c, ev.Id));
                Shot("ev_new", "ev", (c, 0));
            }
            File.WriteAllLines(Path.Combine(dir, $"missing_{lang}.txt"), L.Missing.OrderBy(x => x));
        }
    }
}
