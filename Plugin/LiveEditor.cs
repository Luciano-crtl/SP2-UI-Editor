using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;
using UnityEngine;
using UnityEngine.UI;
using Attribute = Jundroo.Juicy.Widgets.Serialization.Attribute;

namespace SP2UIEditor
{
    // Changing the widgets on the screen. Every change is a step that can be undone. Methods return null, or why it was not done.
    internal static class LiveEditor
    {
        public static readonly History History = new History();

        private static readonly MethodInfo SetDirty = typeof(Widget).GetMethod("SetDirtyFlag", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ChildrenField = typeof(Widget).GetField("_widgets", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly HashSet<int> added = new HashSet<int>();                  // handles
        private static readonly HashSet<string> touched = new HashSet<string>();          // "handle attribute"
        private static readonly Dictionary<int, XElement> recipes = new Dictionary<int, XElement>();
        private static readonly Dictionary<int, Dictionary<string, string>> recipeTexts = new Dictionary<int, Dictionary<string, string>>();

        private static readonly Dictionary<int, string> uids = new Dictionary<int, string>();   // handle of an added element -> its saved name
        private static List<Step> sink;                                                         // replaying a package: the steps go here, not to the history

        public static string UidOf(Widget w)
        {
            if (uids.Count == 0 || w == null) return null;
            string uid;
            return uids.TryGetValue(Tree.Handle(w), out uid) ? uid : null;
        }

        private static void Record(Widget w, Op op, string author, string text, Func<string> undo, Func<string> redo, bool exact = true)
        {
            Step step = new Step { Author = author, Text = text, Undo = undo, Redo = redo, Exact = exact, Op = op, Handle = w != null ? Tree.Handle(w) : 0 };
            if (op != null) op.Text = text;
            if (sink != null) { sink.Add(step); return; }
            if (w != null) Packages.Mark(w, op);
            History.Add(step);
        }

        // A saved edit, done again to a widget of a screen that was just built.
        public static string Replay(Op op, Widget w, Widget parent, out Step step, out Widget made)
        {
            made = null;
            step = null;
            List<Step> steps = new List<Step>();
            sink = steps;
            string text = op.Text, problem;
            try
            {
                switch (op.Do)
                {
                    case "set": problem = Set(w, op.Name, op.Value, "package"); break;
                    case "class": problem = w.HasClass(op.Name) == op.On ? null : Class(w, op.Name, op.On, "package"); break;
                    case "add": problem = Add(w, XElement.Parse(op.Xml), op.Index, op.Texts, "package", out made, op.Uid); break;
                    case "move": problem = parent == null ? "its new parent is not there" : Move(w, parent, op.Index, "package"); break;
                    case "action": problem = Act(w, op.Name, op.Args, "package"); break;
                    default: problem = "unknown edit"; break;
                }
            }
            catch (Exception e) { problem = e.GetBaseException().Message; }
            finally { sink = null; }
            op.Text = text;
            step = steps.FirstOrDefault();
            return problem;
        }

        private const string Gone = "that element no longer exists (its screen was closed)";

        public static bool IsAdded(Widget w)
        {
            if (added.Count == 0) return false;
            for (Widget at = w; at != null; at = at.Parent) if (added.Contains(Tree.Handle(at))) return true;
            return false;
        }

        public static bool Touched(Widget w, string attribute) { return touched.Contains(Tree.Handle(w) + " " + attribute); }

        private static string Name(Widget w)
        {
            string words = Tree.Words(w);
            return !string.IsNullOrEmpty(words) ? "\"" + (words.Length > 30 ? words.Substring(0, 30) + "..." : words) + "\"" : Tree.NameOf(w);
        }

        private static void Dirty(Widget w, Widget.DirtyFlags flags) { SetDirty.Invoke(w, new object[] { flags, false }); }

        private static void Refresh(Widget w)
        {
            if (w == null) return;
            Dirty(w, Widget.DirtyFlags.ApplyStyes | Widget.DirtyFlags.UpdateLayout);
            if (w.Parent != null) Dirty(w.Parent, Widget.DirtyFlags.UpdateLayout);
            if (w.Rect != null) LayoutRebuilder.MarkLayoutForRebuild(w.Rect);
        }

        // ---- one attribute ----

        // What an attribute's setter can change besides the style, kept so that undoing really brings it back.
        private sealed class Before
        {
            public bool Own, Visible, Exact;
            public string Value;
            public Vector2 AnchorMin, AnchorMax, Pivot, SizeDelta, Anchored;
            public Vector3 Scale;
            public Quaternion Rotation;
            public PropertyInfo Property;
            public object PropertyValue;
            public Color? Color;
        }

        private static readonly string[] RectAttributes = { "anchor", "anchorMin", "anchorMax", "pivot", "rotation", "scale", "position" };

        private static readonly Dictionary<string, string> PropertyNames = new Dictionary<string, string>
        {
            { "colSpan", "ColumnSpan" }, { "colOffset", "ColumnOffset" }, { "colStart", "ColumnStart" }, { "colPadding", "ColumnPadding" },
        };

        private static Before Keep(Widget w, string attribute)
        {
            Before b = new Before();
            b.Own = Tree.Style(w).Attributes.TryGetValue(attribute, out b.Value);
            RectTransform r = w.Rect;
            b.AnchorMin = r.anchorMin; b.AnchorMax = r.anchorMax; b.Pivot = r.pivot; b.SizeDelta = r.sizeDelta;
            b.Anchored = r.anchoredPosition; b.Scale = r.localScale; b.Rotation = r.localRotation; b.Visible = w.Visible;
            bool fromClass = Tree.Classes(w).Select(c => w.Stylesheet.GetStyle(c)).Any(s => s != null && s.GetAttribute(attribute) != null);
            b.Property = PropertyOf(w, attribute);
            if (b.Property != null) b.PropertyValue = Copy(b.Property.GetValue(w));
            if (attribute.StartsWith("color", StringComparison.OrdinalIgnoreCase))
            {
                Graphic g = GraphicOf(w);
                if (g != null) b.Color = g.color;
            }
            b.Exact = b.Own || fromClass || b.Property != null || b.Color.HasValue || RectAttributes.Contains(attribute);
            return b;
        }

        private static Graphic GraphicOf(Widget w)
        {
            TextWidget t = w as TextWidget;
            return t != null ? t.TextMeshPro : w.GetComponent<Graphic>();
        }

        private static PropertyInfo PropertyOf(Widget w, string attribute)
        {
            string name;
            if (!PropertyNames.TryGetValue(attribute, out name)) name = char.ToUpperInvariant(attribute[0]) + attribute.Substring(1);
            PropertyInfo p;
            try { p = w.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public); }
            catch (AmbiguousMatchException) { return null; }
            if (p == null || !p.CanRead || p.GetSetMethod() == null || p.GetIndexParameters().Length > 0) return null;
            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector2) || t == typeof(RectOffset) || t == typeof(Color) ? p : null;
        }

        private static object Copy(object o)
        {
            RectOffset r = o as RectOffset;
            return r != null ? new RectOffset(r.left, r.right, r.top, r.bottom) : o;
        }

        private static void Restore(Widget w, string attribute, Before b)
        {
            if (b.Property != null) b.Property.SetValue(w, Copy(b.PropertyValue));
            // after the property: a text's setter writes the style too
            Dictionary<string, string> own = Tree.Style(w).Attributes;
            if (b.Own) own[attribute] = b.Value; else own.Remove(attribute);
            RectTransform r = w.Rect;
            r.anchorMin = b.AnchorMin; r.anchorMax = b.AnchorMax; r.pivot = b.Pivot; r.sizeDelta = b.SizeDelta;
            r.anchoredPosition = b.Anchored; r.localScale = b.Scale; r.localRotation = b.Rotation;
            if (b.Color.HasValue)
            {
                Graphic g = GraphicOf(w);
                if (g != null) g.color = b.Color.Value;
            }
            if (attribute == "visible") w.Visible = b.Visible;
            Refresh(w);
        }

        public static string Set(Widget w, string attribute, string value, string author)
        {
            if (string.IsNullOrEmpty(attribute) || !SafeXml.AttributeAllowed(attribute)) return "the attribute '" + attribute + "' can't be edited";
            if (!SafeXml.ValueAllowed(value)) return "a value with { } is an expression the game would evaluate; it is not accepted";
            Attribute a;
            if (!Tree.Set(w).Attributes.TryGetValue(attribute, out a)) return "a " + Tree.TypeName(w) + " has no attribute '" + attribute + "'";
            if (w.Stylesheet == null) return "this element is not ready yet";
            string resolved = w.Stylesheet.ReplaceConstants(value ?? "");
            string wrong = Values.Problem(Tree.Kind(a), resolved);
            if (wrong != null) return "'" + value + "' is not a value for " + attribute + ": " + wrong;
            string named = Name(w);
            Before before = Keep(w, attribute);

            // Tried on its own first: the game's own pass would only write an error to the log and leave the bad value in place.
            try { a.Apply(w, resolved); }
            catch (Exception e)
            {
                Restore(w, attribute, before);
                return "'" + value + "' is not a value for " + attribute + ": " + e.GetBaseException().Message;
            }
            w.SetStyle(attribute, resolved);
            Refresh(w);

            int h = Tree.Handle(w);
            string key = h + " " + attribute;
            bool first = touched.Add(key);
            string id = sink == null ? Identity.Of(w) : null;
            Op op = id == null ? null : new Op { Do = "set", Target = id, Name = attribute, Value = value, Prints = Identity.Prints(id) };
            Record(w, op, author, named + ": " + attribute + " = " + value + (before.Own ? " (was " + before.Value + ")" : ""),
                () =>
                {
                    Widget at = Tree.Get(h);
                    if (at == null) return Gone;
                    Restore(at, attribute, before);
                    if (first) touched.Remove(key);
                    return null;
                },
                () =>
                {
                    Widget at = Tree.Get(h);
                    if (at == null) return Gone;
                    a.Apply(at, resolved);
                    at.SetStyle(attribute, resolved);
                    Refresh(at);
                    touched.Add(key);
                    return null;
                }, before.Exact);
            return null;
        }

        // ---- classes ----

        public static string Class(Widget w, string name, bool on, string author)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains(" ")) return "one class name is needed";
            if (w.HasClass(name) == on) return on ? "it already has that class" : "it doesn't have that class";
            int h = Tree.Handle(w);
            Func<bool, string> put = x =>
            {
                Widget at = Tree.Get(h);
                if (at == null) return Gone;
                at.EnableClass(name, x);
                Refresh(at);
                return null;
            };
            string problem = put(on);
            if (problem != null) return problem;
            string id = sink == null ? Identity.Of(w) : null;
            Op op = id == null ? null : new Op { Do = "class", Target = id, Name = name, On = on, Prints = Identity.Prints(id) };
            Record(w, op, author, Name(w) + ": " + (on ? "+ " : "- ") + "class " + name, () => put(!on), () => put(on), false);
            return null;
        }

        // ---- adding and removing ----

        public static string Add(Widget parent, XElement element, int index, Dictionary<string, string> texts, string author, out Widget made, string uid = null)
        {
            made = null;
            string problem = SafeXml.Problem(element);
            if (problem != null) return problem;
            if (parent is ScrollViewWidget && parent.Widgets.Count > 0) return "a scroll view holds a single element; add inside that one";
            if (parent is TextWidget) return "a text holds no other elements; add next to it";
            foreach (XElement e in element.DescendantsAndSelf("Instance"))
                if (parent.Stylesheet.GetTemplate((string)e.Attribute("templateId") ?? "") == null)
                    return "this screen has no template '" + (string)e.Attribute("templateId") + "'";

            XElement kept = new XElement(element);
            int parentHandle = Tree.Handle(parent);
            string id = sink == null ? Identity.Of(parent) : null;
            made = Make(parent, kept, index, texts);
            if (made == null) return "the game could not make it (see the log)";
            int h = Tree.Handle(made);
            added.Add(h);
            if (uid == null) uid = Guid.NewGuid().ToString("N").Substring(0, 12);
            uids[h] = uid;
            // the screen was built again: the changes made to the earlier one go on with this one
            foreach (int old in uids.Where(x => x.Key != h && x.Value == uid && Tree.Get(x.Key) == null).Select(x => x.Key).ToList())
            {
                uids.Remove(h);
                added.Remove(h);
                Tree.Rebind(old, made);
                h = old;
                break;
            }
            recipes[h] = kept;
            recipeTexts[h] = texts;
            string what = (string)element.Attribute("templateId") ?? element.Name.LocalName;
            Op op = id == null ? null : new Op { Do = "add", Target = id, Uid = uid, Xml = kept.ToString(SaveOptions.DisableFormatting), Texts = texts, Index = index, Prints = Identity.Prints(id) };
            Record(parent, op, author, "added " + what + " to " + Name(parent), () => RemoveHandle(h), () => MakeAgain(parentHandle, h, kept, index, texts));
            return null;
        }

        private static string RemoveHandle(int h)
        {
            Widget at = Tree.Get(h);
            if (at != null) Remove(at);
            return null;
        }

        private static string MakeAgain(int parentHandle, int h, XElement element, int index, Dictionary<string, string> texts)
        {
            Widget p = Tree.Get(parentHandle);
            if (p == null) return Gone;
            Widget again = Make(p, element, index, texts);
            if (again == null) return "the game could not make it again";
            Tree.Rebind(h, again);
            return null;
        }

        private static Widget Make(Widget parent, XElement element, int index, Dictionary<string, string> texts)
        {
            int before = parent.Widgets.Count;
            Widget w;
            try
            {
                // a copy: Juicy writes the constants' values into the element
                w = parent.Context.CreateWidget(new XElement(element), parent, parent.Stylesheet);
            }
            catch (Exception e)
            {
                EditorPlugin.Log.LogWarning("[UIEDIT] adding failed: " + e);
                while (parent.Widgets.Count > before) Remove(parent.Widgets[parent.Widgets.Count - 1]);
                return null;
            }
            if (w == null) return null;
            if (texts != null)
                foreach (KeyValuePair<string, string> t in texts)
                {
                    TextWidget text = w.FindWidget<TextWidget>(t.Key);
                    if (text != null) text.Text = t.Value;
                }
            if (index >= 0 && index < parent.Widgets.Count - 1) w.SetIndex(index);
            Refresh(w);
            return w;
        }

        private static void Remove(Widget w)
        {
            Widget parent = w.Parent;
            // Out of its parent's list now: Juicy only does it when Unity destroys the object, at the end of the frame.
            if (parent != null) ((List<Widget>)ChildrenField.GetValue(parent)).Remove(w);
            if (Picker.Selected == w) Picker.Select(null);
            w.Destroy();
            if (parent != null) Refresh(parent);
        }

        // Only what was added here is really removed. The game's scripts keep references to its own elements, so those are hidden instead.
        public static string Delete(Widget w, string author)
        {
            int h = Tree.Handle(w);
            if (w.Parent == null) return "a whole screen can't be removed";
            if (!added.Contains(h)) return Set(w, "visible", "false", author);
            Op op = sink == null ? new Op { Do = "remove", Target = "add:" + uids[h] } : null;
            Widget oldParent = w.Parent;
            int index = w.Index, parentHandle = Tree.Handle(w.Parent);
            Dictionary<string, string> texts;
            recipeTexts.TryGetValue(h, out texts);
            XElement element = new XElement(recipes[h]);
            if (element.Name.LocalName != "Instance")
                foreach (KeyValuePair<string, string> a in Tree.Style(w).Attributes)
                    if (touched.Contains(h + " " + a.Key)) element.SetAttributeValue(a.Key, a.Value);
            string name = Name(w);
            Remove(w);
            // what was changed inside it afterwards does not come back: undoing it is not exact
            Record(oldParent, op, author, "removed " + name, () => MakeAgain(parentHandle, h, element, index, texts), () => RemoveHandle(h), false);
            return null;
        }

        // ---- actions and panels ----

        public static string Act(Widget w, string kind, Dictionary<string, string> args, string author)
        {
            string problem = Op.ActionProblem(kind, args);
            if (problem != null) return problem;
            int h = Tree.Handle(w);
            Actions.Binding old = Actions.Of(w);
            string oldKind = old != null ? old.Kind : "none";
            Dictionary<string, string> oldArgs = old != null ? old.Args : null;
            Func<string, Dictionary<string, string>, string> put = (k, a) =>
            {
                Widget at = Tree.Get(h);
                if (at == null) return Gone;
                string p = null;
                if (k == "none") Actions.Unbind(at); else Actions.Bind(at, k, a, out p);
                return p;
            };
            problem = put(kind, args);
            if (problem != null) return problem;
            string id = sink == null ? Identity.Of(w) : null;
            Op op = id == null ? null : new Op { Do = "action", Target = id, Name = kind, Args = args, Prints = Identity.Prints(id) };
            Record(w, op, author, Name(w) + ": action " + kind + (args == null ? "" : " " + string.Join(", ", args.Where(x => x.Key != "target").Select(x => x.Value))), () => put(oldKind, oldArgs), () => put(kind, args));
            return null;
        }

        public static string NewPanel(string title, string author, out string uid)
        {
            uid = Guid.NewGuid().ToString("N").Substring(0, 12);
            string made = uid;
            Op op = new Op { Do = "panel", Target = "panel:" + uid, Uid = uid, Name = (title ?? "").Trim(), Value = "480" };
            string problem = op.Problem();
            if (problem != null) return problem;
            Record(null, op, author, "new panel \"" + op.Name + "\"", () => { Panels.Close(made); return null; }, () => null);
            return Panels.Open(uid);
        }

        public static string RemovePanel(string uid, string author)
        {
            Op def = Panels.Def(uid);
            if (def == null) return "no such panel";
            Panels.Close(uid);
            Record(null, new Op { Do = "remove", Target = "panel:" + uid }, author, "removed panel \"" + def.Name + "\"", () => null, () => { Panels.Close(uid); return null; });
            return null;
        }

        // ---- moving ----

        public static string Move(Widget w, Widget parent, int index, string author)
        {
            if (w.Parent == null) return "a whole screen can't be moved";
            for (Widget at = parent; at != null; at = at.Parent) if (at == w) return "an element can't go inside itself";
            if (parent != w.Parent)
            {
                if (parent is ScrollViewWidget || w.Parent is ScrollViewWidget) return "a scroll view holds a single element; it can't be moved in or out";
                if (parent is TextWidget) return "a text holds no other elements";
                if (w.Context != parent.Context) return "it can't move to another screen";
            }
            string id = sink == null ? Identity.Of(w) : null, idTo = sink == null ? Identity.Of(parent) : null;
            int h = Tree.Handle(w), from = Tree.Handle(w.Parent), to = Tree.Handle(parent), oldIndex = w.Index;
            Func<int, int, string> put = (p, i) =>
            {
                Widget at = Tree.Get(h), target = Tree.Get(p);
                if (at == null || target == null) return Gone;
                Widget left = at.Parent;
                if (target != left) target.AddWidget(at);
                if (i >= 0) at.SetIndex(i);
                Refresh(at);
                if (left != null) Refresh(left);
                return null;
            };
            string problem = put(to, index);
            if (problem != null) return problem;
            int newIndex = w.Index;
            Op op = id == null ? null : new Op { Do = "move", Target = id, Parent = idTo, Index = newIndex, Prints = Identity.Prints(id, idTo) };
            Record(w, op, author, "moved " + Name(w) + " to place " + (newIndex + 1) + " in " + Name(parent), () => put(from, oldIndex), () => put(to, newIndex));
            return null;
        }

        // The plugin is being unloaded: the screens go back to what they were.
        public static void UndoAll()
        {
            while (History.Done.Count > 0)
            {
                Step step;
                History.Undo(out step);
            }
        }
    }
}
