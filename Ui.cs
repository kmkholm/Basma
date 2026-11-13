using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Basma
{
    public sealed class GridRow
    {
        public string[] C; public object Tag;
        public string this[int i] => i < C.Length ? C[i] : "";
        public GridRow(object tag, params string[] c) { Tag = tag; C = c; }
    }

    public static class Ui
    {
        static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        public static readonly SolidColorBrush Ink = B("#111111"), Paper = B("#ECE6D8"), CardBg = B("#FBF8F1"), Card2 = B("#F4EFE3"), Orange = B("#FF4D00"),
            Yellow = B("#FFD400"), Green = B("#0B7A33"), Red = B("#C1121F"), Amber = B("#B35C00"), Blue = B("#1F4E8C"), Muted = B("#5D5A52"),
            White = B("#FFFFFF"), OkBg = B("#D9F2E0"), BadBg = B("#FFD9D9"), WarnBg = B("#FFF1BF"), Side = B("#DDD8CB");
        public static readonly FontFamily Sans = new("Segoe UI"), Mono = new("Cascadia Mono, Consolas, Courier New");

        public static TextBlock Tx(string s, double size = 14, bool bold = false, Brush fg = null, bool mono = false, bool wrap = true)
        {
            var t = new TextBlock { Text = s ?? "", FontSize = size, Foreground = fg ?? Ink, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
            if (bold) t.FontWeight = FontWeights.Bold;
            if (mono) { t.FontFamily = Mono; t.FlowDirection = FlowDirection.LeftToRight; }
            return t;
        }

        public static TextBox Sel(string s, double size = 13, bool mono = true)
        {
            var t = new TextBox { Text = s ?? "", IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = size, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0) };
            if (mono) { t.FontFamily = Mono; t.FlowDirection = FlowDirection.LeftToRight; }
            return t;
        }

        public static Button Btn(string text, Action click, string kind = "plain", string icon = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) sp.Children.Add(new Border { Child = Icon(icon, kind == "dark" ? White : Ink, 16), Margin = new Thickness(0, 0, 8, 0) });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp };
            b.Background = kind switch { "primary" => Orange, "dark" => Ink, "yellow" => Yellow, _ => CardBg };
            if (kind == "dark") b.Foreground = White;
            if (kind == "small") { b.Padding = new Thickness(9, 4, 9, 4); b.FontSize = 12.5; }
            b.Click += (_, _) => click?.Invoke();
            return b;
        }

        public static Border Card2Box(UIElement child, Brush bg = null, double pad = 14) =>
            new() { Background = bg ?? White, BorderBrush = Ink, BorderThickness = new Thickness(2), Padding = new Thickness(pad), Child = child };

        public static FrameworkElement Card(string title, string num, params UIElement[] content)
        {
            var sp = new StackPanel();
            if (title != null)
            {
                var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                if (num != null) h.Children.Add(new Border { Background = Ink, Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Child = Tx(num, 12, true, Yellow, true) });
                h.Children.Add(Tx(title, 18, true));
                sp.Children.Add(h);
            }
            foreach (var c in content) if (c != null) sp.Children.Add(c);
            var g = new Grid { Margin = new Thickness(0, 0, 6, 22) };
            g.Children.Add(new Border { Background = Ink, Margin = new Thickness(6, 6, -6, -6) });
            g.Children.Add(new Border { Background = CardBg, BorderBrush = Ink, BorderThickness = new Thickness(2), Padding = new Thickness(20), Child = sp });
            return g;
        }

        public static FrameworkElement Section(string title, string num)
        {
            var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
            h.Children.Add(new Border { Background = Orange, BorderBrush = Ink, BorderThickness = new Thickness(2), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Child = Tx(num, 13, true, Ink, true) });
            h.Children.Add(Tx(title, 22, true));
            return h;
        }

        public static TextBox Input(string text = "", bool mono = false, bool multi = false)
        {
            var t = new TextBox { Text = text ?? "" };
            if (mono) { t.FontFamily = Mono; t.FlowDirection = FlowDirection.LeftToRight; }
            if (multi) { t.AcceptsReturn = true; t.TextWrapping = TextWrapping.Wrap; t.MinHeight = 90; t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            return t;
        }

        public static FrameworkElement Field(string label, UIElement input, string hint = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 14, 12) };
            sp.Children.Add(Tx(label, 12.5, true));
            sp.Children.Add(new Border { Height = 4 });
            sp.Children.Add(input);
            if (hint != null) sp.Children.Add(Tx(hint, 11.5, false, Muted));
            return sp;
        }

        public static ComboBox Combo(IEnumerable<(string val, string label)> items, string sel = null)
        {
            var c = new ComboBox();
            foreach (var (v, l) in items) c.Items.Add(new ComboBoxItem { Content = l, Tag = v });
            c.SelectedIndex = 0;
            if (sel != null) for (int i = 0; i < c.Items.Count; i++) if ((string)((ComboBoxItem)c.Items[i]).Tag == sel) c.SelectedIndex = i;
            return c;
        }
        public static string Val(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string;

        public static CheckBox Chk(string text, bool on = false) => new() { Content = text, IsChecked = on };

        public static WrapPanel Row(params UIElement[] items)
        {
            var w = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) if (i != null) w.Children.Add(i);
            return w;
        }

        public static UniformGrid Cols(int n, params UIElement[] items)
        {
            var g = new UniformGrid { Columns = n };
            foreach (var i in items) if (i != null) g.Children.Add(i);
            return g;
        }

        public static Grid Split(UIElement a, UIElement b, double ra = 1.35, double rb = 1)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ra, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rb, GridUnitType.Star) });
            Grid.SetColumn((FrameworkElement)a, 0); Grid.SetColumn((FrameworkElement)b, 2);
            g.Children.Add(a); g.Children.Add(b);
            return g;
        }

        public static (string text, Brush color) StatusOf(string st) => st switch
        {
            "INTACT" => (L.T("INTACT"), Green), "MATCH" => (L.T("MATCH"), Green), "IDENTICAL" => (L.T("IDENTICAL"), Green),
            "BROKEN" => (L.T("BROKEN"), Red), "MISMATCH" => (L.T("MISMATCH"), Red), "DIFFERENT" => (L.T("DIFFERENT"), Red),
            "WARNINGS" => (L.T("WARNINGS"), Amber), "BASELINE" => (L.T("BASELINE"), Blue), _ => (L.T(st ?? "—"), Muted)
        };

        public static Border Stamp(string status, double size = 14)
        {
            var (t, c) = StatusOf(status);
            return new Border
            {
                BorderBrush = c, BorderThickness = new Thickness(3), Padding = new Thickness(9, 1, 9, 2), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, RenderTransform = new RotateTransform(-3), RenderTransformOrigin = new Point(.5, .5),
                Child = new TextBlock { Text = t, Foreground = c, FontWeight = FontWeights.Black, FontSize = size }
            };
        }

        public static Border Tag(string text, Brush bg = null, Brush fg = null) =>
            new() { Background = bg ?? White, BorderBrush = Ink, BorderThickness = new Thickness(2), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(0, 0, 6, 6), Child = Tx(text, 12, true, fg ?? Ink) };

        public static Border Finding(string level, string text)
        {
            var c = level switch { "critical" => Red, "warning" => Orange, _ => Blue };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var d = new DockPanel();
            var lab = Tx(level.ToUpperInvariant(), 11, true, c, true, false); lab.Margin = new Thickness(0, 2, 8, 0); lab.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(lab, Dock.Left);
            d.Children.Add(lab); d.Children.Add(Tx(text, 13.5));
            return new Border { BorderBrush = Ink, BorderThickness = new Thickness(2), Background = White, Margin = new Thickness(0, 0, 0, 8), Child = new Border { BorderBrush = c, BorderThickness = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 7, 10, 7), Child = d } };
        }

        public static Border Verdict(string status, string big, params UIElement[] details)
        {
            var bg = status switch { "ok" => OkBg, "bad" => BadBg, _ => WarnBg };
            var d = new DockPanel();
            var bigT = Tx(big, 30, true, Ink, false, false); bigT.Margin = new Thickness(0, 0, 18, 0); bigT.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(bigT, Dock.Left);
            d.Children.Add(bigT);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var x in details) sp.Children.Add(x);
            d.Children.Add(sp);
            return new Border { Background = bg, BorderBrush = Ink, BorderThickness = new Thickness(3), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10), Child = d };
        }

        public static FrameworkElement HashLine(string algo, string value, Brush bg = null)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var a = Tx(algo, 13, true, Ink, true, false); a.VerticalAlignment = VerticalAlignment.Center;
            var v = Sel(value, 14); v.VerticalAlignment = VerticalAlignment.Center;
            var cp = Btn(L.T("Copy"), () => { try { Clipboard.SetText(value ?? ""); MainWindow.Toast(L.T("Copied")); } catch { } }, "small");
            cp.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(v, 1); Grid.SetColumn(cp, 2);
            g.Children.Add(a); g.Children.Add(v); g.Children.Add(cp);
            return new Border { Background = bg ?? White, BorderBrush = Ink, BorderThickness = new Thickness(2), Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 0, 0, 6), Child = g };
        }

        public sealed class ProgressBox
        {
            public FrameworkElement Root; public Border Fill; public TextBlock Pct, Meta; public Button Cancel; public Grid Track;
        }

        public static ProgressBox Progress()
        {
            var p = new ProgressBox();
            p.Track = new Grid { Height = 28 };
            p.Fill = new Border { Background = Orange, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            p.Pct = Tx("0%", 12.5, true, Ink, true, false); p.Pct.HorizontalAlignment = HorizontalAlignment.Center; p.Pct.VerticalAlignment = VerticalAlignment.Center;
            p.Track.Children.Add(p.Fill); p.Track.Children.Add(p.Pct);
            p.Meta = Tx("", 12, false, Muted, true);
            p.Meta.Margin = new Thickness(0, 6, 0, 6);
            p.Cancel = Btn(L.T("Stop"), null, "small");
            var sp = new StackPanel();
            sp.Children.Add(new Border { BorderBrush = Ink, BorderThickness = new Thickness(2), Background = White, Child = p.Track });
            sp.Children.Add(p.Meta);
            sp.Children.Add(Row(p.Cancel));
            p.Root = sp;
            return p;
        }

        public static DataGrid Table(string[] headers, IEnumerable<GridRow> rows, int[] monoCols = null, Action<GridRow> onOpen = null, double[] widths = null)
        {
            var dg = new DataGrid();
            for (int i = 0; i < headers.Length; i++)
            {
                var col = new DataGridTextColumn { Header = headers[i], Binding = new Binding($"[{i}]") };
                var st = new Style(typeof(TextBlock));
                st.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                if (monoCols != null && monoCols.Contains(i)) { st.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Mono)); st.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight)); }
                col.ElementStyle = st;
                if (widths != null && i < widths.Length) col.Width = widths[i] < 0 ? new DataGridLength(-widths[i], DataGridLengthUnitType.Star) : new DataGridLength(widths[i]);
                dg.Columns.Add(col);
            }
            dg.ItemsSource = rows.ToList();
            if (onOpen != null) dg.MouseDoubleClick += (_, _) => { if (dg.SelectedItem is GridRow r) onOpen(r); };
            return dg;
        }

        public static readonly Dictionary<string, string> Paths = new()
        {
            ["home"] = "M3,11 L12,4 L21,11 M5,9 L5,20 L19,20 L19,9",
            ["hash"] = "M9,3 L7,21 M17,3 L15,21 M4,8 L21,8 M3,16 L20,16",
            ["disk"] = "M3,6 L21,6 L21,18 L3,18 Z M7,14 L7,14.1 M11,14 L17,14",
            ["disks"] = "M3,6 L21,6 L21,18 L3,18 Z M7,14 L7,14.1 M11,14 L17,14",
            ["compare"] = "M4,7 L16,7 M12,3 L16,7 L12,11 M20,17 L8,17 M12,13 L8,17 L12,21",
            ["tools"] = "M4,20 L12,12 M14,4 A4,4 0 1 0 20,10 L17,10 L14,7 Z",
            ["sets"] = "M4,5 L20,5 M4,10 L20,10 M4,15 L10,15 M4,20 L10,20 M14,18 L16,20 L21,15",
            ["cases"] = "M3,6 L10,6 L12,8 L21,8 L21,20 L3,20 Z M3,11 L21,11",
            ["settings"] = "M4,7 L14,7 M18,7 L20,7 M4,17 L6,17 M10,17 L20,17 M14,5 L18,5 L18,9 L14,9 Z M6,15 L10,15 L10,19 L6,19 Z",
            ["plus"] = "M12,4 L12,20 M4,12 L20,12",
            ["file"] = "M6,3 L14,3 L18,7 L18,21 L6,21 Z M14,3 L14,7 L18,7",
            ["folder"] = "M3,6 L10,6 L12,8 L21,8 L21,19 L3,19 Z",
            ["play"] = "M7,4 L20,12 L7,20 Z",
            ["print"] = "M6,9 L6,3 L18,3 L18,9 M6,18 L3,18 L3,10 L21,10 L21,18 L18,18 M6,14 L18,14 L18,21 L6,21 Z",
            ["back"] = "M10,6 L4,12 L10,18 M4,12 L20,12",
            ["shield"] = "M12,3 L4,6 L4,12 C4,17 8,20 12,21 C16,20 20,17 20,12 L20,6 Z M8,12 L11,15 L16,9",
            ["box"] = "M3,7 L12,3 L21,7 L21,17 L12,21 L3,17 Z M3,7 L12,11 L21,7 M12,11 L12,21",
            ["eye"] = "M2,12 C5,6 19,6 22,12 C19,18 5,18 2,12 Z M12,9 A3,3 0 1 0 12.01,9",
        };

        public static FrameworkElement Icon(string name, Brush fg, double size)
        {
            if (!Paths.TryGetValue(name, out var d)) return new Border();
            return new Path
            {
                Data = Geometry.Parse(d), Stroke = fg, StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square,
                Width = size, Height = size, Stretch = Stretch.Uniform, Fill = name == "play" ? fg : null, VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight
            };
        }

        public static FrameworkElement Num(string value, string label, Brush bg = null, Brush fg = null)
        {
            var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            sp.Children.Add(Tx(value, 52, true, fg ?? Ink, true, false));
            sp.Children.Add(Tx(label, 12.5, true, fg ?? Muted));
            return new Border { Background = bg ?? CardBg, BorderBrush = Ink, BorderThickness = new Thickness(0, 0, 2, 0), Child = sp };
        }
    }
}
