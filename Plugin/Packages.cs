using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Jundroo.Juicy.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SP2UIEditor
{
    // A name for an element that is the same the next time its screen is built.
    internal static class Identity
    {
        public static string Of(Widget w)
        {
            if (w == null) return null;
            string uid = LiveEditor.UidOf(w);
            if (uid != null) return "add:" + uid;
            string src = LayoutSrc(w);
            if (src != null) return "src:" + src;
            string step = Step(w);
            if (w.Parent == null) return "root:" + w.gameObject.name;
            return Of(w.Parent) + "|" + step;
        }

        // The last part of it, without climbing: enough to see that a widget matters to no edit.
        public static string Leaf(Widget w)
        {
            string uid = LiveEditor.UidOf(w);
            if (uid != null) return "add:" + uid;
            string src = LayoutSrc(w);
            if (src != null) return "src:" + src;
            return w.Parent == null ? "root:" + w.gameObject.name : Step(w);
        }

        public static string LeafOf(string identity)
        {
            int bar = identity.LastIndexOf('|');
            return bar < 0 ? identity : identity.Substring(bar + 1);
        }

        // Written out in a layout (not inside a template, which is shared).
        private static string LayoutSrc(Widget w)
        {
            string src = Tree.Src(w);
            if (src == null) return null;
            XElement raw = Patches.RawOf(Sources.FileOf(src));
            XElement e = raw != null ? Sources.Find(raw, src) : null;
            return e != null && !Sources.InTemplate(e) ? src : null;
        }

        private static string Clean(string s) { return s == null ? "" : s.Replace('|', ' ').Replace(':', ' '); }

        private static string Step(Widget w)
        {
            Patches.Instance i = Patches.InstanceOf(w);
            if (i != null)
            {
                string label;
                if (!i.Attributes.TryGetValue("label", out label) && !i.Attributes.TryGetValue("title", out label)) label = w.Id;
                if (!string.IsNullOrEmpty(label)) return "t:" + i.Template + ":" + Clean(label) + Nth(w, x => { Patches.Instance o = Patches.InstanceOf(x); return o != null && o.Template == i.Template && Label(o, x) == label; });
            }
            if (!string.IsNullOrEmpty(w.Id)) return "#" + Clean(w.Id) + Nth(w, x => x.Id == w.Id);
            string type = Tree.TypeName(w);
            return "n:" + type + Nth(w, x => Tree.TypeName(x) == type && string.IsNullOrEmpty(x.Id));
        }

        private static string Label(Patches.Instance i, Widget w)
        {
            string label;
            return i.Attributes.TryGetValue("label", out label) || i.Attributes.TryGetValue("title", out label) ? label : w.Id;
        }

        // Its turn among the like ones beside it (what the editor added does not count).
        private static string Nth(Widget w, Func<Widget, bool> like)
        {
            if (w.Parent == null) return "";
            int n = 0;
            foreach (Widget x in w.Parent.Widgets)
            {
                if (x == w) break;
                if (x != null && LiveEditor.UidOf(x) == null && like(x)) n++;
            }
            return n == 0 ? "" : ":" + n;
        }

        public static Dictionary<string, string> Prints(params string[] identities)
        {
            Dictionary<string, string> prints = null;
            foreach (string id in identities)
            {
                if (id == null || !id.StartsWith("src:")) continue;
                string b = Op.BaseOf(id);
                XElement raw = Patches.RawOf(Sources.FileOf(b.Substring(4)));
                XElement e = raw != null ? Sources.Find(raw, b.Substring(4)) : null;
                if (e == null) continue;
                if (prints == null) prints = new Dictionary<string, string>();
                prints[b] = Op.Print(e);
            }
            return prints;
        }
    }

    // The saved edits: applied to each screen as the game builds it.
    internal static class Packages
    {
        private sealed class Marks { public readonly HashSet<Op> Ops = new HashSet<Op>(); }

        private sealed class Done
        {
            public Op Op;
            public Package Package;
            public Step Step;
        }

        public static readonly List<Package> All = new List<Package>();      // in the order they are applied
        private static readonly HashSet<string> disabled = new HashSet<string>();
        private static readonly List<string> problems = new List<string>();
        public static Package Current;                                       // where new edits are saved
        private static int savedVersion;

        private static readonly List<Widget> created = new List<Widget>();
        private static readonly ConditionalWeakTable<Widget, Marks> marks = new ConditionalWeakTable<Widget, Marks>();
        private static readonly List<Done> applied = new List<Done>();
        private static readonly Dictionary<string, XElement> locatedIn = new Dictionary<string, XElement>();
        private static readonly Dictionary<string, string> located = new Dictionary<string, string>();
        private static bool walk;
        private static int walkAt;      // after a reload (F6) the earlier copy takes its changes back first

        private static string Folder { get { return Path.Combine(BepInEx.Paths.ConfigPath, "SP2UIEditor", "packages"); } }
        private static string StateFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "SP2UIEditor", "state.json"); } }

        public static bool Enabled(Package p) { return !disabled.Contains(p.Name); }
        public static bool Unsaved { get { return LiveEditor.History.Version != savedVersion; } }

        // ---- files ----

        public static void Load()
        {
            Directory.CreateDirectory(Folder);
            JObject state = new JObject();
            try { if (File.Exists(StateFile)) state = JObject.Parse(File.ReadAllText(StateFile)); }
            catch (Exception e) { problems.Add("state.json: " + e.Message); }
            foreach (string file in Directory.GetFiles(Folder, "*.sp2ui").OrderBy(x => x))
            {
                try
                {
                    Package p = Package.FromJson(File.ReadAllText(file));
                    p.Path = file;
                    if (string.IsNullOrWhiteSpace(p.Name)) p.Name = Path.GetFileNameWithoutExtension(file);
                    if (All.Any(x => x.Name == p.Name)) throw new Exception("another package has the name '" + p.Name + "'");
                    p.Base = p.Ops;
                    All.Add(p);
                    foreach (string r in p.Refused) problems.Add(p.Name + ": refused " + r);
                }
                catch (Exception e) { problems.Add(Path.GetFileName(file) + ": " + e.Message); }
            }
            List<string> order = state["order"] is JArray ? state["order"].Select(x => (string)x).ToList() : new List<string>();
            List<Package> sorted = All.OrderBy(p => order.IndexOf(p.Name) < 0 ? int.MaxValue : order.IndexOf(p.Name)).ToList();
            All.Clear();
            All.AddRange(sorted);
            if (state["disabled"] is JArray) foreach (JToken t in state["disabled"]) disabled.Add((string)t);
            Current = All.FirstOrDefault(p => p.Name == (string)state["current"]);
            walk = true;
            walkAt = Time.frameCount + 5;
            foreach (string s in problems) EditorPlugin.Log.LogWarning("[UIEDIT] package " + s);
            EditorPlugin.Log.LogInfo("[UIEDIT] packages: " + (All.Count == 0 ? "none" : string.Join(", ", All.Select(p => p.Name + " (" + p.Ops.Count + " edits" + (Enabled(p) ? "" : ", off") + ")"))));
        }

        private static void SaveState()
        {
            changed++;
            JObject o = new JObject
            {
                ["order"] = new JArray(All.Select(p => p.Name)),
                ["disabled"] = new JArray(disabled.Where(n => All.Any(p => p.Name == n))),
                ["current"] = Current != null ? Current.Name : null,
            };
            File.WriteAllText(StateFile, o.ToString());
        }

        // ---- what the editor's window asks ----

        private static Package Find(string name)
        {
            Package p = All.FirstOrDefault(x => x.Name == name);
            if (p == null) throw new Exception("no package '" + name + "'");
            return p;
        }

        public static string New(string name)
        {
            string safe = Package.SafeFileName(name);
            if (safe == null) return "a name is needed";
            string path = Path.Combine(Folder, safe + ".sp2ui");
            if (File.Exists(path) || All.Any(p => string.Equals(p.Name, safe, StringComparison.OrdinalIgnoreCase))) return "there is already a package '" + safe + "'";
            if (Current != null && Unsaved && LiveEditor.History.Done.Any(s => s.Op != null)) return "save or undo the changes first: they belong to '" + Current.Name + "'";
            if (Current != null) Adopt();
            Package made = new Package { Base = new List<Op>(), Name = safe, GameVersion = Application.version };
            made.Write(path);
            All.Add(made);
            Current = made;
            SaveState();
            return null;
        }

        public static string Use(string name)
        {
            Package p = Find(name);
            if (p == Current) return null;
            if (Current != null && Unsaved && LiveEditor.History.Done.Any(s => s.Op != null)) return "save or undo the changes first";
            // what was done so far stays in the package it was saved in; from here on the list of changes starts empty
            if (Current != null) Adopt();
            Current = p;
            SaveState();
            return null;
        }

        // The steps of this session are in the saved package now: the package's own edits stand for them.
        private static void Adopt()
        {
            foreach (Step s in LiveEditor.History.Done)
                if (s.Op != null) applied.Add(new Done { Op = s.Op, Package = Current, Step = s });
            LiveEditor.History.Clear();
            Current.Base = Current.Ops;
            savedVersion = LiveEditor.History.Version;
        }

        public static string Save()
        {
            if (Current == null) return "choose or make a package first";
            List<Op> session = LiveEditor.History.Done.Where(s => s.Op != null).Select(s => s.Op).ToList();
            // from what it held before this session, so that undone changes disappear
            List<Op> ops = Package.Compact(Current.Base.Concat(session));
            List<Op> before = Current.Ops;
            Current.Ops = ops;
            Current.GameVersion = Application.version;
            try { Current.Write(Current.Path ?? Path.Combine(Folder, Package.SafeFileName(Current.Name) + ".sp2ui")); }
            catch (Exception e)
            {
                Current.Ops = before;
                return "the file could not be written: " + e.Message;
            }
            saved = true;
            savedVersion = LiveEditor.History.Version;
            SaveState();
            return null;
        }

        private static bool saved;

        public static string Enable(string name, bool on)
        {
            Package p = Find(name);
            if (on == Enabled(p)) return null;
            if (!on && p == Current && LiveEditor.History.Done.Any(s => s.Op != null))
            {
                if (Unsaved) return "save or undo the changes first";
                Adopt();
            }
            if (on) disabled.Remove(name);
            else
            {
                disabled.Add(name);
                Unapply(d => d.Package == p);
            }
            Panels.CloseMissing();
            walk = true;
            SaveState();
            return null;
        }

        public static string Drop(string name, int index)
        {
            Package p = Find(name);
            if (index < 0 || index >= p.Ops.Count) return "no such edit";
            Op op = p.Ops[index];
            if (LiveEditor.History.Done.Any(s => s.Op == op)) return "that change is of this session: use undo";
            List<Op> left = op.Do == "add" || op.Do == "panel" ? Package.Compact(p.Ops.Concat(new[] { new Op { Do = "remove", Target = (op.Do == "add" ? "add:" : "panel:") + op.Uid } })) : p.Ops.Where(o => o != op).ToList();
            HashSet<Op> gone = new HashSet<Op>(p.Ops.Except(left));
            List<Op> before = p.Ops;
            p.Ops = left;
            if (p.Base != null) p.Base = p.Base.Where(o => !gone.Contains(o)).ToList();
            try { p.Write(p.Path); }
            catch (Exception e) { p.Ops = before; return e.Message; }
            Unapply(d => gone.Contains(d.Op));
            changed++;
            Panels.CloseMissing();
            return null;
        }

        public static string Order(string name, int index)
        {
            Package p = Find(name);
            All.Remove(p);
            All.Insert(Mathf.Clamp(index, 0, All.Count), p);
            SaveState();
            return null;      // seen the next time a screen is built
        }

        public static JObject Describe()
        {
            return new JObject
            {
                ["folder"] = Folder,
                ["current"] = Current != null ? Current.Name : null,
                ["unsaved"] = Unsaved && LiveEditor.History.Done.Any(s => s.Op != null) || Unsaved && saved,
                ["notSaveable"] = new JArray(LiveEditor.History.Done.Where(s => s.Op == null).Select(s => s.Text)),
                ["conflicts"] = new JArray(Package.Conflicts(All.Where(Enabled).ToList())),
                ["problems"] = new JArray(problems),
                ["packages"] = new JArray(All.Select(p => new JObject
                {
                    ["name"] = p.Name,
                    ["on"] = Enabled(p),
                    ["game"] = p.GameVersion,
                    ["ops"] = new JArray(p.Ops.Select(o => new JObject
                    {
                        ["text"] = o.Text,
                        ["state"] = LiveEditor.History.Done.Any(s => s.Op == o) ? "session" : o.Orphan != null ? "orphan" : o.Applied > 0 || o.Do == "panel" || o.Do == "style" || o.Do == "constant" ? "applied" : "waiting",
                        ["why"] = o.Orphan,
                    })),
                })),
            };
        }

        // Every edit in force: of the packages that are on, with this session's changes.
        public static List<Op> Live()
        {
            List<Op> all = new List<Op>();
            foreach (Package p in All) if (Enabled(p)) all.AddRange(p == Current ? p.Base : p.Ops);
            all.AddRange(LiveEditor.History.Done.Where(s => s.Op != null).Select(s => s.Op));
            return Package.Compact(all);
        }

        // The widgets that have an identity now.
        public static List<Widget> Alive(string identity)
        {
            List<Widget> found = new List<Widget>();
            if (string.IsNullOrEmpty(identity)) return found;
            string leaf = Identity.LeafOf(identity);
            List<Widget> all = new List<Widget>();
            foreach (Widget r in Tree.Roots()) Collect(r, all);
            foreach (Widget w in all) if (Identity.Leaf(w) == leaf && Identity.Of(w) == identity) found.Add(w);
            return found;
        }

        public static JObject Export(string name)
        {
            Package p = Find(name);
            Exporter x = Exporter.Run(p, Panels.Layout);
            string folder = Path.Combine(BepInEx.Paths.ConfigPath, "SP2UIEditor", "export", Package.SafeFileName(p.Name));
            Directory.CreateDirectory(folder);
            foreach (KeyValuePair<string, string> f in x.Files) File.WriteAllText(Path.Combine(folder, f.Key), f.Value);
            return new JObject { ["ok"] = true, ["folder"] = folder, ["files"] = new JArray(x.Files.Keys), ["left"] = new JArray(x.Left) };
        }

        // ---- applying ----

        // From the patch on CreateWidget: only noted; the widget is complete when the frame is over.
        public static void Created(Widget w) { created.Add(w); }

        public static void Mark(Widget w, Op op) { if (op != null) marks.GetOrCreateValue(w).Ops.Add(op); }

        private static int styleVersion = -1, changed;

        public static void Tick()
        {
            if (Time.frameCount >= walkAt && styleVersion != LiveEditor.History.Version * 1000 + changed)
            {
                styleVersion = LiveEditor.History.Version * 1000 + changed;
                try { Styles.Sync(Live()); }
                catch (Exception e) { EditorPlugin.Log.LogError("[UIEDIT] styles: " + e); }
            }
            if (Time.frameCount < walkAt || (!walk && created.Count == 0)) return;
            List<KeyValuePair<Package, Op>> ops = new List<KeyValuePair<Package, Op>>();
            foreach (Package p in All)
            {
                if (!Enabled(p)) continue;
                if (p != Current) foreach (Op op in p.Ops) ops.Add(new KeyValuePair<Package, Op>(p, op));
                else Session(p.Base, p, ops);
            }
            if (Current == null) Session(new List<Op>(), null, ops);
            List<Widget> look;
            if (walk)
            {
                look = new List<Widget>();
                if (ops.Count > 0) foreach (Widget r in Tree.Roots()) Collect(r, look);
            }
            else look = created.Where(w => w != null && w.Stylesheet != null).ToList();
            created.Clear();
            walk = false;
            ops.RemoveAll(o => o.Value.Do == "panel" || o.Value.Do == "style" || o.Value.Do == "constant");
            if (ops.Count == 0 || look.Count == 0) return;
            try { Apply(ops, look); }
            catch (Exception e) { EditorPlugin.Log.LogError("[UIEDIT] applying packages: " + e); }
        }

        // The current package with this session's changes over it.
        private static void Session(List<Op> from, Package p, List<KeyValuePair<Package, Op>> ops)
        {
            HashSet<Op> old = new HashSet<Op>(from);
            foreach (Op op in Package.Compact(from.Concat(LiveEditor.History.Done.Where(s => s.Op != null).Select(s => s.Op))))
                ops.Add(new KeyValuePair<Package, Op>(old.Contains(op) ? p : null, op));
        }

        private static void Collect(Widget w, List<Widget> into)
        {
            if (w == null) return;
            into.Add(w);
            foreach (Widget c in w.Widgets) Collect(c, into);
        }

        // An identity as it is in this version of the game; null = not found (yet, or any more).
        private static string Now(Op op, string identity)
        {
            if (identity == null || !identity.StartsWith("src:")) return identity;
            string b = Op.BaseOf(identity), file = Sources.FileOf(b.Substring(4)), to;
            XElement raw;
            if (!Patches.Raw.TryGetValue(file, out raw) || raw == null) return identity;      // the screen has not been opened
            string print = null;
            if (op.Prints != null) op.Prints.TryGetValue(b, out print);
            string key = b + " " + print;
            XElement when;
            if (!located.TryGetValue(key, out to) || !locatedIn.TryGetValue(key, out when) || when != raw)
            {
                to = Op.Locate(b, print, raw);
                located[key] = to;
                locatedIn[key] = raw;
            }
            if (to == null)
            {
                op.Orphan = "the game's layout " + file + " no longer has this element";
                return null;
            }
            op.Orphan = null;
            return to + identity.Substring(b.Length);
        }

        private static void Apply(List<KeyValuePair<Package, Op>> ops, List<Widget> look)
        {
            HashSet<string> leaves = new HashSet<string>();
            foreach (var o in ops)
            {
                string t = Now(o.Value, o.Value.Target);
                if (t != null) leaves.Add(Identity.LeafOf(t));
            }
            Dictionary<string, List<Widget>> byIdentity = new Dictionary<string, List<Widget>>();
            foreach (Widget w in look) Note(w, leaves, byIdentity);
            if (byIdentity.Count == 0) return;

            // a screen that was built again: the changes of this session go on with the new widgets
            foreach (Step s in LiveEditor.History.Done.Concat(LiveEditor.History.Undone))
            {
                if (s.Op == null || s.Handle == 0 || Tree.Get(s.Handle) != null) continue;
                List<Widget> again;
                if (byIdentity.TryGetValue(s.Op.Target, out again) && again.Count == 1) Tree.Rebind(s.Handle, again[0]);
            }

            foreach (var o in ops)
            {
                Op op = o.Value;
                string target = Now(op, op.Target);
                List<Widget> hits;
                if (target == null || !byIdentity.TryGetValue(target, out hits)) continue;
                foreach (Widget w in hits.ToList())
                {
                    if (w == null || marks.GetOrCreateValue(w).Ops.Contains(op)) continue;
                    Mark(w, op);
                    Widget made;
                    Step step;
                    Actions.Owner = o.Key != null ? o.Key.Name : null;
                    string problem = LiveEditor.Replay(op, w, op.Do == "move" ? FindParent(w, Now(op, op.Parent)) : null, out step, out made);
                    Actions.Owner = null;
                    if (problem != null)
                    {
                        if (op.Orphan == null) EditorPlugin.Log.LogWarning("[UIEDIT] " + op.Text + ": " + problem);
                        op.Orphan = problem;
                        continue;
                    }
                    op.Orphan = null;
                    op.Applied++;
                    if (o.Key != null && step != null) applied.Add(new Done { Op = op, Package = o.Key, Step = step });
                    if (made != null)
                    {
                        List<Widget> inside = new List<Widget>();
                        Collect(made, inside);
                        foreach (Widget x in inside) Note(x, leaves, byIdentity);
                    }
                }
            }
            applied.RemoveAll(d => d.Step.Handle != 0 && Tree.Get(d.Step.Handle) == null);
        }

        private static void Note(Widget w, HashSet<string> leaves, Dictionary<string, List<Widget>> byIdentity)
        {
            if (w == null || !leaves.Contains(Identity.Leaf(w))) return;
            string id = Identity.Of(w);
            List<Widget> list;
            if (!byIdentity.TryGetValue(id, out list)) byIdentity[id] = list = new List<Widget>();
            if (!list.Contains(w)) list.Add(w);
        }

        private static Widget FindParent(Widget of, string identity)
        {
            if (identity == null) return null;
            if (of.Parent != null && Identity.Of(of.Parent) == identity) return of.Parent;
            Widget root = of;
            while (root.Parent != null) root = root.Parent;
            List<Widget> all = new List<Widget>();
            Collect(root, all);
            string leaf = Identity.LeafOf(identity);
            return all.FirstOrDefault(w => Identity.Leaf(w) == leaf && Identity.Of(w) == identity);
        }

        private static void Unapply(Predicate<Done> which)
        {
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                Done d = applied[i];
                if (!which(d)) continue;
                applied.RemoveAt(i);
                try { d.Step.Undo(); }
                catch (Exception e) { EditorPlugin.Log.LogWarning("[UIEDIT] taking back '" + d.Op.Text + "': " + e.Message); }
                Widget w = Tree.Get(d.Step.Handle);
                if (w != null) marks.GetOrCreateValue(w).Ops.Remove(d.Op);
                d.Op.Applied = 0;
            }
        }

        public static void UnapplyAll() { Unapply(d => true); }
    }
}
