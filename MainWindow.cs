using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Basma
{
    public partial class MainWindow : Window
    {
        public static MainWindow I;
        public readonly Grid Root = new();
        readonly StackPanel nav = new();
        readonly TextBlock kicker = Ui.Tx("", 12, true, Ui.Muted), title = Ui.Tx("", 32, true);
        readonly WrapPanel actions = new() { VerticalAlignment = VerticalAlignment.Bottom };
        readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly StackPanel toasts = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24) };
        readonly TextBlock who = Ui.Tx("", 13, true, Ui.White), adminTxt = Ui.Tx("", 11.5, false, Ui.Side);
        string page = "home"; object arg;

        public MainWindow()
        {
            I = this;
            Title = "BASMA · بصمة";
            Width = 1440; Height = 900; MinWidth = 1080; MinHeight = 640;
            Background = Ui.Paper; FontFamily = Ui.Sans; FontSize = 14;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Basma.ico")); } catch { }
            BuildShell();
            Content = Root;
            ApplyLang();
            Go("home");
        }

        void BuildShell()
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(262) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = new DockPanel { Background = Ui.Ink };
            var brand = new StackPanel { Margin = new Thickness(20, 20, 20, 14) };
            var mark = new StackPanel { Orientation = Orientation.Horizontal };
            mark.Children.Add(Logo(44));
            var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            words.Children.Add(Ui.Tx("بصمة", 30, true, Ui.White, false, false));
            words.Children.Add(Ui.Tx("B A S M A", 12, true, Ui.Yellow, true, false));
            mark.Children.Add(words);
            brand.Children.Add(mark);
            brand.Children.Add(Ui.Tx(L.T("Forensic hashing · comparison · chain of custody"), 11.5, false, Ui.Side));
            var top = new StackPanel();
            top.Children.Add(brand);
            top.Children.Add(Tape());
            DockPanel.SetDock(top, Dock.Top);
            side.Children.Add(top);
            var foot = new StackPanel { Margin = new Thickness(20, 12, 20, 16) };
            foot.Children.Add(Ui.Tx(L.T("Current examiner"), 11.5, false, Ui.Side));
            foot.Children.Add(who);
            foot.Children.Add(adminTxt);
            var langs = new UniformGrid2();
            foot.Children.Add(langs.Root);
            DockPanel.SetDock(foot, Dock.Bottom);
            side.Children.Add(foot);
            side.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Grid.SetColumn(side, 0);
            Root.Children.Add(side);

            var main = new DockPanel();
            var head = new Border { BorderBrush = Ui.Ink, BorderThickness = new Thickness(0, 0, 0, 3), Padding = new Thickness(34, 22, 34, 16), Background = Ui.Paper };
            var hd = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            hd.Children.Add(actions);
            var ht = new StackPanel(); ht.Children.Add(kicker); ht.Children.Add(title);
            hd.Children.Add(ht);
            head.Child = hd;
            DockPanel.SetDock(head, Dock.Top);
            main.Children.Add(head);
            main.Children.Add(scroll);
            Grid.SetColumn(main, 1);
            Root.Children.Add(main);
            Grid.SetColumnSpan(toasts, 2);
            Root.Children.Add(toasts);
        }

        sealed class UniformGrid2
        {
            public Border Root;
            public UniformGrid2()
            {
                var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
                foreach (var (code, lab) in new[] { ("ar", "عربي"), ("en", "English") })
                {
                    var b = new Button { Content = lab, Margin = new Thickness(0), Padding = new Thickness(6, 5, 6, 5), FontSize = 13 };
                    b.Click += (_, _) => { Store.S.Lang = code; Store.Save(); I.ApplyLang(); I.Go(I.page, I.arg); };
                    b.Tag = code;
                    g.Children.Add(b);
                }
                Root = new Border { Margin = new Thickness(0, 10, 0, 0), Child = g };
                LangButtons = g;
            }
        }
        static System.Windows.Controls.Primitives.UniformGrid LangButtons;

        public static FrameworkElement Logo(double size)
        {
            var c = new Canvas { Width = size, Height = size, FlowDirection = FlowDirection.LeftToRight };
            double s = size / 46.0;
            void R(double x, Brush b) { var r = new System.Windows.Shapes.Rectangle { Width = 22 * s, Height = 16 * s, Stroke = b, StrokeThickness = 4 * s }; Canvas.SetLeft(r, x * s); Canvas.SetTop(r, 15 * s); c.Children.Add(r); }
            R(3, Ui.Yellow); R(21, Ui.Orange);
            return c;
        }

        public static FrameworkElement Tape()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(28, 28), MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(255, 212, 0), 0)); b.GradientStops.Add(new GradientStop(Color.FromRgb(255, 212, 0), .5));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(17, 17, 17), .5)); b.GradientStops.Add(new GradientStop(Color.FromRgb(17, 17, 17), 1));
            return new Border { Height = 10, Background = b };
        }

        static readonly (string key, string label, string num, string group)[] Pages =
        {
            ("home", "Dashboard", "01", "WORK"), ("hash", "Hash", "02", "WORK"), ("disks", "Disks & partitions", "03", "WORK"),
            ("compare", "Compare", "04", "WORK"), ("cases", "Cases & custody", "05", "WORK"),
            ("tools", "Analysis tools", "06", "TOOLS"), ("sets", "Known hash sets", "07", "TOOLS"),
            ("settings", "Settings & about", "08", "SYSTEM"),
        };

        void DrawNav()
        {
            nav.Children.Clear();
            string g = null;
            string active = page switch { "case" or "ev" => "cases", _ => page };
            foreach (var p in Pages)
            {
                if (p.group != g) { g = p.group; nav.Children.Add(new Border { Padding = new Thickness(20, 14, 20, 4), Child = Ui.Tx(L.T(g), 11, true, new SolidColorBrush(Color.FromRgb(0x6d, 0x69, 0x5f))) }); }
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var n = Ui.Tx(p.num, 12, false, new SolidColorBrush(Color.FromRgb(0x77, 0x73, 0x6a)), true, false); n.Width = 24; n.Margin = new Thickness(0, 0, 8, 0); n.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(n);
                var ic = Ui.Icon(p.key, active == p.key ? Ui.Ink : Ui.Side, 19); ((FrameworkElement)ic).Margin = new Thickness(0, 0, 12, 0);
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = L.T(p.label), VerticalAlignment = VerticalAlignment.Center });
                var rb = new RadioButton { Style = (Style)Application.Current.Resources["Nav"], Content = sp, IsChecked = active == p.key, GroupName = "nav" };
                var key = p.key;
                rb.Checked += (_, _) => { if (page != key) Go(key); };
                nav.Children.Add(rb);
            }
        }

        public void ApplyLang()
        {
            L.Ar = Store.S.Lang != "en";
            FlowDirection = L.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Root.FlowDirection = FlowDirection;
            who.Text = string.IsNullOrWhiteSpace(Store.S.Examiner) ? "—" : Store.S.Examiner;
            adminTxt.Text = Native.IsAdmin() ? "● " + L.T("Running as administrator (disk access on)") : "○ " + L.T("Not administrator — disk reading is off");
            if (LangButtons != null) foreach (Button b in LangButtons.Children) b.Background = (string)b.Tag == Store.S.Lang ? Ui.Yellow : Ui.CardBg;
        }

        public void Head(string k, string t, params UIElement[] acts)
        {
            kicker.Text = k; title.Text = t;
            actions.Children.Clear();
            foreach (var a in acts) if (a != null) actions.Children.Add(a);
        }

        public void Go(string p, object a = null)
        {
            page = p; arg = a;
            DrawNav();
            FrameworkElement content;
            try
            {
                content = p switch
                {
                    "hash" => PageHash(a as string), "disks" => PageDisks(), "compare" => PageCompare(), "cases" => PageCases(),
                    "case" => PageCase((ValueTuple<CaseFile, string>)a), "ev" => PageEvidence((ValueTuple<CaseFile, int>)a), "tools" => PageTools(a as string),
                    "sets" => PageSets(), "settings" => PageSettings(), _ => PageHome()
                };
            }
            catch (Exception e) { content = Ui.Finding("critical", e.Message); }
            scroll.Content = new Border { Padding = new Thickness(34, 26, 34, 60), Child = content };
            scroll.ScrollToTop();
        }

        public static void Toast(string text, bool err = false)
        {
            if (I == null) return;
            var b = new Border { Background = Ui.Ink, BorderBrush = err ? Ui.Red : Ui.Yellow, BorderThickness = new Thickness(2), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 6, 0, 0), Child = Ui.Tx(text, 14, true, Ui.White) };
            I.toasts.Children.Add(b);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(err ? 6 : 2.6) };
            t.Tick += (_, _) => { t.Stop(); I.toasts.Children.Remove(b); };
            t.Start();
        }

        /// <summary>Runs work on a background thread with a live progress box; done() runs on the UI thread.</summary>
        public void RunJob<T>(Panel host, Func<Prog, T> work, Action<T> done, Action always = null)
        {
            var pb = Ui.Progress();
            host.Children.Clear(); host.Children.Add(pb.Root);
            var prog = new Prog();
            pb.Cancel.Click += (_, _) => prog.Cancel = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                double pct = Math.Min(100, prog.Pct);
                pb.Fill.Width = Math.Max(0, pb.Track.ActualWidth * pct / 100);
                pb.Pct.Text = $"{pct:0.0}%  ·  {Fmt.Bytes(prog.Done)} / {Fmt.Bytes(prog.Total)}";
                pb.Meta.Text = $"{L.T("files")} {prog.FilesDone}/{prog.FilesTotal}   {L.T("speed")} {Fmt.Bytes(prog.Speed)}/s   {L.T("elapsed")} {Fmt.Dur(prog.SW.Elapsed.TotalSeconds)}" +
                               (prog.Eta is double e ? $"   {L.T("remaining")} {Fmt.Dur(e)}" : "") + (prog.Bad > 0 ? $"   {L.T("unreadable sectors")} {prog.Bad}" : "") + $"\n{prog.Current}";
            };
            timer.Start();
            Task.Run(() => work(prog)).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                timer.Stop();
                host.Children.Clear();
                always?.Invoke();
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException();
                    host.Children.Add(Ui.Finding(ex is CancelledException ? "warning" : "critical", ex is CancelledException ? L.T("Cancelled.") : ex?.Message));
                    if (ex is UnauthorizedAccessException) host.Children.Add(AdminButton());
                }
                else
                {
                    try { done(t.Result); } catch (Exception e2) { host.Children.Add(Ui.Finding("critical", e2.Message)); }
                }
            }));
        }

        public FrameworkElement AdminButton() => Ui.Btn(L.T("Restart as administrator"), RestartAdmin, "dark", "shield");

        public static void RestartAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas", Arguments = App.DataArg };
                Process.Start(psi);
                Application.Current.Shutdown();
            }
            catch { Toast(L.T("Elevation was cancelled."), true); }
        }

        public static string PickFile(string filter = null)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = filter ?? L.T("All files") + "|*.*" };
            return d.ShowDialog() == true ? d.FileName : null;
        }
        public static string PickFolder()
        {
            var d = new Microsoft.Win32.OpenFolderDialog();
            return d.ShowDialog() == true ? d.FolderName : null;
        }

        /// <summary>Path box + browse buttons (file / folder).</summary>
        public static (FrameworkElement ui, TextBox box) PathPicker(string label, string value = "", bool file = true, bool folder = true, string filter = null)
        {
            var box = Ui.Input(value, true);
            var d = new DockPanel();
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            if (file) btns.Children.Add(Ui.Btn(L.T("File…"), () => { var p = PickFile(filter); if (p != null) box.Text = p; }, "small", "file"));
            if (folder) btns.Children.Add(Ui.Btn(L.T("Folder…"), () => { var p = PickFolder(); if (p != null) box.Text = p; }, "small", "folder"));
            DockPanel.SetDock(btns, Dock.Right);
            d.Children.Add(btns); d.Children.Add(box);
            return (Ui.Field(label, d), box);
        }

        public void SnapPng(string path, int w)
        {
            Content = null;
            Root.FlowDirection = L.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Root.Measure(new Size(w, double.PositiveInfinity));
            double h = Math.Max(900, Math.Min(6000, Root.DesiredSize.Height));
            Root.Measure(new Size(w, h)); Root.Arrange(new Rect(0, 0, w, h)); Root.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var rtb = new RenderTargetBitmap(w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(Root);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path); enc.Save(fs);
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
    }
}
