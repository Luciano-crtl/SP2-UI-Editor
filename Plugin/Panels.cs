using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Assets.Scripts.UI;
using HarmonyLib;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;
using Newtonsoft.Json.Linq;

namespace SP2UIEditor
{
    // A window made in the editor. The game finds this class by its name (see Panels.ScriptTypePostfix).
    public class PackagePanel : PanelDialogScript
    {
        // It doesn't block the game.
        public override bool IsModal { get { return false; } }

        public override void OnWidgetInitialized(Widget widget)
        {
            base.OnWidgetInitialized(widget);
            Widget close = widget.FindWidget("sp2ui-close");
            if (close != null) close.Clicked += w => Close();
        }
    }

    internal static class Panels
    {
        public const string Prefix = "SP2UI/";      // the layout's "file"
        private static readonly Dictionary<string, PackagePanel> open = new Dictionary<string, PackagePanel>();

        // Every panel of the packages that are on, and those made in this session.
        public static List<Op> Defs()
        {
            return Packages.Live().Where(o => o.Do == "panel").ToList();
        }

        public static Op Def(string uid) { return Defs().FirstOrDefault(o => o.Uid == uid); }

        public static bool IsOpen(string uid)
        {
            PackagePanel p;
            return open.TryGetValue(uid, out p) && p != null && !p.IsClosed;
        }

        public static string Open(string uid)
        {
            if (Def(uid) == null) return "that panel does not exist (is its package off?)";
            if (IsOpen(uid)) return null;
            try
            {
                open[uid] = Assets.Scripts.Game.Instance.UserInterface.CreateDialog<PackagePanel>(Prefix + uid);
                return null;
            }
            catch (Exception e)
            {
                EditorPlugin.Log.LogWarning("[UIEDIT] panel: " + e);
                return "the panel could not be opened: " + e.GetBaseException().Message;
            }
        }

        public static void Close(string uid)
        {
            if (IsOpen(uid)) open[uid].Close();
            open.Remove(uid);
        }

        public static void Toggle(string uid)
        {
            if (IsOpen(uid)) { Close(uid); return; }
            string problem = Open(uid);
            if (problem != null) EditorPlugin.Log.LogWarning("[UIEDIT] " + problem);
        }

        public static void CloseAll() { foreach (string uid in open.Keys.ToList()) Close(uid); }

        // Panels whose package was switched off or that were removed.
        public static void CloseMissing()
        {
            HashSet<string> live = new HashSet<string>(Defs().Select(o => o.Uid));
            foreach (string uid in open.Keys.ToList()) if (!live.Contains(uid)) Close(uid);
        }

        public static Widget Content(string uid)
        {
            return IsOpen(uid) ? open[uid].Widget.FindWidget("sp2ui-content") : null;
        }

        public static JArray Describe()
        {
            return new JArray(Defs().Select(o => new JObject { ["uid"] = o.Uid, ["title"] = o.Name, ["open"] = IsOpen(o.Uid) }));
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ResourceLoader), "LoadXml"), prefix: new HarmonyMethod(AccessTools.Method(typeof(Panels), nameof(LoadXmlPrefix))));
                harmony.Patch(AccessTools.Method(typeof(ResourceLoader), "GetScriptType"), postfix: new HarmonyMethod(AccessTools.Method(typeof(Panels), nameof(ScriptTypePostfix))));
            }
            catch (Exception e) { EditorPlugin.Log.LogError("[UIEDIT] [COMPAT] panels: " + e.Message); }
        }

        private static void ScriptTypePostfix(string scriptTypeName, ref Type __result)
        {
            if (__result == null && scriptTypeName == typeof(PackagePanel).FullName) __result = typeof(PackagePanel);
            if (__result == null && scriptTypeName == typeof(PackageList).FullName) __result = typeof(PackageList);
        }

        private static bool LoadXmlPrefix(string path, ref XElement __result)
        {
            if (path == null || !path.StartsWith(Prefix)) return true;
            if (path == Player.Path)
            {
                __result = Player.Layout();
                return false;
            }
            Op def = Def(path.Substring(Prefix.Length));
            if (def == null) return true;
            __result = Layout(def);
            return false;
        }

        private static XElement E(string name, params object[] content) { return new XElement(name, content); }
        private static XAttribute A(string name, string value) { return new XAttribute(name, value); }

        public static XElement Layout(Op def) { return Frame(def.Name, def.Value ?? "480", typeof(PackagePanel)); }

        public static XElement Frame(string title, string width, Type script)
        {
            return E("Xml",
                E("Stylesheets",
                    E("Stylesheet", A("path", "Xml/Styles")),
                    E("Stylesheet", E("Styles", E("Style", A("class", "sp2ui-panel"), A("width", width), A("maxHeightPercent", "90"))))),
                E("Widgets",
                    E("Instance", A("id", "sp2ui-panel"), A("templateId", "dialog"), A("class", "sp2ui-panel"), A("contentPadding", "7 7 7 7"),
                        A("anchor", "MiddleCenter"), A("script", script.FullName), A("title", title.Replace("@", "@@")),
                        A("backerVisible", "false"), A("buttonRowVisible", "false"), A("spacerVisible", "false"),
                        E("Section", A("sectionId", "title-buttons"),
                            E("Button", A("id", "sp2ui-close"), A("class", "btn btn-danger"), A("width", "35"), A("height", "35"), A("anchor", "MiddleRight"),
                                E("Image", A("sprite", "Sprites/Common/IconButtonClose")))),
                        E("Section", A("sectionId", "content"),
                            E("ScrollView", A("fitContentHeight", "true"), A("verticalScrollbarVisibility", "AutoHideAndExpandViewport"),
                                E("VerticalLayout", A("id", "sp2ui-content"), A("class", "vertical-layout"), A("padding", "@HalfSpacing @Spacing @HalfSpacing @HalfSpacing"), A("spacing", "@Spacing")))))));
        }
    }
}
