using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;
using Newtonsoft.Json.Linq;

namespace SP2UIEditor
{
    // Values shared by many elements: the attributes of a style class ("btn-primary": color) and the game's constants ("ColorPrimary").
    // What is wanted comes from the edits in force; Sync puts it on every stylesheet there is, and a patch on every one made later.
    internal static class Styles
    {
        private sealed class Originals { public readonly Dictionary<string, string> Values = new Dictionary<string, string>(); }

        private static readonly ConditionalWeakTable<Stylesheet, Originals> originals = new ConditionalWeakTable<Stylesheet, Originals>();
        private static Dictionary<string, string> inForce = new Dictionary<string, string>();      // "style:btn color" or "const:ColorPrimary Value" -> value

        private static string Key(Op o) { return o.Target + " " + o.Name; }

        public static void Apply(Harmony harmony)
        {
            try { harmony.Patch(AccessTools.Method(typeof(Stylesheet), "LoadXml"), new HarmonyMethod(AccessTools.Method(typeof(Styles), nameof(LoadPrefix)))); }
            catch (Exception e) { EditorPlugin.Log.LogError("[UIEDIT] [COMPAT] styles: " + e.Message); }
        }

        private static void LoadPrefix(Stylesheet __instance, XElement xml)
        {
            if (inForce.Count == 0 || xml == null) return;
            try
            {
                Dictionary<string, string> keep = originals.GetOrCreateValue(__instance).Values;
                string value;
                foreach (XElement c in xml.Elements("Constants").Elements())
                {
                    string key = "const:" + (string)c.Attribute("name") + " Value";
                    if (!inForce.TryGetValue(key, out value)) continue;
                    keep[key] = (string)c.Attribute("value");
                    c.SetAttributeValue("value", value);
                }
                foreach (XElement s in xml.Elements("Styles").Elements())
                    foreach (XAttribute a in s.Attributes().ToList())
                    {
                        string key = "style:" + (string)s.Attribute("class") + " " + a.Name.LocalName;
                        if (!inForce.TryGetValue(key, out value)) continue;
                        keep[key] = a.Value;
                        a.Value = value;
                    }
            }
            catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] styles: " + e.Message); }
        }

        private static List<KeyValuePair<Stylesheet, List<Widget>>> Sheets()
        {
            Dictionary<Stylesheet, List<Widget>> all = new Dictionary<Stylesheet, List<Widget>>();
            foreach (Widget w in UnityEngine.Resources.FindObjectsOfTypeAll<Widget>())
            {
                if (w == null || w.Stylesheet == null || w.Context == null) continue;
                List<Widget> list;
                if (!all.TryGetValue(w.Stylesheet, out list)) all[w.Stylesheet] = list = new List<Widget>();
                list.Add(w);
            }
            return all.ToList();
        }

        // Makes what is on the screen agree with the edits in force.
        public static void Sync(IEnumerable<Op> live)
        {
            Dictionary<string, string> want = new Dictionary<string, string>();
            foreach (Op o in live) if (o.Do == "style" || o.Do == "constant") want[Key(o)] = o.Value;
            if (want.Count == inForce.Count && want.All(x => inForce.TryGetValue(x.Key, out string v) && v == x.Value)) return;
            List<string> back = inForce.Keys.Where(k => !want.ContainsKey(k)).ToList();
            List<KeyValuePair<string, string>> put = want.Where(x => !inForce.TryGetValue(x.Key, out string v) || v != x.Value).ToList();
            inForce = want;
            foreach (KeyValuePair<Stylesheet, List<Widget>> sheet in Sheets())
            {
                Originals kept = originals.GetOrCreateValue(sheet.Key);
                HashSet<string> classes = new HashSet<string>();
                bool constants = false;
                foreach (string k in back)
                {
                    string original;
                    if (!kept.Values.TryGetValue(k, out original)) continue;
                    kept.Values.Remove(k);
                    constants |= Write(sheet.Key, k, original, classes, null);
                }
                foreach (KeyValuePair<string, string> k in put) constants |= Write(sheet.Key, k.Key, k.Value, classes, kept.Values);
                if (classes.Count == 0 && !constants) continue;
                foreach (Widget w in sheet.Value)
                    if (Tree.Classes(w).Any(classes.Contains)) LiveEditor.Refresh(w);
            }
        }

        // True when a constant was written. keep: where the value there was before goes (the first time).
        private static bool Write(Stylesheet s, string key, string value, HashSet<string> classes, Dictionary<string, string> keep)
        {
            int space = key.LastIndexOf(' ');
            string target = key.Substring(0, space), name = key.Substring(space + 1);
            if (target.StartsWith("const:"))
            {
                string constant = target.Substring(6), old = s.GetConstant(constant);
                if (old == null || old == value) return false;
                if (keep != null && !keep.ContainsKey(key)) keep[key] = old;
                // styles that were written with this constant hold its old value
                Dictionary<WidgetStyle, List<string>> using_ = new Dictionary<WidgetStyle, List<string>>();
                foreach (XElement raw in RawStyles())
                {
                    WidgetStyle style = s.GetStyle((string)raw.Attribute("class") ?? "");
                    if (style == null) continue;
                    foreach (XAttribute a in raw.Attributes())
                    {
                        if (a.Value.IndexOf("@" + constant, StringComparison.Ordinal) < 0 && a.Value.IndexOf("@(" + constant, StringComparison.Ordinal) < 0) continue;
                        string now;
                        if (!style.Attributes.TryGetValue(a.Name.LocalName, out now) || now != s.ReplaceConstants(a.Value)) continue;
                        if (!using_.ContainsKey(style)) using_[style] = new List<string>();
                        using_[style].Add(a.Name.LocalName + "\n" + a.Value);
                    }
                }
                s.SetConstant(constant, value);
                foreach (KeyValuePair<WidgetStyle, List<string>> u in using_)
                    foreach (string x in u.Value)
                    {
                        string[] p = x.Split(new[] { '\n' }, 2);
                        u.Key.Attributes[p[0]] = s.ReplaceConstants(p[1]);
                        classes.Add(u.Key.Name);
                    }
                return true;
            }
            WidgetStyle found = s.GetStyle(target.Substring(6));
            string before;
            if (found == null || !found.Attributes.TryGetValue(name, out before)) return false;
            if (keep != null && !keep.ContainsKey(key)) keep[key] = before;
            found.Attributes[name] = s.ReplaceConstants(value);
            classes.Add(found.Name);
            return false;
        }

        private static IEnumerable<XElement> RawStyles()
        {
            // after a reload (F6) these were not seen loading
            Patches.RawOf("Xml/Styles");
            Patches.RawOf("Xml/Flight/FlightStyles");
            return Patches.Raw.Values.Where(r => r != null).SelectMany(r => r.Descendants("Style")).ToList();
        }

        private static IEnumerable<XElement> RawConstants()
        {
            Patches.RawOf("Xml/Styles");
            return Patches.Raw.Values.Where(r => r != null).SelectMany(r => r.Descendants("Constant")).ToList();
        }

        // ---- for the editor's window ----

        public static string Problem(Widget w, string target, string name, string value)
        {
            string old;
            if (target.StartsWith("const:"))
            {
                old = w.Stylesheet.GetConstant(target.Substring(6));
                if (old == null) return "this screen has no constant '" + target.Substring(6) + "'";
            }
            else if (target.StartsWith("style:"))
            {
                WidgetStyle s = w.Stylesheet.GetStyle(target.Substring(6));
                if (s == null || !s.Attributes.TryGetValue(name, out old)) return "that style does not set '" + name + "'";
            }
            else return "a style or a constant is needed";
            // the same kind of thing as before: a color for a color, a number for a number
            string now = w.Stylesheet.ReplaceConstants(value ?? "");
            foreach (string kind in new[] { "bool", "float", "color" })
                if (old.Length > 0 && Values.Problem(kind, old) == null) return Values.Problem(kind, now);
            return null;
        }

        public static JObject Describe(Widget w)
        {
            JArray styles = new JArray(), constants = new JArray();
            Dictionary<string, WidgetStyle> all = Tree.Styles(w.Stylesheet);
            foreach (string c in Tree.Classes(w))
            {
                WidgetStyle s;
                if (!all.TryGetValue(c, out s)) continue;
                JArray attributes = new JArray();
                foreach (KeyValuePair<string, string> a in s.Attributes.OrderBy(x => x.Key))
                    if (SafeXml.AttributeAllowed(a.Key) && SafeXml.ValueAllowed(a.Value))
                        attributes.Add(new JObject { ["name"] = a.Key, ["value"] = a.Value, ["edited"] = inForce.ContainsKey("style:" + c + " " + a.Key), ["color"] = a.Value.Length > 0 && Values.Problem("color", a.Value) == null });
                if (attributes.Count > 0) styles.Add(new JObject { ["class"] = c, ["attributes"] = attributes });
            }
            foreach (string name in RawConstants().Select(x => (string)x.Attribute("name")).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x))
            {
                string v = w.Stylesheet.GetConstant(name);
                if (v == null || v.IndexOf('{') >= 0) continue;
                constants.Add(new JObject { ["name"] = name, ["value"] = v, ["edited"] = inForce.ContainsKey("const:" + name + " Value"), ["color"] = v.Length > 0 && Values.Problem("color", v) == null });
            }
            return new JObject { ["styles"] = styles, ["constants"] = constants };
        }

        public static void Restore() { Sync(new Op[0]); }
    }
}
