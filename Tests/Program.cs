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

        // packages
        Op S(string t, string n, string v) { return new Op { Do = "set", Target = t, Name = n, Value = v, Text = n }; }
        Op A(string t, string uid) { return new Op { Do = "add", Target = t, Uid = uid, Xml = "<Text text='x'/>", Text = uid }; }
        Package pk = new Package { Name = "P", Ops = { S("src:F#1.0", "color", "#ffffff"), A("src:F#1.0", "u1"), new Op { Do = "move", Target = "add:u1", Parent = "src:F#1.1", Index = 2 } } };
        Package back = Package.FromJson(pk.ToJson());
        Check(back.ToJson() == pk.ToJson() && back.Ops.Count == 3 && back.Refused.Count == 0, "a package written and read");
        Package evil = Package.FromJson("{\"Format\":1,\"Ops\":[{\"Do\":\"set\",\"Target\":\"x\",\"Name\":\"onClick\",\"Value\":\"Quit\"},{\"Do\":\"add\",\"Target\":\"x\",\"Uid\":\"a\",\"Xml\":\"<Empty script='X'/>\"},{\"Do\":\"add\",\"Target\":\"x\",\"Uid\":\"a\",\"Xml\":\"<bad\"},{\"Do\":\"run\",\"Target\":\"x\"},null,{\"Do\":\"set\",\"Target\":\"x\",\"Name\":\"text\",\"Value\":\"{a}\"}]}");
        Check(evil.Ops.Count == 0 && evil.Refused.Count == 6, "unsafe edits of a package are refused");
        foreach (string broken in new[] { "", "{", "[]", "{\"Format\":2}" })
        {
            bool threw = false;
            try { Package.FromJson(broken); } catch (Exception) { threw = true; }
            Check(threw, "a broken package was read: " + broken);
        }
        Check(Package.Compact(new[] { S("a", "color", "1"), S("a", "color", "2"), S("b", "color", "3") }).Select(o => o.Value).SequenceEqual(new[] { "2", "3" }), "the last value counts");
        Check(Package.Compact(new[] { A("p", "u1"), S("add:u1|n:Text", "text", "x"), A("add:u1|n:Row", "u2"), S("add:u2", "text", "y"), A("p", "u10"), S("p", "color", "z"), new Op { Do = "remove", Target = "add:u1" } })
              .Select(o => o.Uid ?? o.Name).SequenceEqual(new[] { "u10", "color" }), "a removed element takes its edits along");
        Check(Package.Conflicts(new[] { new Package { Name = "A", Ops = { S("x", "color", "1"), S("x", "width", "1") } }, new Package { Name = "B", Ops = { S("x", "color", "2"), S("x", "width", "1") } } }).Count == 1, "conflicts");
        Check(Package.SafeFileName("..\\x/y:z") == "___x_y_z" && Package.SafeFileName("  ") == null, "file names");
        foreach (XElement l in layouts)
        {
            XElement x = new XElement(l);
            Sources.Stamp("F", x);
            foreach (XElement e in x.Descendants().Where(e => e.Attribute(Sources.Mark) != null && !Sources.InTemplate(e)))
            {
                string b = "src:" + (string)e.Attribute(Sources.Mark);
                Check(Op.Locate(b, Op.Print(Sources.Find(l, b.Substring(4))), l) == b, "located in the same layout");
            }
        }
        XElement v1 = XElement.Parse("<Xml><Widgets><Empty><Button id='a'/><Text text='t'/></Empty></Widgets></Xml>"), v2 = XElement.Parse("<Xml><Widgets><Empty><Image/><Button id='a'/><Text text='t'/></Empty></Widgets></Xml>");
        Check(Op.Locate("src:F#0.0.0", Op.Print(Sources.Find(v1, "F#0.0.0")), v2) == "src:F#0.0.1", "an element with an id is found after the layout changed");
        XElement r1 = XElement.Parse("<Xml><Widgets><Empty><Row><Image id='x'/></Row><Row><Image id='y'/></Row></Empty></Widgets></Xml>"), r2 = XElement.Parse("<Xml><Widgets><Empty><Spacer/><Row><Image id='x'/></Row><Row><Image id='new'/><Image id='y'/></Row></Empty></Widgets></Xml>");
        Check(Op.Locate("src:F#0.0.1", Op.Print(Sources.Find(r1, "F#0.0.1")), r2) == "src:F#0.0.2", "a row without an id is found by what is in it");
        Check(Op.Locate("src:F#0.0.1", "Row;;", r2) == "src:F#0.0.1", "a print of the first version still reads");
        Check(Op.Locate("src:F#0.0.1", Op.Print(Sources.Find(v1, "F#0.0.1")), v2) == null, "one without an id is lost, not confused");

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
