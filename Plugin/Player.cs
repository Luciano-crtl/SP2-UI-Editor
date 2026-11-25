using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Controls;
using Jundroo.Juicy.Widgets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SP2UIEditor
{
    // For who only uses packages: a window to switch them on and off (Ctrl+Shift+U), and the question asked when the editor's program wants to connect.
    internal static class Player
    {
        public const string Path = "SP2UI/packages";
        private static PackageList window;
        private static float nextLook;
        private static DateTime asked = DateTime.MinValue;
        private static bool asking, refused;

        private static string Marker
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SP2UIEditor", "editor-open"); }
        }

        // The program touches this file every second while it is open.
        private static bool EditorOpen()
        {
            try { return File.Exists(Marker) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(Marker)).TotalSeconds < 6; }
            catch { return false; }
        }

        public static void Tick(bool serverOn, Action start)
        {
            if (Keyboard.current != null && Keyboard.current.uKey.wasPressedThisFrame && Keyboard.current.ctrlKey.isPressed && Keyboard.current.shiftKey.isPressed
                && !Assets.Scripts.Game.Instance.UserInterface.IsTextInputFocused) Toggle();
            if (serverOn || asking || Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 2f;
            bool open = EditorOpen();
            if (!open) { refused = false; return; }
            if (refused) return;
            asking = true;
            MessageDialogScript d = Assets.Scripts.Game.Instance.UserInterface.CreateMessageDialog(MessageDialogType.OkayCancel,
                "The SP2 UI Editor program is open on this computer and wants to show and change the interface of the game.\n\nAllow it until the game closes?", "SP2 UI Editor",
                x => { asking = false; x.Close(); start(); });
            // closed in any other way = no, until the program is opened again
            d.Closed += x => { if (asking) { asking = false; refused = true; } };
        }

        public static void Toggle()
        {
            try
            {
                if (window != null && !window.IsClosed) { window.Close(); window = null; return; }
                window = Assets.Scripts.Game.Instance.UserInterface.CreateDialog<PackageList>(Path);
            }
            catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] package list: " + e); }
        }

        public static void Close()
        {
            if (window != null && !window.IsClosed) window.Close();
            window = null;
        }

        public static XElement Layout() { return Panels.Frame("UI packages", "420", typeof(PackageList)); }
    }

    public class PackageList : PanelDialogScript
    {
        public override bool IsModal { get { return false; } }

        private readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<TextControl, Package>> rows = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<TextControl, Package>>();
        private float next;

        // "Designer, FlightUI": the screens it changes.
        private static string Where(Package p)
        {
            string[] files = p.Ops.SelectMany(o => o.Bases()).Select(b => Sources.FileOf(b.Substring(4))).Where(f => f != null && !f.StartsWith("SP2UI/")).Select(f => f.Substring(f.LastIndexOf('/') + 1)).Distinct().Take(4).ToArray();
            string s = string.Join(", ", files);
            if (p.Ops.Any(o => o.Do == "style" || o.Do == "constant")) s = s.Length == 0 ? "all screens" : s + ", styles";
            return s.Length == 0 ? "panels" : s;
        }

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            foreach (var r in rows)
            {
                int now = r.Value.Ops.Count(o => Packages.AppliedNow(o));
                r.Key.ValueText.Text = !Packages.Enabled(r.Value) ? "off" : now > 0 ? now + " on this screen" : "not on this screen";
            }
        }

        public override void OnWidgetInitialized(Widget widget)
        {
            base.OnWidgetInitialized(widget);
            widget.FindWidget("sp2ui-close").Clicked += w => Close();
            Widget list = widget.FindWidget("sp2ui-content");
            if (Packages.All.Count == 0)
                new TextControl(list.Context.CreateWidgetFromTemplate("control-text", list)).LabelText = "No packages. Put .sp2ui files in BepInEx/config/SP2UIEditor/packages";
            foreach (Package p in Packages.All.ToList())
            {
                string name = p.Name;
                ToggleControl t = new ToggleControl(list.Context.CreateWidgetFromTemplate("control-toggle", list, new[] { new XAttribute("label", name.Replace("@", "@@")) }));
                t.IsOn = Packages.Enabled(p);
                t.ValueChanged += on =>
                {
                    string problem = Packages.Enable(name, on);
                    if (problem == null) return;
                    EditorPlugin.Log.LogWarning("[UIEDIT] " + name + ": " + problem);
                    t.IsOn = Packages.Enabled(p);
                };
                // what it changes, so that nobody wonders why nothing happens on another screen
                TextControl info = new TextControl(list.Context.CreateWidgetFromTemplate("control-text", list));
                info.LabelText = p.Ops.Count + " edits: " + Where(p);
                rows.Add(new System.Collections.Generic.KeyValuePair<TextControl, Package>(info, p));
            }
        }
    }
}
