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
            History.Add(author, Name(w) + ": " + attribute + " = " + value + (before.Own ? " (was " + before.Value + ")" : ""),
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
            History.Add(author, Name(w) + ": " + (on ? "+ " : "- ") + "class " + name, () => put(!on), () => put(on), false);
            return null;
        }

        // ---- adding and removing ----

        public static string Add(Widget parent, XElement element, int index, Dictionary<string, string> texts, string author, out Widget made)
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
            made = Make(parent, kept, index, texts);
            if (made == null) return "the game could not make it (see the log)";
            int h = Tree.Handle(made);
            added.Add(h);
            recipes[h] = kept;
            recipeTexts[h] = texts;
            string what = (string)element.Attribute("templateId") ?? element.Name.LocalName;
            History.Add(author, "added " + what + " to " + Name(parent), () => RemoveHandle(h), () => MakeAgain(parentHandle, h, kept, index, texts));
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
            History.Add(author, "removed " + name, () => MakeAgain(parentHandle, h, element, index, texts), () => RemoveHandle(h), false);
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
            History.Add(author, "moved " + Name(w) + " to place " + (newIndex + 1) + " in " + Name(parent), () => put(from, oldIndex), () => put(to, newIndex));
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
