using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SP2UIEditor
{
    // A package as XML and C# that a mod can carry without the editor.
    public sealed class Exporter
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public readonly List<string> Left = new List<string>();     // what could not be exported

        private readonly List<Op> ops;
        private readonly HashSet<Op> done = new HashSet<Op>();

        public static Exporter Run(Package package, Func<Op, XElement> panelLayout)
        {
            Exporter e = new Exporter(Package.Compact(package.Ops));
            e.Write(package.Name, panelLayout);
            return e;
        }

        private Exporter(List<Op> ops) { this.ops = ops; }

        private static string Ident(string s)
        {
            string id = new string((s ?? "").Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray());
            return id.Length == 0 ? "Ui" : char.IsDigit(id[0]) ? "Ui" + id : id;
        }

        private static string Lit(string s)
        {
            if (s == null) return "null";
            StringBuilder b = new StringBuilder("\"");
            foreach (char c in s)
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < 32 || c > 126) b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            return b.Append('"').ToString();
        }

        // An added element with all that was done to it and added inside it.
        private XElement Build(Op add)
        {
            done.Add(add);
            XElement e = XElement.Parse(add.Xml);
            string me = "add:" + add.Uid;
            foreach (Op o in ops.Where(x => x != add && Op.Under(x.Target, me)).ToList())
            {
                XElement at = o.Target == me ? e : Inside(e, o.Target.Substring(me.Length + 1));
                if (at == null || (at.Name.LocalName == "Instance" && o.Do != "action")) continue;
                if (o.Do == "set") { at.SetAttributeValue(o.Name, o.Value); done.Add(o); }
                else if (o.Do == "class") { Class(at, o); done.Add(o); }
                else if (o.Do == "add") { Insert(at, Build(o), o.Index); }
                else if (o.Do == "action") { if (at.Attribute("id") == null) at.SetAttributeValue("id", "ui-" + add.Uid + (at == e ? "" : "-" + e.Descendants().ToList().IndexOf(at))); actionIds[o] = (string)at.Attribute("id"); }
            }
            return e;
        }

        private readonly Dictionary<Op, string> actionIds = new Dictionary<Op, string>();

        private static void Class(XElement e, Op o)
        {
            List<string> c = ((string)e.Attribute("class") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != o.Name).ToList();
            if (o.On) c.Add(o.Name);
            e.SetAttributeValue("class", c.Count == 0 ? null : string.Join(" ", c));
        }

        private static void Insert(XElement parent, XElement child, int index)
        {
            XElement before = index >= 0 ? parent.Elements().ElementAtOrDefault(index) : null;
            if (before != null) before.AddBeforeSelf(child); else parent.Add(child);
        }

        // "#id" and "n:Type[:nth]" steps, in plain XML.
        private static XElement Inside(XElement e, string steps)
        {
            foreach (string step in steps.Split('|'))
            {
                if (e == null || e.Name.LocalName == "Instance") return null;
                string[] p = step.Split(':');
                int n;
                if (step.StartsWith("#")) e = e.Elements().Where(x => (string)x.Attribute("id") == p[0].Substring(1)).ElementAtOrDefault(p.Length > 1 && int.TryParse(p[1], out n) ? n : 0);
                else if (p[0] == "n" && p.Length > 1) e = e.Elements().Where(x => x.Name.LocalName == p[1] && x.Attribute("id") == null).ElementAtOrDefault(p.Length > 2 && int.TryParse(p[2], out n) ? n : 0);
                else return null;
            }
            return e;
        }

        private void Write(string name, Func<Op, XElement> panelLayout)
        {
            string space = Ident(name) + "Ui";
            StringBuilder layouts = new StringBuilder(), edits = new StringBuilder(), styles = new StringBuilder(), wiring = new StringBuilder();

            foreach (Op panel in ops.Where(o => o.Do == "panel").ToList())
            {
                done.Add(panel);
                XElement layout = panelLayout(panel);
                layout.Descendants().First(x => (string)x.Attribute("script") != null).SetAttributeValue("script", space + ".ExportedPanel");
                string file = "src:SP2UI/" + panel.Uid + "#";
                XElement content = layout.Descendants().First(x => (string)x.Attribute("id") == "sp2ui-content");
                foreach (Op o in ops.Where(x => x.Target != null && x.Target.StartsWith(file)).ToList())
                {
                    XElement at = o.Target.IndexOf('|') >= 0 ? null : Sources.Find(layout, o.Target.Substring(4));
                    if (at == null) continue;
                    if (o.Do == "add") Insert(at, Build(o), o.Index);
                    else if (o.Do == "set" && at.Name.LocalName != "Instance") { at.SetAttributeValue(o.Name, o.Value); done.Add(o); }
                    else if (o.Do == "class" && at.Name.LocalName != "Instance") { Class(at, o); done.Add(o); }
                }
                Files["Panel_" + Ident(panel.Name) + "_" + panel.Uid + ".xml"] = layout.ToString();
                layouts.Append("            { ").Append(Lit("Mods/" + space + "/" + panel.Uid)).Append(", ").Append(Lit(layout.ToString(SaveOptions.DisableFormatting))).Append(" },\r\n");
            }

            foreach (Op o in ops)
            {
                if (done.Contains(o)) continue;
                if (o.Do == "style" || o.Do == "constant")
                {
                    styles.Append("            { ").Append(Lit(o.Target + " " + o.Name)).Append(", ").Append(Lit(o.Value)).Append(" },\r\n");
                    done.Add(o);
                    continue;
                }
                if (o.Target == null || !o.Target.StartsWith("src:") || o.Target.IndexOf('|') >= 0 || o.Target.StartsWith("src:SP2UI/")) continue;
                string src = o.Target.Substring(4), path = Sources.FileOf(src), place = src.Substring(src.LastIndexOf('#') + 1), print = null;
                if (o.Prints != null) o.Prints.TryGetValue(o.Target, out print);
                string[] p = (print ?? ";;").Split(';');
                if (p[0] == "Instance" && o.Do != "add" && o.Do != "move") continue;
                string find = "Find(root, " + Lit(place) + ", " + Lit(p[0]) + ", " + Lit(p.Length > 1 ? p[1] : "") + ")";
                string line = null;
                if (o.Do == "set") line = "Set(" + find + ", " + Lit(o.Name) + ", " + Lit(o.Value) + ");";
                else if (o.Do == "class") line = "Class(" + find + ", " + Lit(o.Name) + ", " + (o.On ? "true" : "false") + ");";
                else if (o.Do == "add") line = "Add(" + find + ", " + Lit(Build(o).ToString(SaveOptions.DisableFormatting)) + ", " + o.Index + ");";
                if (line == null) continue;
                done.Add(o);
                edits.Append("            if (path == ").Append(Lit(path)).Append(") ").Append(line).Append("\r\n");
            }

            foreach (KeyValuePair<Op, string> a in actionIds)
            {
                string arg;
                Func<string, string> get = n => a.Key.Args != null && a.Key.Args.TryGetValue(n, out arg) ? arg : null;
                string code = a.Key.Name == "panel" ? "OpenPanel(" + Lit(get("panel")) + ")" : a.Key.Name == "link" ? "UnityEngine.Application.OpenURL(" + Lit(get("url")) + ")" : null;
                if (code == null) continue;
                wiring.Append("            if (w.Id == ").Append(Lit(a.Value)).Append(") w.Clicked += x => ").Append(code).Append(";\r\n");
                done.Add(a.Key);
            }
            foreach (Op o in ops) if (!done.Contains(o)) Left.Add((o.Text ?? o.Do) + (o.Do == "action" ? " (" + o.Name + ")" : ""));

            string cs = Template.Replace("%SPACE%", space).Replace("%NAME%", Lit(name)).Replace("%LAYOUTS%", layouts.ToString()).Replace("%EDITS%", edits.ToString())
                                .Replace("%STYLES%", styles.ToString()).Replace("%WIRING%", wiring.ToString())
                                .Replace("%LEFT%", Left.Count == 0 ? "" : "// Not exported (they only exist while a screen is running; do them in your own code):\r\n" + string.Join("", Left.Select(l => "//   " + l.Replace('\r', ' ').Replace('\n', ' ') + "\r\n")));
            Files[space + ".cs"] = cs.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private const string Template = @"// Made by the SP2 UI Editor. Put it in a BepInEx mod and call %SPACE%.ExportedUi.Apply(harmony) in Awake.
// References: 0Harmony, Game, Jundroo.Packages, Jundroo.Common, UnityEngine, UnityEngine.CoreModule, UnityEngine.UI, UnityEngine.UIModule.
%LEFT%using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Assets.Scripts.UI;
using HarmonyLib;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;

namespace %SPACE%
{
    public class ExportedPanel : PanelDialogScript
    {
        public override bool IsModal { get { return false; } }

        public override void OnWidgetInitialized(Widget widget)
        {
            base.OnWidgetInitialized(widget);
            Widget close = widget.FindWidget(""sp2ui-close"");
            if (close != null) close.Clicked += w => Close();
        }
    }

    public static class ExportedUi
    {
        private static readonly Dictionary<string, string> Layouts = new Dictionary<string, string>
        {
%LAYOUTS%        };

        private static readonly Dictionary<string, string> Styles = new Dictionary<string, string>
        {
%STYLES%        };

        private static readonly Dictionary<string, ExportedPanel> open = new Dictionary<string, ExportedPanel>();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ResourceLoader), ""LoadXml""), new HarmonyMethod(AccessTools.Method(typeof(ExportedUi), ""LoadXmlPrefix"")), new HarmonyMethod(AccessTools.Method(typeof(ExportedUi), ""LoadXmlPostfix"")));
            harmony.Patch(AccessTools.Method(typeof(ResourceLoader), ""GetScriptType""), postfix: new HarmonyMethod(AccessTools.Method(typeof(ExportedUi), ""ScriptTypePostfix"")));
            harmony.Patch(AccessTools.Method(typeof(Stylesheet), ""LoadXml""), new HarmonyMethod(AccessTools.Method(typeof(ExportedUi), ""StylesheetPrefix"")));
            harmony.Patch(AccessTools.Method(typeof(WidgetContext), ""CreateWidget"", new[] { typeof(XElement), typeof(Widget), typeof(Stylesheet) }), postfix: new HarmonyMethod(AccessTools.Method(typeof(ExportedUi), ""CreatedPostfix"")));
        }

        public static void OpenPanel(string uid)
        {
            ExportedPanel p;
            if (open.TryGetValue(uid, out p) && p != null && !p.IsClosed) { p.Close(); return; }
            open[uid] = Assets.Scripts.Game.Instance.UserInterface.CreateDialog<ExportedPanel>(""Mods/%SPACE%/"" + uid);
        }

        private static bool LoadXmlPrefix(string path, ref XElement __result)
        {
            string xml;
            if (path == null || !Layouts.TryGetValue(path, out xml)) return true;
            __result = XElement.Parse(xml);
            return false;
        }

        private static void LoadXmlPostfix(string path, XElement __result)
        {
            if (__result == null) return;
            XElement root = __result;
%EDITS%        }

        private static void ScriptTypePostfix(string scriptTypeName, ref Type __result)
        {
            if (__result == null && scriptTypeName == typeof(ExportedPanel).FullName) __result = typeof(ExportedPanel);
        }

        private static void StylesheetPrefix(XElement xml)
        {
            if (Styles.Count == 0) return;
            string value;
            foreach (XElement c in xml.Elements(""Constants"").Elements())
                if (Styles.TryGetValue(""const:"" + (string)c.Attribute(""name"") + "" Value"", out value)) c.SetAttributeValue(""value"", value);
            foreach (XElement s in xml.Elements(""Styles"").Elements())
                foreach (XAttribute a in s.Attributes().ToList())
                    if (Styles.TryGetValue(""style:"" + (string)s.Attribute(""class"") + "" "" + a.Name.LocalName, out value)) a.Value = value;
        }

        private static void CreatedPostfix(Widget __result)
        {
            Widget w = __result;
            if (w == null || string.IsNullOrEmpty(w.Id)) return;
%WIRING%        }

        // The element at its place, if it still looks the same; else the only one with its id; else none (the game changed).
        private static XElement Find(XElement root, string place, string type, string id)
        {
            XElement e = root;
            foreach (string part in place.Split('.')) e = e == null ? null : e.Elements().ElementAtOrDefault(int.Parse(part));
            if (e != null && (type.Length == 0 || e.Name.LocalName == type) && ((string)e.Attribute(""id"") ?? """") == id) return e;
            if (id.Length == 0) return null;
            List<XElement> same = root.Descendants(type).Where(x => (string)x.Attribute(""id"") == id).ToList();
            return same.Count == 1 ? same[0] : null;
        }

        private static void Set(XElement e, string name, string value) { if (e != null) e.SetAttributeValue(name, value); }

        private static void Class(XElement e, string name, bool on)
        {
            if (e == null) return;
            List<string> c = ((string)e.Attribute(""class"") ?? """").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != name).ToList();
            if (on) c.Add(name);
            e.SetAttributeValue(""class"", c.Count == 0 ? null : string.Join("" "", c));
        }

        private static void Add(XElement parent, string xml, int index)
        {
            if (parent == null) return;
            XElement before = index >= 0 ? parent.Elements().ElementAtOrDefault(index) : null;
            if (before != null) before.AddBeforeSelf(XElement.Parse(xml)); else parent.Add(XElement.Parse(xml));
        }
    }
}
";
    }
}
