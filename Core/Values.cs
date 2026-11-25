using System;
using System.Globalization;
using System.Linq;

namespace SP2UIEditor
{
    // The game takes a value it can't read without a word (a wrong color becomes white, a wrong number 0). The editor says so instead.
    public static class Values
    {
        private static readonly string[] Colors = { "none", "red", "green", "blue", "white", "black", "yellow", "cyan", "magenta", "gray", "grey", "clear" };
        private static readonly char[] Separators = { ' ', ',' };

        // kind: as the editor names the game's attributes (bool, color, float, int, vector2, rectoffset...). Null = good, else why not.
        public static string Problem(string kind, string value)
        {
            string v = (value ?? "").Trim();
            string target = v;
            // "1.2:OutBack 0.25" = the value, then how to animate to it
            if ((kind == "color" || kind == "float" || kind == "vector2") && v.Contains(":")) target = v.Split(':')[0].Trim();
            string[] parts = target.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            switch (kind)
            {
                case "bool": return new[] { "true", "false", "1", "0" }.Contains(v.ToLowerInvariant()) ? null : "it must be true or false";
                case "float": return parts.Length == 1 && Number(parts[0]) ? null : "it must be a number (with a point: 1.5)";
                case "int": return parts.Length == 1 && Whole(parts[0]) ? null : "it must be a whole number";
                case "vector2": return (parts.Length == 1 || parts.Length == 2) && parts.All(Number) ? null : "it must be two numbers: x y";
                case "rectoffset": return (parts.Length == 1 || parts.Length == 2 || parts.Length == 4) && parts.All(Whole) ? null : "it must be 1, 2 or 4 whole numbers: top right bottom left";
                case "color": return Color(target) ? null : "it must be a color: #RRGGBB, #RRGGBBAA, a @Constant or a name such as white";
            }
            return null;
        }

        private static bool Number(string s)
        {
            float f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !float.IsNaN(f) && !float.IsInfinity(f);
        }

        private static bool Whole(string s)
        {
            int i;
            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out i);
        }

        private static bool Color(string s)
        {
            if (!s.StartsWith("#")) return Colors.Contains(s.ToLowerInvariant());
            string hex = s.Substring(1);
            return (hex.Length == 6 || hex.Length == 8) && hex.All(Uri.IsHexDigit);
        }
    }
}
