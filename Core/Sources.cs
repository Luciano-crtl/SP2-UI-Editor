using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SP2UIEditor
{
    // Where an element comes from. Every element of a layout gets a "_src" attribute ("Xml/Flight/FlightUI#1.0.3": the file, and the place of the
    // element in it as child indexes from the document's root). Juicy ignores attributes it does not know and keeps them when it copies templates,
    // so each widget on the screen can say which element of which file made it.
    public static class Sources
    {
        public const string Mark = "_src";

        public static void Stamp(string path, XElement root)
        {
            int i = 0;
            foreach (XElement section in root.Elements())
            {
                string name = section.Name.LocalName, at = (i++).ToString();
                if (name == "Widgets" || name == "Templates") StampBelow(path, section, at);
                else if (name == "Stylesheets")
                {
                    // <Stylesheets><Stylesheet><Templates>: only the templates hold widgets (a mark on a <Style> would be taken as its value)
                    int s = 0;
                    foreach (XElement sheet in section.Elements())
                    {
                        int t = 0;
                        foreach (XElement part in sheet.Elements())
                        {
                            if (part.Name.LocalName == "Templates") StampBelow(path, part, at + "." + s + "." + t);
                            t++;
                        }
                        s++;
                    }
                }
            }
        }

        private static void StampBelow(string path, XElement element, string at)
        {
            int i = 0;
            foreach (XElement child in element.Elements())
            {
                string place = at + "." + i++;
                // the <Template> container is read as constants, not as a widget
                if (child.Name.LocalName != "Template") child.SetAttributeValue(Mark, path + "#" + place);
                StampBelow(path, child, place);
            }
        }

        public static void Unstamp(XElement root)
        {
            foreach (XElement e in root.DescendantsAndSelf()) e.SetAttributeValue(Mark, null);
        }

        public static XElement Find(XElement root, string src)
        {
            if (root == null || src == null) return null;
            int hash = src.LastIndexOf('#');
            if (hash < 0) return null;
            XElement at = root;
            foreach (string part in src.Substring(hash + 1).Split('.'))
            {
                int i;
                if (!int.TryParse(part, out i) || i < 0) return null;
                at = at.Elements().ElementAtOrDefault(i);
                if (at == null) return null;
            }
            return at;
        }

        public static string FileOf(string src)
        {
            int hash = src == null ? -1 : src.LastIndexOf('#');
            return hash < 0 ? null : src.Substring(0, hash);
        }

        // Inside a template: editing it there would change everything made from that template.
        public static bool InTemplate(XElement element)
        {
            return element != null && element.Ancestors("Template").Any();
        }
    }

    // What may be written through the editor. Scripts and event names run code, so they are refused until packages can be trusted (a later phase).
    public static class SafeXml
    {
        public static readonly string[] Elements =
        {
            "Empty", "Text", "Image", "RawImage", "Button", "Row", "VerticalLayout", "HorizontalLayout", "GridLayout", "ScrollView", "Spacer",
            "Slider", "Toggle", "Input", "ProgressBar", "Instance", "Section",
        };

        public static bool AttributeAllowed(string name)
        {
            if (string.IsNullOrEmpty(name) || name == Sources.Mark) return false;
            if (name.Equals("script", StringComparison.OrdinalIgnoreCase)) return false;
            return !(name.Length > 2 && name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && char.IsUpper(name[2]));
        }

        // "{...}" makes the game evaluate an expression; only plain values are taken.
        public static bool ValueAllowed(string value)
        {
            return value == null || value.IndexOf('{') < 0;
        }

        public static string Problem(XElement element)
        {
            foreach (XElement e in element.DescendantsAndSelf())
            {
                if (!Elements.Contains(e.Name.LocalName)) return "the element <" + e.Name.LocalName + "> can't be added";
                foreach (XAttribute a in e.Attributes())
                {
                    if (!AttributeAllowed(a.Name.LocalName)) return "the attribute '" + a.Name.LocalName + "' can't be set";
                    if (!ValueAllowed(a.Value)) return "the value of '" + a.Name.LocalName + "' can't contain an expression";
                }
                foreach (XText t in e.Nodes().OfType<XText>())
                    if (!ValueAllowed(t.Value)) return "a text can't contain an expression";
            }
            return null;
        }
    }

    // One undoable step. Undo and Redo return null, or the reason they could not be done.
    public sealed class Step
    {
        public int Number;
        public string Author;      // "user" or "claude"
        public string Text;        // what it did, for the list of changes
        public bool Exact = true;  // does undoing it bring back exactly what there was?
        public Func<string> Undo;
        public Func<string> Redo;
    }

    public sealed class History
    {
        private readonly List<Step> done = new List<Step>();
        private readonly List<Step> undone = new List<Step>();
        private int next = 1;

        public int Version { get; private set; }
        public IReadOnlyList<Step> Done { get { return done; } }
        public IReadOnlyList<Step> Undone { get { return undone; } }

        public Step Add(string author, string text, Func<string> undo, Func<string> redo, bool exact = true)
        {
            Step step = new Step { Number = next++, Author = author, Text = text, Undo = undo, Redo = redo, Exact = exact };
            done.Add(step);
            undone.Clear();
            Version++;
            return step;
        }

        // A step that can't be undone (its screen is gone) leaves the list: keeping it would block the ones below it.
        public string Undo(out Step step) { return Take(done, undone, s => s.Undo, "undo", out step); }
        public string Redo(out Step step) { return Take(undone, done, s => s.Redo, "redo", out step); }

        private string Take(List<Step> from, List<Step> to, Func<Step, Func<string>> what, string verb, out Step step)
        {
            step = from.LastOrDefault();
            if (step == null) return "nothing to " + verb;
            from.RemoveAt(from.Count - 1);
            Version++;
            string problem;
            try { problem = what(step)(); }
            catch (Exception e) { problem = e.GetBaseException().Message; }
            if (problem == null) to.Add(step);
            return problem;
        }
    }
}
