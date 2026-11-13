using System;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Basma
{
    public static class Fmt
    {
        public const string LRM = "‎";
        public static string Bytes(double n)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (n >= 1024 && i < u.Length - 1) { n /= 1024; i++; }
            return (i == 0 ? n.ToString("0", CultureInfo.InvariantCulture) : n.ToString("0.##", CultureInfo.InvariantCulture)) + " " + u[i];
        }
        public static string Ltr(string s) => LRM + s + LRM;
        public static string Utc(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        public static string NowUtc() => Utc(DateTime.UtcNow);
        public static string Dur(double s) => s < 60 ? s.ToString("0.0", CultureInfo.InvariantCulture) + " s" :
            TimeSpan.FromSeconds(s).ToString(s >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        public static DateTime? ParseUtc(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) return d;
            return null;
        }
        public static string LocalOf(string utc)
        {
            var d = ParseUtc(utc);
            return d == null ? "" : d.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        public static string Hex(byte[] b, int off = 0, int len = -1)
        {
            if (len < 0) len = b.Length - off;
            return Convert.ToHexString(b, off, len).ToLowerInvariant();
        }
    }

    public static class Js
    {
        public static readonly JsonSerializerOptions O = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
            WriteIndented = false,
        };
        public static readonly JsonSerializerOptions Pretty = new(O) { WriteIndented = true };
        public static string S<T>(T o) => JsonSerializer.Serialize(o, O);
        public static T D<T>(string s) => JsonSerializer.Deserialize<T>(s, O);
    }
}
