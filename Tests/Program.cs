using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SP2UIEditor;

// The core, tried on the game's own layouts (read from its resources.assets each time; nothing of the game is kept).
//   dotnet run --project Tests -- "C:\...\SimplePlanes 2_Data\resources.assets"
internal static class Program
{
    private static int failed;

    private static void Check(bool good, string what)
    {
        if (good) return;
        failed++;
        Console.WriteLine("FAILED: " + what);
    }

    private static List<XElement> Layouts(string assets)
    {
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(assets));
        List<XElement> list = new List<XElement>();
        foreach (string tag in new[] { "Xml", "Stylesheet" })
            for (int i = text.IndexOf("<" + tag + " xmlns"); i >= 0; i = text.IndexOf("<" + tag + " xmlns", i + 1))
            {
                int end = text.IndexOf("</" + tag + ">", i);
                if (end < 0 || end - i > 3000000) continue;
                string xml = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(text.Substring(i, end - i + tag.Length + 3)));
                try { list.Add(XElement.Parse(xml)); } catch { }
            }
        return list;
    }

    private static int Main(string[] args)
    {
        List<XElement> layouts = args.Length > 0 ? Layouts(args[0]) : new List<XElement>();
        int marks = 0;
        foreach (XElement original in layouts)
        {
            XElement x = new XElement(original);
            Sources.Stamp("Xml/T", x);
            Check(x.Descendants("Style").Concat(x.Descendants("Constant")).All(e => e.Attribute(Sources.Mark) == null), "a style or constant was marked");
            Check(x.Descendants("Template").All(e => e.Attribute(Sources.Mark) == null), "a template container was marked");
            HashSet<string> seen = new HashSet<string>();
            foreach (XElement e in x.Descendants().Where(e => e.Attribute(Sources.Mark) != null))
            {
                string src = (string)e.Attribute(Sources.Mark);
                marks++;
                Check(seen.Add(src), "two elements have the mark " + src);
                XElement found = Sources.Find(original, src);
                Check(found != null && found.Name == e.Name && found.Attributes().All(a => (string)e.Attribute(a.Name) == a.Value), src + " finds another element");
                Check(Sources.FileOf(src) == "Xml/T", "file of " + src);
            }
            foreach (XElement e in x.Descendants("Widgets").SelectMany(w => w.Descendants())) Check(e.Attribute(Sources.Mark) != null, "an element of Widgets has no mark");
            Sources.Unstamp(x);
            Check(XNode.DeepEquals(x, original), "marking and unmarking changed a layout");
        }
        if (args.Length > 0) Check(layouts.Count > 70, "only " + layouts.Count + " layouts were found");

        Check(Sources.Find(new XElement("Xml"), "a#9.9") == null && Sources.Find(new XElement("Xml"), "nothing") == null && Sources.Find(null, "a#0") == null, "a wrong place");
        Check(SafeXml.Problem(XElement.Parse("<Button class='btn'><Text text='x'/></Button>")) == null, "a plain button");
        foreach (string bad in new[] { "<Button onClick='Quit'/>", "<Empty script='A.B'/>", "<File path='x'/>", "<Text text='{Evil}'/>", "<Text>{Evil}</Text>",
                                       "<Row><Empty><Image onPointerDown='X'/></Empty></Row>", "<Instance templateId='control-header' script='X'/>", "<Text _src='x'/>", "<Canvas/>" })
            Check(SafeXml.Problem(XElement.Parse(bad)) != null, bad + " was accepted");
        Check(SafeXml.AttributeAllowed("opacity") && SafeXml.AttributeAllowed("on") && !SafeXml.AttributeAllowed("onSelect") && !SafeXml.AttributeAllowed("Script"), "attribute names");

        foreach (string good in new[] { "color #e8503a", "color #e8503aff", "color None", "color White:Linear 0.2", "float 1.5", "float -3", "float 1.025:OutBack 0.5", "int 12",
                                        "bool true", "bool False", "vector2 0.5 1", "vector2 -15 95", "rectoffset 10", "rectoffset 0 5 0 0", "string anything", "enum X" })
            Check(Values.Problem(good.Split(' ')[0], good.Substring(good.IndexOf(' ') + 1)) == null, "value " + good);
        foreach (string bad in new[] { "color banana", "color #12", "color #gg0000", "float abc", "float 1,5x", "float NaN", "int 1.5", "bool maybe", "vector2 a b", "rectoffset 1 2 3", "float @Nope" })
            Check(Values.Problem(bad.Split(' ')[0], bad.Substring(bad.IndexOf(' ') + 1)) != null, "value " + bad + " was accepted");

        // every value the game's own layouts use must be accepted
        string[] floats = { "width", "height", "fontSize", "opacity" }, offsets = { "margin" }, colors = { "color" };
        foreach (XAttribute a in layouts.SelectMany(x => x.Descendants()).Attributes())
        {
            if (a.Value.Length == 0 || a.Value.Contains("@") || a.Value.Contains("{")) continue;
            string kind = floats.Contains(a.Name.LocalName) ? "float" : offsets.Contains(a.Name.LocalName) ? "rectoffset" : colors.Contains(a.Name.LocalName) ? "color" : null;
            if (kind != null && a.Parent.Name.LocalName != "Template") Check(Values.Problem(kind, a.Value) == null, "the game uses " + a.Name + "=" + a.Value);
        }

        History h = new History();
        int value = 0;
        Step step;
        void Put(int v) { int old = value; value = v; h.Add("user", "" + v, () => { value = old; return null; }, () => { value = v; return null; }); }
        Put(1); Put(2); Put(3);
        h.Undo(out step); h.Undo(out step);
        Check(value == 1, "undo");
        h.Redo(out step);
        Check(value == 2, "redo");
        Put(9);
        Check(h.Undone.Count == 0 && h.Redo(out step) != null, "a new change clears redo");
        h.Undo(out step); h.Undo(out step); h.Undo(out step);
        Check(value == 0 && h.Undo(out step) != null && step == null, "all undone");
        Put(1);
        h.Add("claude", "bad", () => throw new Exception("gone"), () => null);
        Check(h.Undo(out step) == "gone" && h.Undo(out step) == null && value == 0, "a step that can't be undone does not block the others");

        Console.WriteLine((failed == 0 ? "ALL CHECKS PASSED" : failed + " FAILED") + ": " + layouts.Count + " layouts, " + marks + " elements");
        return failed == 0 ? 0 : 1;
    }
}
