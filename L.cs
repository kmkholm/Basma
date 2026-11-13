using System.Collections.Generic;
using System.Linq;

namespace Basma
{
    /// <summary>UI strings: keys are the English text; Arabic lives in <see cref="A"/>.</summary>
    public static partial class L
    {
        public static bool Ar = true;
        public static readonly HashSet<string> Missing = new();
        public static string T(string key)
        {
            if (key == null) return "";
            if (!Ar) return key;
            if (A.TryGetValue(key, out var v)) return v;
            if (key.Length > 0 && key.Any(char.IsLetter) && !key.Any(c => c >= 0x0600 && c <= 0x06FF)) lock (Missing) Missing.Add(key);
            return key;
        }
        public static readonly Dictionary<string, string> A = new();
    }
}
