using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Jundroo.Juicy;
using Jundroo.Juicy.Widgets;
using Jundroo.Juicy.Widgets.Serialization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Attribute = Jundroo.Juicy.Widgets.Serialization.Attribute;

namespace SP2UIEditor
{
    // Reading the widgets on the screen. A widget is named by a number that stays the same while it lives.
    internal static class Tree
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly PropertyInfo StyleProperty = typeof(Widget).GetProperty("Style", Any);
        private static readonly PropertyInfo SetProperty = typeof(Widget).GetProperty("AttributeSet", Any);
        private static readonly FieldInfo ClassesField = typeof(Widget).GetField("_classNames", Any);
        private static readonly FieldInfo DynamicField = typeof(Widget).GetField("_dynamicValues", Any);
        private static readonly FieldInfo TemplatesField = typeof(Stylesheet).GetField("_templates", Any);
        private static readonly FieldInfo StylesField = typeof(Stylesheet).GetField("_styles", Any);

        private static readonly Dictionary<int, Widget> byHandle = new Dictionary<int, Widget>();
        private static readonly Dictionary<int, int> handles = new Dictionary<int, int>();   // instance id -> handle
        private static int nextHandle = 1;

        public static int Handle(Widget w)
        {
            int h, id = w.GetInstanceID();
            if (handles.TryGetValue(id, out h)) return h;
            h = nextHandle++;
            handles[id] = h;
            byHandle[h] = w;
            return h;
        }

        // A widget that was made again (undoing its removal) keeps the number of the one it replaces.
        public static void Rebind(int handle, Widget w)
        {
            byHandle[handle] = w;
            handles[w.GetInstanceID()] = handle;
        }

        public static Widget Get(int handle)
        {
            Widget w;
            return byHandle.TryGetValue(handle, out w) && w != null ? w : null;
        }

        public static void Prune()
        {
            foreach (int h in byHandle.Where(x => x.Value == null).Select(x => x.Key).ToList()) byHandle.Remove(h);
            HashSet<int> live = new HashSet<int>(byHandle.Values.Select(x => x.GetInstanceID()));
            foreach (int id in handles.Keys.Where(x => !live.Contains(x)).ToList()) handles.Remove(id);
        }

        public static List<Widget> Roots()
        {
            return UnityEngine.Object.FindObjectsByType<Widget>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(w => w.Parent == null && w.Context != null && w.Stylesheet != null).OrderByDescending(SortOrder).ToList();
        }

        private static int SortOrder(Widget w)
        {
            Canvas c = w.GetComponentInParent<Canvas>(true);
            return c != null ? c.rootCanvas.sortingOrder : 0;
        }

        public static string TypeName(Widget w)
        {
            string n = w.GetType().Name;
            return n.EndsWith("Widget") && n.Length > 6 ? n.Substring(0, n.Length - 6) : w.Element != null ? w.Element.Name.LocalName : n;
        }

        public static string Src(Widget w)
        {
            Patches.Instance i = Patches.InstanceOf(w);
            if (i != null && i.Src != null) return i.Src;
            return w.Element != null ? (string)w.Element.Attribute(Sources.Mark) : null;
        }

        public static WidgetStyle Style(Widget w) { return (WidgetStyle)StyleProperty.GetValue(w); }
        public static AttributeSet Set(Widget w) { return (AttributeSet)SetProperty.GetValue(w); }
        public static List<string> Classes(Widget w) { return (List<string>)ClassesField.GetValue(w); }
        public static Dictionary<string, WidgetStyle> Styles(Stylesheet s) { return (Dictionary<string, WidgetStyle>)StylesField.GetValue(s); }
        public static Dictionary<string, XElement> Templates(Stylesheet s) { return (Dictionary<string, XElement>)TemplatesField.GetValue(s); }

        // The words an element shows: its own text, or the first text inside it when it is a button or was made from a template.
        public static string Words(Widget w)
        {
            TextWidget t = w as TextWidget;
            if (t != null) return t.TextMeshPro != null ? t.TextMeshPro.text : null;
            if (w is ButtonWidget || Patches.InstanceOf(w) != null)
            {
                TMP_Text inner = w.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.text));
                return inner != null ? inner.text : null;
            }
            return null;
        }

        private static string Short(string s, int n = 80)
        {
            if (s == null) return null;
            s = s.Replace('\n', ' ');
            return s.Length > n ? s.Substring(0, n) + "..." : s;
        }

        public static JObject Node(Widget w, int depth, bool visibleOnly)
        {
            Patches.Instance instance = Patches.InstanceOf(w);
            JObject o = new JObject { ["h"] = Handle(w), ["type"] = TypeName(w) };
            if (!string.IsNullOrEmpty(w.Id)) o["id"] = w.Id;
            if (instance != null) o["template"] = instance.Template;
            string words = Short(Words(w));
            if (!string.IsNullOrEmpty(words)) o["text"] = words;
            if (!w.gameObject.activeInHierarchy) o["hidden"] = true;
            if (LiveEditor.IsAdded(w)) o["added"] = true;
            int kids = w.Widgets.Count;
            if (kids > 0)
            {
                o["n"] = kids;
                if (depth != 0)
                    o["kids"] = new JArray(w.Widgets.Where(x => x != null && (!visibleOnly || x.gameObject.activeInHierarchy))
                        .Select(x => Node(x, depth - 1, visibleOnly)));
            }
            return o;
        }

        public static JArray Screens(bool visibleOnly)
        {
            JArray list = new JArray();
            foreach (Widget w in Roots())
            {
                if (visibleOnly && !w.gameObject.activeInHierarchy) continue;
                JObject o = Node(w, 0, visibleOnly);
                o["file"] = Sources.FileOf(Src(w));
                o["object"] = w.transform.root.name + "/" + w.gameObject.name;
                o["scene"] = w.gameObject.scene.name;
                list.Add(o);
            }
            return list;
        }

        public static JArray Find(string text, bool visibleOnly, int limit)
        {
            JArray found = new JArray();
            if (text.Length == 0) return found;
            foreach (Widget root in Roots()) FindIn(root, text, visibleOnly, limit, found);
            return found;
        }

        private static void FindIn(Widget w, string text, bool visibleOnly, int limit, JArray found)
        {
            if (found.Count >= limit || (visibleOnly && !w.gameObject.activeInHierarchy)) return;
            string words = w is TextWidget ? Words(w) : null;
            if (Has(w.Id, text) || Has(words, text) || Has(w.Tooltip, text))
            {
                JObject o = Node(w, 0, false);
                o["path"] = Path(w);
                found.Add(o);
            }
            foreach (Widget child in w.Widgets) if (child != null) FindIn(child, text, visibleOnly, limit, found);
        }

        private static bool Has(string s, string part) { return s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }

        public static string NameOf(Widget w) { return string.IsNullOrEmpty(w.Id) ? TypeName(w) : w.Id; }

        public static string Path(Widget w)
        {
            return string.Join(" > ", Ancestors(w).Concat(new[] { w }).Select(NameOf));
        }

        public static List<Widget> Ancestors(Widget w)
        {
            List<Widget> list = new List<Widget>();
            for (Widget at = w.Parent; at != null; at = at.Parent) list.Add(at);
            list.Reverse();
            return list;
        }

        public static JObject Details(Widget w)
        {
            JObject o = Node(w, 0, false);
            o["path"] = Path(w);
            o["ancestors"] = new JArray(Ancestors(w).Select(x => new JObject { ["h"] = Handle(x), ["name"] = NameOf(x) }));
            o["fullText"] = Words(w);
            o["classes"] = new JArray(Classes(w).Where(x => !x.Contains(" ")));
            o["index"] = w.Parent != null ? w.Index : 0;
            o["siblings"] = w.Parent != null ? w.Parent.Widgets.Count : 1;

            o["identity"] = Identity.Of(w);
            Actions.Binding action = Actions.Of(w);
            if (action != null) o["action"] = new JObject { ["kind"] = action.Kind, ["args"] = JObject.FromObject(action.Args) };
            string src = Src(w), file = Sources.FileOf(src);
            o["src"] = src;
            XElement raw;
            if (file != null && (raw = Patches.RawOf(file)) != null)
            {
                XElement element = Sources.Find(raw, src);
                if (element != null)
                {
                    o["source"] = new XElement(element.Name, element.Attributes()).ToString();
                    o["inTemplate"] = Sources.InTemplate(element);
                }
            }

            Patches.Instance instance = Patches.InstanceOf(w);
            if (instance != null) o["templateAttributes"] = JObject.FromObject(instance.Attributes);

            WidgetStyle style = Style(w);
            Dictionary<string, string> own = style != null ? style.Attributes : new Dictionary<string, string>();
            IDictionary dynamic = DynamicField.GetValue(w) as IDictionary;
            Dictionary<string, string> fromClasses = new Dictionary<string, string>();
            if (w.Stylesheet != null)
                foreach (WidgetStyle s in Classes(w).Select(x => w.Stylesheet.GetStyle(x)).Where(x => x != null).OrderBy(x => x.Order))
                    foreach (KeyValuePair<string, string> a in s.Attributes) fromClasses[a.Key] = a.Value;

            JArray attributes = new JArray();
            foreach (Attribute a in Set(w).Attributes.Values)
            {
                JObject x = new JObject { ["name"] = a.Name, ["kind"] = Kind(a) };
                EnumAttributeBase e = a as EnumAttributeBase;
                if (e != null) x["options"] = new JArray(Enum.GetNames(e.EnumType));
                string value;
                if (own.TryGetValue(a.Name, out value)) { x["value"] = value; x["own"] = true; }
                else if (fromClasses.TryGetValue(a.Name, out value)) x["value"] = value;
                if (!SafeXml.AttributeAllowed(a.Name)) x["locked"] = true;
                if (dynamic != null && dynamic.Contains(a.Name)) x["dynamic"] = true;
                if (LiveEditor.Touched(w, a.Name)) x["edited"] = true;
                attributes.Add(x);
            }
            o["attributes"] = attributes;

            Rect r;
            if (ScreenRect(w, out r)) o["rect"] = RectJson(r);
            TextWidget text = w as TextWidget;
            if (text != null && w.Element != null)
            {
                string original = (string)w.Element.Attribute("text"), now = Words(w);
                o["textSetByGame"] = !LiveEditor.Touched(w, "text") && original != null && now != original;
            }
            return o;
        }

        // bool, color, colorblock, enum, float, int, rectoffset, sound, string, vector2, animation
        public static string Kind(Attribute a)
        {
            string n = a.GetType().Name;
            int tick = n.IndexOf('`');
            if (tick >= 0) n = n.Substring(0, tick);
            return n.Replace("Attribute", "").ToLowerInvariant();
        }

        public static JObject RectJson(Rect r)
        {
            // as shares of the screen, from the top left corner
            return new JObject
            {
                ["x"] = r.xMin / Screen.width, ["y"] = 1f - r.yMax / Screen.height,
                ["w"] = r.width / Screen.width, ["h"] = r.height / Screen.height,
            };
        }

        private static readonly Vector3[] corners = new Vector3[4];

        public static bool ScreenRect(Widget w, out Rect rect)
        {
            rect = default(Rect);
            if (w == null || w.Rect == null) return false;
            Canvas canvas = w.GetComponentInParent<Canvas>(true);
            if (canvas == null) return false;
            canvas = canvas.rootCanvas;
            if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null) return false;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            w.Rect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        // The element on top at a point of the screen (pixels from the bottom left): among those that draw something there, the one drawn last.
        public static Widget At(Vector2 point)
        {
            Widget best = null;
            int bestOrder = int.MinValue;
            foreach (Widget root in Roots())
            {
                if (!root.gameObject.activeInHierarchy) continue;
                int order = SortOrder(root);
                if (best != null && order <= bestOrder && !DrawnAfter(root, best)) continue;
                Widget hit = Hit(root, point, 1f);
                if (hit != null) { best = hit; bestOrder = order; }
            }
            return best;
        }

        // On one canvas what comes later among the children is drawn on top.
        private static bool DrawnAfter(Widget a, Widget b)
        {
            Canvas ca = a.GetComponentInParent<Canvas>(true), cb = b.GetComponentInParent<Canvas>(true);
            if (ca == null || cb == null || ca.rootCanvas != cb.rootCanvas) return false;
            List<Transform> pa = Chain(a.transform), pb = Chain(b.transform);
            for (int i = 0; i < pa.Count && i < pb.Count; i++)
                if (pa[i] != pb[i]) return pa[i].GetSiblingIndex() > pb[i].GetSiblingIndex();
            return false;
        }

        private static List<Transform> Chain(Transform t)
        {
            List<Transform> list = new List<Transform>();
            for (; t != null; t = t.parent) list.Add(t);
            list.Reverse();
            return list;
        }

        private static Widget Hit(Widget w, Vector2 point, float alpha)
        {
            if (!w.gameObject.activeInHierarchy) return null;
            CanvasGroup group = w.GetComponent<CanvasGroup>();
            if (group != null) alpha *= group.alpha;
            if (alpha < 0.05f) return null;
            Rect r;
            bool inside = ScreenRect(w, out r) && r.Contains(point);
            // what a mask cuts off is not on the screen
            if (!inside && (w.GetComponent<RectMask2D>() != null || w.GetComponent<Mask>() != null || w is ScrollViewWidget)) return null;
            for (int i = w.Widgets.Count - 1; i >= 0; i--)
            {
                if (w.Widgets[i] == null) continue;
                Widget hit = Hit(w.Widgets[i], point, alpha);
                if (hit != null) return hit;
            }
            if (!inside) return null;
            Graphic g = w.GetComponent<Graphic>();
            if (g == null) g = w is TextWidget ? ((TextWidget)w).TextMeshPro : null;
            return g != null && g.enabled && g.color.a * alpha >= 0.05f ? w : null;
        }
    }
}
