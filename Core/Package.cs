using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace SP2UIEditor
{
    // One saved edit. Targets are identities (see Identity in the plugin): "src:<file>#<place>" for an element of a layout, "add:<uid>" for an
    // added one, then "|step" for what is inside a template or made by code.
    public sealed class Op
    {
        public string Do;                                  // set, class, add, move, remove, panel, action
        public string Target;                              // the element (add: its parent)
        public string Name, Value;                         // set: attribute and value; class: Name
        public bool On = true;                             // class
        public string Parent;                              // move: the new parent
        public int Index = -1;                             // add, move
        public string Uid, Xml;                            // add
        public Dictionary<string, string> Args;            // action: what it needs (Name = its kind)
        public Dictionary<string, string> Texts;           // add: texts written into children, by id
        public Dictionary<string, string> Prints;          // what the layout elements named above looked like, to notice a change of the game
        public string Text;                                // for people

        [JsonIgnore] public string Orphan;                 // why it can't be applied any more
        [JsonIgnore] public int Applied;                   // to how many elements, so far

        public string Problem()
        {
            if (string.IsNullOrEmpty(Target)) return "no target";
            switch (Do)
            {
                case "set":
                    if (!SafeXml.AttributeAllowed(Name)) return "the attribute '" + Name + "' is not allowed";
                    return SafeXml.ValueAllowed(Value) ? null : "an expression is not allowed";
                case "class": return string.IsNullOrWhiteSpace(Name) || Name.Contains(" ") ? "a class name is needed" : null;
                case "move": return string.IsNullOrEmpty(Parent) ? "no parent" : null;
                case "remove": return null;
                case "panel":
                    if (!GoodUid(Uid) || Target != "panel:" + Uid) return "no uid";
                    if (string.IsNullOrWhiteSpace(Name) || !SafeXml.ValueAllowed(Name)) return "a title is needed";
                    return Value == null || Values.Problem("int", Value) == null ? null : "its width must be a whole number";
                case "action": return ActionProblem(Name, Args);
                case "style":
                    if (!Target.StartsWith("style:") || Target.Length < 7) return "no style";
                    return SafeXml.AttributeAllowed(Name) && SafeXml.ValueAllowed(Value) && Value != null ? null : "that value is not allowed";
                case "constant":
                    if (!Target.StartsWith("const:") || Target.Length < 7 || !Target.Substring(6).All(char.IsLetterOrDigit) || Name != "Value") return "no constant";
                    return Value != null && SafeXml.ValueAllowed(Value) ? null : "that value is not allowed";
                case "add":
                    if (!GoodUid(Uid)) return "no uid";
                    try { return SafeXml.Problem(XElement.Parse(Xml ?? "")); }
                    catch (Exception) { return "its XML can't be read"; }
            }
            return "unknown edit '" + Do + "'";
        }

        private static bool GoodUid(string uid) { return !string.IsNullOrEmpty(uid) && uid.All(char.IsLetterOrDigit); }

        public static bool GoodLink(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https") && string.IsNullOrEmpty(u.UserInfo);
        }

        // The kinds of action there are; null = good.
        public static string ActionProblem(string kind, Dictionary<string, string> args)
        {
            Func<string, string> a = n => { string v; return args != null && args.TryGetValue(n, out v) ? v : null; };
            switch (kind)
            {
                case "none": return null;
                case "panel": return GoodUid(a("panel")) ? null : "choose a panel";
                case "element":
                    if (string.IsNullOrEmpty(a("target"))) return "choose an element";
                    return new[] { "show", "hide", "toggle" }.Contains(a("how")) ? null : "show, hide or toggle";
                case "link": return GoodLink(a("url")) ? null : "the address must begin with http:// or https://";
                case "config":
                    if (string.IsNullOrEmpty(a("plugin")) || string.IsNullOrEmpty(a("section")) || string.IsNullOrEmpty(a("key"))) return "choose a setting";
                    return Limits(a);
                case "variable":
                    string n2 = a("name");
                    if (string.IsNullOrEmpty(n2) || n2.Length > 40 || !(char.IsLetter(n2[0]) || n2[0] == '_') || !n2.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_')))
                        return "a variable's name has only letters, digits and _";
                    return Limits(a);
                case "method":
                    string sig = a("method") ?? "";
                    int paren = sig.IndexOf('(');
                    if (!Calls.Roots.Contains(a("root")) || paren < 1 || !Calls.NameAllowed(sig.Substring(0, paren))) return "that method may not be called";
                    return args.Keys.All(k => new[] { "root", "method", "a0", "a1", "a2" }.Contains(k)) ? null : "unknown argument";
                case "mod": return string.IsNullOrEmpty(a("name")) ? "choose an action" : null;
            }
            return "unknown action '" + kind + "'";
        }

        private static string Limits(Func<string, string> a)
        {
            foreach (string n in new[] { "min", "max" }) if (a(n) != null && Values.Problem("float", a(n)) != null) return n + " must be a number";
            return null;
        }

        // Layout files named by the edit.
        public IEnumerable<string> Bases()
        {
            foreach (string t in new[] { Target, Parent })
                if (t != null && t.StartsWith("src:")) yield return BaseOf(t);
        }

        public static string BaseOf(string identity)
        {
            int bar = identity.IndexOf('|');
            return bar < 0 ? identity : identity.Substring(0, bar);
        }

        public static bool Under(string identity, string of)
        {
            return identity != null && (identity == of || identity.StartsWith(of + "|"));
        }

        // How an element of a layout is recognized: kind, id and the words it was written with.
        public static string Print(XElement e)
        {
            return e.Name.LocalName + ";" + (string)e.Attribute("id") + ";" + ((string)e.Attribute("text") ?? (string)e.Attribute("title") ?? (string)e.Attribute("templateId") ?? "").Replace(';', ',')
                 + ";" + string.Join(",", e.Descendants().Select(x => (string)x.Attribute("id")).Where(x => !string.IsNullOrEmpty(x)).Take(12));
        }

        // The same element? One without id or words is told by the ids inside it (a mod may have added or taken some: one in common is enough).
        public static bool Same(string saved, string now)
        {
            if (saved == null) return true;
            string[] a = saved.Split(';'), b = now.Split(';');
            for (int i = 0; i < 3; i++) if ((i < a.Length ? a[i] : "") != (i < b.Length ? b[i] : "")) return false;
            if (a.Length < 4) return true;
            string[] x = a[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), y = b[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return x.Length == 0 || x.Intersect(y).Any();
        }

        // Where the element a base identity names is now: the same place if it still looks the same, else the only element with its id. Null = lost.
        public static string Locate(string baseIdentity, string print, XElement raw)
        {
            string src = baseIdentity.Substring(4);
            XElement e = Sources.Find(raw, src);
            if (e != null && Same(print, Print(e))) return baseIdentity;
            string[] p = (print ?? "").Split(';');
            if (p.Length < 2 || (p[1].Length == 0 && (p.Length < 4 || p[3].Length == 0))) return null;
            List<XElement> same = raw.Descendants().Where(x => (p[1].Length > 0 ? (string)x.Attribute("id") == p[1] && x.Name.LocalName == p[0] : Same(print, Print(x))) && !Sources.InTemplate(x)).ToList();
            if (same.Count != 1) return null;
            List<int> place = new List<int>();
            for (XElement at = same[0]; at.Parent != null; at = at.Parent) place.Insert(0, at.ElementsBeforeSelf().Count());
            return "src:" + Sources.FileOf(src) + "#" + string.Join(".", place);
        }
    }

    public sealed class Package
    {
        public int Format = 1;
        public string Name;
        public string Author;
        public string GameVersion;
        public List<Op> Ops = new List<Op>();

        [JsonIgnore] public string Path;
        [JsonIgnore] public List<Op> Base;                 // what it held before the changes of this session
        [JsonIgnore] public List<string> Refused = new List<string>();

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 16,
        };

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.Indented, Settings); }

        public static Package FromJson(string json)
        {
            Package p = JsonConvert.DeserializeObject<Package>(json, Settings);
            if (p == null) throw new Exception("it is empty");
            if (p.Format != 1) throw new Exception("it was made by a newer version of the editor (format " + p.Format + ")");
            List<Op> good = new List<Op>();
            foreach (Op op in p.Ops ?? new List<Op>())
            {
                string problem = op == null ? "empty edit" : op.Problem();
                if (problem == null) good.Add(op); else p.Refused.Add((op != null ? op.Text + ": " : "") + problem);
            }
            p.Ops = good;
            return p;
        }

        public void Write(string path)
        {
            // through a second file, so a crash never leaves half a package
            string temp = path + ".tmp";
            File.WriteAllText(temp, ToJson());
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            Path = path;
        }

        // What is left to do after all of them: a later value of the same thing replaces the earlier one, and an added element that was removed
        // takes everything done to it along.
        public static List<Op> Compact(IEnumerable<Op> ops)
        {
            List<Op> list = new List<Op>();
            foreach (Op op in ops)
            {
                if (op.Do == "remove")
                {
                    // also what was added inside it, and what that held
                    List<string> gone = new List<string> { op.Target };
                    bool more = true;
                    while (more)
                    {
                        more = false;
                        foreach (Op o in list)
                            if (o.Do == "add" && !gone.Contains("add:" + o.Uid) && gone.Any(g => Within(o.Target, g))) { gone.Add("add:" + o.Uid); more = true; }
                    }
                    list.RemoveAll(o => gone.Any(g => Within(o.Target, g) || Within(o.Parent, g) || OpensPanel(o, g) || (o.Do == "add" && "add:" + o.Uid == g)));
                    continue;
                }
                if (op.Do == "set" || op.Do == "class" || op.Do == "style" || op.Do == "constant") list.RemoveAll(o => o.Do == op.Do && o.Target == op.Target && o.Name == op.Name);
                if (op.Do == "action") list.RemoveAll(o => o.Do == "action" && o.Target == op.Target);
                if (op.Do == "action" && op.Name == "none") continue;
                if (op.Do == "panel") list.RemoveAll(o => o.Do == "panel" && o.Uid == op.Uid);
                if (op.Do == "move") list.RemoveAll(o => o.Do == "move" && o.Target == op.Target);
                list.Add(op);
            }
            return list;
        }

        // In what was removed: an added element, or anything in a panel.
        private static bool Within(string identity, string gone)
        {
            if (identity == null) return false;
            return Op.Under(identity, gone) || (gone.StartsWith("panel:") && identity.StartsWith("src:SP2UI/" + gone.Substring(6) + "#"));
        }

        private static bool OpensPanel(Op o, string gone)
        {
            string p;
            return o.Do == "action" && gone.StartsWith("panel:") && o.Args != null && o.Args.TryGetValue("panel", out p) && o.Name == "panel" && "panel:" + p == gone;
        }

        // Two packages that want different things of the same attribute.
        public static List<string> Conflicts(IList<Package> inOrder)
        {
            List<string> found = new List<string>();
            Dictionary<string, KeyValuePair<Package, Op>> seen = new Dictionary<string, KeyValuePair<Package, Op>>();
            foreach (Package p in inOrder)
                foreach (Op op in p.Ops)
                {
                    if (op.Do != "set" && op.Do != "move" && op.Do != "style" && op.Do != "constant") continue;
                    string key = op.Do + " " + op.Target + " " + op.Name;
                    KeyValuePair<Package, Op> other;
                    if (seen.TryGetValue(key, out other) && other.Key != p && (other.Value.Value != op.Value || other.Value.Parent != op.Parent || other.Value.Index != op.Index))
                        found.Add(p.Name + " replaces " + other.Key.Name + ": " + op.Text);
                    seen[key] = new KeyValuePair<Package, Op>(p, op);
                }
            return found;
        }

        public static string SafeFileName(string name)
        {
            string s = new string((name ?? "").Trim().Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' ? c : '_').ToArray()).Trim();
            return s.Length == 0 ? null : s.Length > 60 ? s.Substring(0, 60) : s;
        }
    }
}
