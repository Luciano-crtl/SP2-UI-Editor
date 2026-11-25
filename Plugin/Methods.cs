using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Scripts.Flight;
using Assets.Scripts.UI;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;
using Newtonsoft.Json.Linq;

namespace SP2UIEditor
{
    // Calling a method of the game from a button. It only happens after the player said yes to that very call, in the game: nothing that comes
    // through the server (the editor's window, Claude) can say yes.
    internal static class Methods
    {
        private static HashSet<string> trusted;

        private static string TrustFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "SP2UIEditor", "trust.json"); } }

        private static HashSet<string> Trusted()
        {
            if (trusted != null) return trusted;
            trusted = new HashSet<string>();
            try { if (File.Exists(TrustFile)) foreach (JToken t in JArray.Parse(File.ReadAllText(TrustFile))) trusted.Add((string)t); }
            catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] trust.json: " + e.Message); }
            return trusted;
        }

        private static object Root(string name, Widget w)
        {
            FlightSceneScript flight = FlightSceneScript.Instance;
            var designer = Assets.Scripts.Design.Designer.Instance;
            switch (name)
            {
                case "game": return Assets.Scripts.Game.Instance;
                case "ui": return Assets.Scripts.Game.Instance.UserInterface;
                case "flight": return flight != null ? flight : null;
                case "flightUI": return flight != null ? flight.FlightUI : null;
                case "designer": return designer;
                case "designerUI": return designer != null && designer.DesignerScript != null ? designer.DesignerScript.DesignerUI : null;
                case "script":
                    for (Widget at = w; at != null; at = at.Parent)
                    {
                        IWidgetScript s = at.GetComponent<IWidgetScript>();
                        if (s != null && Calls.Of(s.GetType()).Count > 0) return s;
                    }
                    return null;
            }
            return null;
        }

        private static MethodInfo Find(object root, Dictionary<string, string> args)
        {
            return root == null ? null : Calls.Of(root.GetType()).FirstOrDefault(m => Calls.Signature(m) == args["method"]);
        }

        // null = good
        public static string Problem(Widget w, Dictionary<string, string> args)
        {
            object root = Root(args["root"], w);
            if (root == null) return null;      // its scene is not open: looked up when clicked
            MethodInfo m = Find(root, args);
            if (m == null) return "that method is not there, or may not be called";
            try { Values(m, args); }
            catch (Exception e) { return e.GetBaseException().Message; }
            return null;
        }

        private static object[] Values(MethodInfo m, Dictionary<string, string> args)
        {
            return m.GetParameters().Select((p, i) => { string v; args.TryGetValue("a" + i, out v); return Calls.Convert(v, p); }).ToArray();
        }

        public static JArray List(Widget w)
        {
            JArray list = new JArray();
            foreach (string r in Calls.Roots)
            {
                object root = Root(r, w);
                if (root == null) continue;
                foreach (MethodInfo m in Calls.Of(root.GetType()))
                    list.Add(new JObject { ["root"] = r, ["of"] = root.GetType().Name, ["method"] = Calls.Signature(m), ["args"] = new JArray(m.GetParameters().Select(p => p.ParameterType.IsEnum ? p.Name + ": " + string.Join(" | ", Enum.GetNames(p.ParameterType)) : p.Name)) });
            }
            return list;
        }

        // A button was clicked.
        public static void Run(Widget w, string owner, Dictionary<string, string> args)
        {
            object root = Root(args["root"], w);
            MethodInfo m = Find(root, args);
            if (m == null) { EditorPlugin.Log.LogWarning("[UIEDIT] " + args["root"] + "." + args["method"] + " is not there now"); return; }
            object[] values = Values(m, args);
            string print = Calls.Fingerprint(owner, Identity.Of(w), args);
            if (Trusted().Contains(print)) { m.Invoke(root, values); return; }
            string shown = root.GetType().Name + "." + m.Name + "(" + string.Join(", ", values.Select(v => Plain(System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)))) + ")";
            Assets.Scripts.Game.Instance.UserInterface.CreateMessageDialog(MessageDialogType.OkayCancel,
                "This button, of the UI package \"" + Plain(owner) + "\", wants to call a method of the game:\n\n" + shown + "\n\nAllow it from now on? Only say yes to packages you trust.", "UI package",
                d =>
                {
                    d.Close();
                    Trusted().Add(print);
                    try { File.WriteAllText(TrustFile, new JArray(Trusted()).ToString()); }
                    catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] trust.json: " + e.Message); }
                    object now = Root(args["root"], w);
                    try { if (now != null) m.Invoke(now, values); }
                    catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] " + shown + ": " + e.GetBaseException().Message); }
                });
        }

        // No formatting tricks in what the player is asked.
        private static string Plain(string s)
        {
            s = new string((s ?? "").Where(c => c >= ' ' && c != '<' && c != '>').ToArray());
            return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
        }
    }
}
