using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;

namespace SP2UIEditor
{
    // What the editor knows about where the UI comes from.
    internal static class Patches
    {
        // The layouts as they were loaded, before Juicy put the constants' values in.
        public static readonly Dictionary<string, XElement> Raw = new Dictionary<string, XElement>();

        internal sealed class Instance
        {
            public string Template;
            public string Src;
            public Dictionary<string, string> Attributes = new Dictionary<string, string>();
        }

        private static readonly ConditionalWeakTable<Widget, Instance> instances = new ConditionalWeakTable<Widget, Instance>();

        public static Instance InstanceOf(Widget w)
        {
            Instance i;
            if (ReferenceEquals(w, null)) return null;
            if (instances.TryGetValue(w, out i)) return i;
            // made while an earlier copy of the editor was loaded (F6): it wrote what it knew on the element
            XElement e = w.Element;
            string template = e != null ? (string)e.Attribute("_tmpl") : null;
            if (template == null) return null;
            i = instances.GetOrCreateValue(w);
            i.Template = template;
            i.Src = (string)e.Attribute("_inst");
            string label = (string)e.Attribute("_label");
            if (label != null) i.Attributes["label"] = label;
            return i;
        }

        // The layout as it is in the game's files; fetched when this copy of the editor has not seen it load.
        public static XElement RawOf(string path)
        {
            XElement raw;
            if (path == null) return null;
            if (Raw.TryGetValue(path, out raw)) return raw;
            try { Assets.Scripts.Game.Instance.UserInterface.ResourceLoader.LoadXml(path); }
            catch (Exception) { }
            if (!Raw.TryGetValue(path, out raw)) Raw[path] = null;
            return raw;
        }

        private static void CreatedPostfix(Widget __result) { if (__result != null) Packages.Created(__result); }

        public static void Apply(Harmony harmony)
        {
            Patch(harmony, "LoadXml", AccessTools.Method(typeof(ResourceLoader), "LoadXml"), nameof(LoadXmlPostfix));
            Patch(harmony, "created widgets", AccessTools.Method(typeof(WidgetContext), "CreateWidget", new[] { typeof(XElement), typeof(Widget), typeof(Stylesheet) }), nameof(CreatedPostfix));
            Patch(harmony, "template instances", AccessTools.Method(typeof(WidgetContext), "CreateWidgetFromTemplate",
                new[] { typeof(XElement), typeof(Widget), typeof(Stylesheet) }), nameof(InstancePostfix));
            Patch(harmony, "templates by code", AccessTools.Method(typeof(WidgetContext), "CreateWidgetFromTemplate",
                new[] { typeof(string), typeof(Widget), typeof(IEnumerable<XAttribute>), typeof(Stylesheet) }), nameof(CodeTemplatePostfix));
        }

        private static void Patch(Harmony harmony, string name, MethodBase method, string postfix)
        {
            try
            {
                if (method == null) throw new MissingMethodException("not found in this version of the game");
                HarmonyMethod patch = new HarmonyMethod(AccessTools.Method(typeof(Patches), postfix));
                patch.priority = Priority.Last;
                harmony.Patch(method, postfix: patch);
            }
            catch (Exception e) { EditorPlugin.Log.LogError("[UIEDIT] [COMPAT] " + name + ": " + e.Message); }
        }

        private static void LoadXmlPostfix(string path, XElement __result)
        {
            if (__result == null) return;
            try
            {
                // an element another copy of the editor already marked (F6) is kept clean
                Sources.Unstamp(__result);
                Raw[path] = new XElement(__result);
                Sources.Stamp(path, __result);
            }
            catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] layout '" + path + "' not followed: " + e.Message); }
        }

        private static void InstancePostfix(XElement instance, Widget __result)
        {
            if (__result == null || instance == null) return;
            Remember(__result, (string)instance.Attribute("templateId"), instance.Attributes());
        }

        private static void CodeTemplatePostfix(string templateId, IEnumerable<XAttribute> instanceAttributes, Widget __result)
        {
            if (__result == null) return;
            Remember(__result, templateId, instanceAttributes ?? Enumerable.Empty<XAttribute>());
        }

        private static void Remember(Widget w, string template, IEnumerable<XAttribute> attributes)
        {
            try
            {
                Instance i = instances.GetOrCreateValue(w);
                i.Template = template;
                foreach (XAttribute a in attributes)
                {
                    if (a.Name.LocalName == Sources.Mark) i.Src = a.Value;
                    else if (a.Name.LocalName != "templateId") i.Attributes[a.Name.LocalName] = a.Value;
                }
                if (w.Element != null)
                {
                    string label;
                    if (!i.Attributes.TryGetValue("label", out label)) i.Attributes.TryGetValue("title", out label);
                    w.Element.SetAttributeValue("_tmpl", template);
                    w.Element.SetAttributeValue("_inst", i.Src);
                    w.Element.SetAttributeValue("_label", label);
                }
            }
            catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] template '" + template + "': " + e.Message); }
        }
    }
}
