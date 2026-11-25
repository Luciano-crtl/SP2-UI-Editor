using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SP2UIEditor
{
    // Which methods of the game a button may call, and how an approval is remembered.
    public static class Calls
    {
        public static readonly string[] Roots = { "game", "ui", "flight", "flightUI", "designer", "designerUI", "script" };
        public const int MaxArgs = 3;

        // Names that touch files, accounts, the network, other players or the whole program. Seen anywhere in the name, as a word of it.
        private static readonly string[] Never =
        {
            "save", "delete", "erase", "file", "files", "path", "directory", "folder", "upload", "download", "login", "logout", "account", "password",
            "token", "web", "http", "url", "link", "publish", "share", "post", "purchase", "buy", "process", "exec", "execute", "run", "script",
            "quit", "exit", "shutdown", "restart", "kick", "ban", "vote", "report", "chat", "message", "send", "rpc", "server", "client", "host", "connect",
            "disconnect", "network", "register", "unregister", "invoke", "reflection", "assembly", "type", "import", "export", "xml", "resource",
            "destroy", "reset", "clear", "clean", "wipe", "command", "mod", "mods", "steam", "dispose",
        };

        // "SaveCraftXml" -> save, craft, xml
        public static List<string> Words(string name)
        {
            List<string> words = new List<string>();
            StringBuilder w = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool starts = char.IsUpper(c) && (i == 0 || !char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if ((starts || !char.IsLetter(c)) && w.Length > 0) { words.Add(w.ToString().ToLowerInvariant()); w.Length = 0; }
                if (char.IsLetter(c)) w.Append(c);
            }
            if (w.Length > 0) words.Add(w.ToString().ToLowerInvariant());
            return words;
        }

        public static bool NameAllowed(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 80 && name.All(c => char.IsLetterOrDigit(c) && c < 128) && !Words(name).Any(Never.Contains);
        }

        private static bool Simple(Type t) { return t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(string) || t.IsEnum; }

        public static bool Allowed(MethodInfo m)
        {
            string space = m.DeclaringType != null ? m.DeclaringType.Namespace : null;
            return m.IsPublic && !m.IsStatic && !m.IsSpecialName && !m.IsGenericMethod && m.ReturnType == typeof(void)
                && space != null && (space == "Assets.Scripts" || space.StartsWith("Assets.Scripts."))
                && NameAllowed(m.Name) && m.GetParameters().Length <= MaxArgs && m.GetParameters().All(p => Simple(p.ParameterType) && !p.IsOut && !p.ParameterType.IsByRef);
        }

        public static List<MethodInfo> Of(Type type)
        {
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(Allowed).OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length).ToList();
        }

        public static string Signature(MethodInfo m)
        {
            return m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Short(p.ParameterType) + " " + p.Name)) + ")";
        }

        private static string Short(Type t) { return t == typeof(bool) ? "bool" : t == typeof(int) ? "int" : t == typeof(float) ? "float" : t == typeof(string) ? "string" : t.Name; }

        // Throws with a reason when a text is not a value for the parameter.
        public static object Convert(string text, ParameterInfo p)
        {
            Type t = p.ParameterType;
            if (text == null && p.HasDefaultValue) return p.DefaultValue;
            if (t == typeof(string)) return text ?? "";
            if (t == typeof(bool)) return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
            if (t.IsEnum) return Enum.Parse(t, text ?? "", true);
            if (Values.Problem(t == typeof(int) ? "int" : "float", text) != null) throw new Exception(p.Name + " needs a number");
            return t == typeof(int) ? (object)int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        // What an approval stands for: this call, on this element, of this package. Any change of it asks again.
        public static string Fingerprint(string package, string target, Dictionary<string, string> args)
        {
            string s = package + "\n" + target + "\n" + string.Join("\n", args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + a.Value.Length + ":" + a.Value));
            using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }
    }
}
