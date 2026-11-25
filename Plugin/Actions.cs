using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assets.Scripts.Craft.Parts.Modifiers.Variables;
using Assets.Scripts.Flight;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Controls;
using BepInEx;
using BepInEx.Configuration;
using Jundroo.Juicy.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SP2UIEditor
{
    // For other mods: an action under a name, which a button made in the editor can then run.
    //   SP2UIEditor.UiActions.Register("My mod: fire the confetti", () => ...);     (also callable by reflection)
    public static class UiActions
    {
        internal static readonly Dictionary<string, Action> All = new Dictionary<string, Action>();

        public static void Register(string name, Action action)
        {
            if (!string.IsNullOrWhiteSpace(name) && action != null) All[name] = action;
        }

        public static void Unregister(string name) { if (name != null) All.Remove(name); }
    }

    // What a button, toggle or slider does. Nothing here runs code that a package brings: only these kinds exist.
    internal static class Actions
    {
        public sealed class Binding
        {
            public string Kind;
            public Dictionary<string, string> Args;
            public Widget Widget;
            public bool Alive = true;
            public WidgetDelegate Clicked;
            public ToggleControl Toggle;
            public SliderControl Slider;
            public TextWidget Text;
            public bool Writing;
            public string Problem;
        }

        private static readonly Dictionary<int, Binding> bindings = new Dictionary<int, Binding>();   // by handle

        public static Binding Of(Widget w)
        {
            Binding b;
            return bindings.TryGetValue(Tree.Handle(w), out b) && b.Alive ? b : null;
        }

        // Binds; problem = why not (nothing was changed then).
        public static Binding Bind(Widget w, string kind, Dictionary<string, string> args, out string problem)
        {
            problem = null;
            Binding b = new Binding { Kind = kind, Args = args ?? new Dictionary<string, string>(), Widget = w };
            bool toggle = w.FindWidget<ToggleWidget>("toggle-value") != null, slider = w.FindWidget<SliderWidget>("slider") != null;
            TextWidget value = w.FindWidget<TextWidget>("value-text");
            if (kind == "config" && Entry(b) == null) { problem = "that setting was not found (is the mod loaded?)"; return null; }
            if (toggle || slider)
            {
                if (kind != "config" && kind != "variable") { problem = "a toggle or slider can only be tied to a mod's setting or a craft variable"; return null; }
                if (toggle && kind == "config" && Entry(b).SettingType != typeof(bool)) { problem = "a toggle needs an on/off setting"; return null; }
                if (slider && !toggle && kind == "config" && !IsNumber(Entry(b).SettingType)) { problem = "a slider needs a number setting"; return null; }
            }
            else if (!(w is ButtonWidget) && !(kind == "config" && value != null)) { problem = "choose a button, a toggle, a slider or (to show a setting) a line with a value"; return null; }

            Unbind(w);
            if (toggle)
            {
                b.Toggle = new ToggleControl(w);
                b.Toggle.ValueChanged += on => { if (b.Alive && !b.Writing) Run(b, on ? "1" : "0"); };
            }
            else if (slider)
            {
                b.Slider = new SliderControl(w);
                Vector2 range = Range(b);
                bool whole = kind == "config" && IsWhole(Entry(b).SettingType);
                b.Slider.SetRange(range.x, range.y, whole ? Mathf.Min(1000, Mathf.RoundToInt(range.y - range.x) + 1) : 0);
                b.Slider.ValueFormatter = x => x.ToString(whole ? "0" : "0.###", CultureInfo.InvariantCulture);
                b.Slider.OnValueChanged += (a, x) => { if (b.Alive && !b.Writing) Run(b, x.ToString("R", CultureInfo.InvariantCulture)); };
            }
            else if (w is ButtonWidget)
            {
                b.Clicked = x => { if (b.Alive) Run(b, null); };
                w.Clicked += b.Clicked;
            }
            else b.Text = value;
            bindings[Tree.Handle(w)] = b;
            Show(b);
            return b;
        }

        public static void Unbind(Widget w)
        {
            Binding b;
            int h = Tree.Handle(w);
            if (!bindings.TryGetValue(h, out b)) return;
            b.Alive = false;
            if (b.Clicked != null && b.Widget != null) b.Widget.Clicked -= b.Clicked;
            bindings.Remove(h);
        }

        public static void UnbindAll()
        {
            foreach (Binding b in bindings.Values.ToList()) if (b.Widget != null) Unbind(b.Widget);
            bindings.Clear();
        }

        // ---- doing it ----

        private static string Arg(Binding b, string name)
        {
            string v;
            return b.Args.TryGetValue(name, out v) ? v : null;
        }

        // value: from a toggle or slider; null = a button was clicked
        private static void Run(Binding b, string value)
        {
            try
            {
                switch (b.Kind)
                {
                    case "panel": Panels.Toggle(Arg(b, "panel")); break;
                    case "element": Element(Arg(b, "target"), Arg(b, "how")); break;
                    case "link": Link(Arg(b, "url")); break;
                    case "config": Config(b, value); break;
                    case "variable": Variable(b, value); break;
                    case "mod":
                        Action action;
                        if (UiActions.All.TryGetValue(Arg(b, "name") ?? "", out action)) action();
                        else EditorPlugin.Log.LogWarning("[UIEDIT] no mod offers the action '" + Arg(b, "name") + "'");
                        break;
                }
            }
            catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] action " + b.Kind + ": " + e.GetBaseException().Message); }
        }

        private static void Element(string identity, string how)
        {
            foreach (Widget w in Packages.Alive(identity))
                w.Visible = how == "show" || (how != "hide" && !w.Visible);
        }

        private static void Link(string url)
        {
            if (!Op.GoodLink(url)) return;
            Assets.Scripts.Game.Instance.UserInterface.CreateMessageDialog(MessageDialogType.OkayCancel, "Open this address in the browser?\n\n" + url, "Link",
                d => { d.Close(); Application.OpenURL(url); });
        }

        // ---- settings of mods ----

        public static IEnumerable<BaseUnityPlugin> Mods()
        {
            return UnityEngine.Object.FindObjectsByType<BaseUnityPlugin>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(p => p.Info != null && p.Info.Metadata != null);
        }

        private static readonly Dictionary<string, KeyValuePair<BaseUnityPlugin, ConfigEntryBase>> entries = new Dictionary<string, KeyValuePair<BaseUnityPlugin, ConfigEntryBase>>();

        private static ConfigEntryBase Entry(Binding b)
        {
            string key = Arg(b, "plugin") + "|" + Arg(b, "section") + "|" + Arg(b, "key");
            KeyValuePair<BaseUnityPlugin, ConfigEntryBase> known;
            if (entries.TryGetValue(key, out known) && known.Key != null) return known.Value;     // a reloaded mod has new ones
            foreach (BaseUnityPlugin p in Mods().Where(x => x.Info.Metadata.GUID == Arg(b, "plugin")))
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> e in Pairs(p.Config))
                    if (e.Key.Section == Arg(b, "section") && e.Key.Key == Arg(b, "key"))
                    {
                        entries[key] = new KeyValuePair<BaseUnityPlugin, ConfigEntryBase>(p, e.Value);
                        return e.Value;
                    }
            return null;
        }

        private static IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>> Pairs(ConfigFile file)
        {
            return file == null ? Enumerable.Empty<KeyValuePair<ConfigDefinition, ConfigEntryBase>>()
                                : file.Keys.ToList().Select(k => new KeyValuePair<ConfigDefinition, ConfigEntryBase>(k, file[k]));
        }

        private static bool IsWhole(Type t) { return t == typeof(int) || t == typeof(long) || t == typeof(byte) || t == typeof(short); }
        private static bool IsNumber(Type t) { return IsWhole(t) || t == typeof(float) || t == typeof(double); }

        private static void Config(Binding b, string value)
        {
            ConfigEntryBase e = Entry(b);
            if (e == null) return;
            Type t = e.SettingType;
            if (value == null)
            {
                string fixedValue = Arg(b, "value");
                if (fixedValue != null) e.SetSerializedValue(fixedValue);
                else if (t == typeof(bool)) e.BoxedValue = !(bool)e.BoxedValue;
                else if (t.IsEnum)
                {
                    Array all = Enum.GetValues(t);
                    e.BoxedValue = all.GetValue((Array.IndexOf(all, e.BoxedValue) + 1) % all.Length);
                }
            }
            else if (t == typeof(bool)) e.BoxedValue = value == "1";
            else if (IsNumber(t))
            {
                double x = double.Parse(value, CultureInfo.InvariantCulture);
                e.BoxedValue = Convert.ChangeType(IsWhole(t) ? Math.Round(x) : x, t, CultureInfo.InvariantCulture);
            }
            ShowAll();
        }

        private static float Parse(string s, float otherwise)
        {
            float f;
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : otherwise;
        }

        private static Vector2 Range(Binding b)
        {
            float min = 0f, max = 1f;
            if (b.Kind == "config")
            {
                ConfigEntryBase e = Entry(b);
                AcceptableValueBase a = e.Description != null ? e.Description.AcceptableValues : null;
                if (a != null && a.GetType().GetProperty("MinValue") != null)
                {
                    min = Convert.ToSingle(a.GetType().GetProperty("MinValue").GetValue(a), CultureInfo.InvariantCulture);
                    max = Convert.ToSingle(a.GetType().GetProperty("MaxValue").GetValue(a), CultureInfo.InvariantCulture);
                }
                else max = Mathf.Max(1f, 2f * Mathf.Abs(Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture)));
            }
            min = Parse(Arg(b, "min"), min);
            max = Parse(Arg(b, "max"), max);
            return max > min ? new Vector2(min, max) : new Vector2(min, min + 1f);
        }

        public static JArray Configs()
        {
            JArray list = new JArray();
            foreach (BaseUnityPlugin p in Mods().OrderBy(x => x.Info.Metadata.Name))
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> e in Pairs(p.Config))
                {
                    Type t = e.Value.SettingType;
                    string kind = t == typeof(bool) ? "bool" : IsNumber(t) ? "number" : t.IsEnum ? "enum" : null;
                    if (kind == null) continue;
                    list.Add(new JObject
                    {
                        ["plugin"] = p.Info.Metadata.GUID, ["mod"] = p.Info.Metadata.Name, ["section"] = e.Key.Section, ["key"] = e.Key.Key, ["kind"] = kind,
                        ["value"] = e.Value.GetSerializedValue(), ["text"] = e.Value.Description != null ? e.Value.Description.Description : "",
                    });
                }
            return list;
        }

        // ---- craft variables: the game forgets a value each frame, so it is written each frame ----

        private static readonly Dictionary<string, float> variables = new Dictionary<string, float>();
        private static object variablesOf;
        private static string variableProblem;

        private static void Variable(Binding b, string value)
        {
            string name = Arg(b, "name");
            float now, on = Parse(Arg(b, "max"), 1f), off = Parse(Arg(b, "min"), 0f);
            variables.TryGetValue(name, out now);
            if (value != null && b.Slider != null) variables[name] = Parse(value, 0f);
            else if (value != null) variables[name] = value == "1" ? on : off;
            else variables[name] = Mathf.Approximately(now, on) ? off : on;
            ShowAll();
        }

        private static void WriteVariables()
        {
            if (variables.Count == 0) return;
            FlightSceneScript flight = FlightSceneScript.Instance;
            var aircraft = flight != null && flight.LocalPlayer != null ? flight.LocalPlayer.Aircraft : null;
            if (aircraft == null || aircraft.VariableSystem == null) return;
            if (!ReferenceEquals(variablesOf, aircraft))
            {
                // another craft starts with its own values
                if (variablesOf != null) variables.Clear();
                variablesOf = aircraft;
            }
            foreach (KeyValuePair<string, float> v in variables)
            {
                try
                {
                    var variable = aircraft.VariableSystem.Variables.FirstOrDefault(x => x.Name == v.Key) ?? aircraft.VariableSystem.AddVariable(v.Key);
                    if (variable != null) variable.SetValue(v.Value, 0);
                }
                catch (Exception e)
                {
                    if (variableProblem != v.Key) EditorPlugin.Log.LogWarning("[UIEDIT] variable '" + v.Key + "': " + e.Message);
                    variableProblem = v.Key;
                }
            }
        }

        // ---- what they show ----

        private static float nextShow;

        public static void Tick()
        {
            WriteVariables();
            if (bindings.Count == 0 || Time.unscaledTime < nextShow) return;
            nextShow = Time.unscaledTime + 0.3f;
            foreach (int h in bindings.Where(x => x.Value.Widget == null).Select(x => x.Key).ToList()) bindings.Remove(h);
            ShowAll();
        }

        private static void ShowAll() { foreach (Binding b in bindings.Values) Show(b); }

        private static void Show(Binding b)
        {
            if (b.Widget == null || (b.Toggle == null && b.Slider == null && b.Text == null)) return;
            b.Writing = true;
            try
            {
                float number = 0f;
                string words = null;
                bool on = false;
                if (b.Kind == "config")
                {
                    ConfigEntryBase e = Entry(b);
                    if (e == null) return;
                    words = e.GetSerializedValue();
                    if (e.SettingType == typeof(bool)) on = (bool)e.BoxedValue;
                    else if (IsNumber(e.SettingType)) number = Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture);
                }
                else
                {
                    variables.TryGetValue(Arg(b, "name") ?? "", out number);
                    on = Mathf.Approximately(number, Parse(Arg(b, "max"), 1f));
                }
                if (b.Toggle != null && b.Toggle.IsOn != on) b.Toggle.IsOn = on;
                if (b.Slider != null && !Mathf.Approximately(b.Slider.Value, number)) b.Slider.SetValue(number);
                if (b.Text != null && b.Text.Text != words) b.Text.Text = words;
            }
            catch (Exception e)
            {
                if (b.Problem == null) EditorPlugin.Log.LogWarning("[UIEDIT] showing " + b.Kind + ": " + e.Message);
                b.Problem = e.Message;
            }
            finally { b.Writing = false; }
        }

        public static void ForgetMods() { entries.Clear(); }
    }
}
