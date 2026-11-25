using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Jundroo.Juicy.Widgets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SP2UIEditor
{
    // The editor's door: a small web server on 127.0.0.1 only, with a new key each time. Requests are answered on the game's main thread.
    internal sealed class Server
    {
        private sealed class Job
        {
            public string Path, Author, Json;
            public JObject Body;
            public byte[] Image;
            public int Status = 200;
            public volatile bool Abandoned;      // nobody waits for it any more: it must not be done late
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
        }

        private HttpListener listener;
        private readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
        private string key;
        private int port;
        private volatile bool stopping;
        private long lastRequest;                // ticks
        private bool wasConnected, runInBackgroundBefore, claudeMayEdit, shooting;
        private string page;

        // In the user's own folder, not the game's: the editor's programs find it wherever the game is installed.
        public static string SessionFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SP2UIEditor", "session.json"); }
        }

        public bool Connected { get { return DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRequest) < TimeSpan.TicksPerSecond * 10; } }

        public void Start()
        {
            byte[] random = new byte[24];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(random);
            key = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
            using (Stream s = typeof(Server).Assembly.GetManifestResourceStream("page.html"))
            using (StreamReader reader = new StreamReader(s, Encoding.UTF8)) page = reader.ReadToEnd();

            // Several ports: after a reload (F6) the old copy still holds its own for a moment.
            Exception last = null;
            for (port = 47811; port <= 47820; port++)
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    listener.Start();
                    break;
                }
                catch (Exception e) { last = e; listener = null; }
            }
            if (listener == null) throw last;

            Directory.CreateDirectory(Path.GetDirectoryName(SessionFile));
            File.WriteAllText(SessionFile, new JObject { ["port"] = port, ["key"] = key, ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id }.ToString());
            new Thread(Listen) { IsBackground = true, Name = "SP2UIEditor server" }.Start();
            EditorPlugin.Log.LogInfo("[UIEDIT] server ready on http://127.0.0.1:" + port + "/ (open it with the SP2 UI Editor program)");
        }

        public void Stop()
        {
            stopping = true;
            if (wasConnected) Application.runInBackground = runInBackgroundBefore;
            try { listener.Abort(); } catch { }
            Job job;
            while (jobs.TryDequeue(out job)) Finish(job, 503, Error("the editor was unloaded"));
            try
            {
                // a newer copy has written its own by now
                if (File.Exists(SessionFile) && (string)JObject.Parse(File.ReadAllText(SessionFile))["key"] == key) File.Delete(SessionFile);
            }
            catch { }
        }

        private void Listen()
        {
            while (!stopping)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch
                {
                    if (stopping) return;
                    Thread.Sleep(50);
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(context));
            }
        }

        private static bool Same(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        private void Handle(HttpListenerContext c)
        {
            try
            {
                HttpListenerRequest q = c.Request;
                string host = "127.0.0.1:" + port, origin = q.Headers["Origin"];
                // Another web page open on this computer must not be able to use the server.
                if (q.Headers["Host"] != host || (origin != null && origin != "http://" + host))
                {
                    Send(c, 403, "text/plain", "refused");
                    return;
                }
                string path = q.Url.AbsolutePath;
                if (path == "/" && q.HttpMethod == "GET")
                {
                    // the page holds nothing secret; it needs the key (after the # of its address) for everything else
                    c.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src blob: data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
                    c.Response.Headers["Referrer-Policy"] = "no-referrer";
                    Send(c, 200, "text/html; charset=utf-8", page);
                    return;
                }
                if (!Same(q.Headers["X-SP2UI-Key"], key)) { SendError(c, 401, "wrong key"); return; }
                if (q.HttpMethod != "POST" || !path.StartsWith("/api/")) { SendError(c, 404, "not found"); return; }
                if (q.ContentLength64 < 0 || q.ContentLength64 > 1000000) { SendError(c, 413, "too long"); return; }
                Interlocked.Exchange(ref lastRequest, DateTime.UtcNow.Ticks);

                string text;
                using (StreamReader r = new StreamReader(q.InputStream, Encoding.UTF8)) text = r.ReadToEnd();
                Job job = new Job
                {
                    Path = path.Substring(5),
                    Body = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text),
                    Author = q.Headers["X-SP2UI-Client"] == "claude" ? "claude" : "user",
                };
                jobs.Enqueue(job);
                if (!job.Done.WaitOne(8000))
                {
                    job.Abandoned = true;
                    SendError(c, 504, "the game did not answer (is it loading, or stopped?)");
                    return;
                }
                if (job.Image != null) SendBytes(c, 200, "image/jpeg", job.Image);
                else Send(c, job.Status, "application/json", job.Json ?? "{}");
            }
            catch (JsonException) { try { SendError(c, 400, "not JSON"); } catch { } }
            catch (Exception e)
            {
                if (!stopping) EditorPlugin.Log.LogWarning("[UIEDIT] request failed: " + e.Message);
                try { c.Response.Abort(); } catch { }
            }
        }

        private static void SendError(HttpListenerContext c, int status, string text) { Send(c, status, "application/json", Error(text).ToString()); }

        private static void Send(HttpListenerContext c, int status, string type, string text) { SendBytes(c, status, type, Encoding.UTF8.GetBytes(text)); }

        private static void SendBytes(HttpListenerContext c, int status, string type, byte[] bytes)
        {
            c.Response.StatusCode = status;
            c.Response.ContentType = type;
            c.Response.Headers["Cache-Control"] = "no-store";
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.ContentLength64 = bytes.Length;
            c.Response.OutputStream.Write(bytes, 0, bytes.Length);
            c.Response.Close();
        }

        // ---- on the main thread ----

        public void Pump()
        {
            bool connected = Connected;
            if (connected != wasConnected)
            {
                // the game must go on while the editor's window has the focus
                if (connected)
                {
                    runInBackgroundBefore = Application.runInBackground;
                    Application.runInBackground = true;
                }
                else
                {
                    Application.runInBackground = runInBackgroundBefore;
                    Picker.Active = false;
                }
                wasConnected = connected;
            }
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            Job job;
            while (watch.ElapsedMilliseconds < 12 && jobs.TryDequeue(out job))
            {
                if (job.Abandoned) continue;
                if (job.Path == "shot") { EditorPlugin.Instance.StartCoroutine(Shot(job)); continue; }
                try
                {
                    JToken answer = Answer(job);
                    JObject o = answer as JObject;
                    Finish(job, o != null && o["error"] != null ? 400 : 200, answer);
                }
                catch (Exception e)
                {
                    EditorPlugin.Log.LogWarning("[UIEDIT] " + job.Path + ": " + e);
                    Finish(job, 500, Error(e.GetBaseException().Message));
                }
            }
        }

        private static void Finish(Job job, int status, JToken answer)
        {
            job.Status = status;
            job.Json = answer.ToString(Formatting.None);
            job.Done.Set();
        }

        private static JObject Error(string text) { return new JObject { ["error"] = text }; }

        private static Widget Wanted(JObject b, string name = "h")
        {
            Widget w = Tree.Get((int?)b[name] ?? 0);
            if (w == null) throw new Exception("no such element (" + name + " = " + b[name] + "); its screen may have been closed");
            return w;
        }

        private static readonly string[] Edits = { "set", "class", "add", "move", "delete", "undo", "redo", "save", "package_new", "package_use", "package_enable", "package_drop", "package_order", "action", "panel_new", "panel_open", "panel_close", "panel_remove" };

        private JToken Answer(Job job)
        {
            JObject b = job.Body;
            if (Edits.Contains(job.Path) && job.Author == "claude" && !claudeMayEdit)
                return Error("the user has not allowed Claude to edit (the 'Claude may edit' switch in the editor's window)");
            bool visible = (bool?)b["visible"] ?? false;
            switch (job.Path)
            {
                case "state": return State();
                case "screens":
                    Tree.Prune();
                    return Tree.Screens(visible);
                case "tree": return Tree.Node(Wanted(b), (int?)b["depth"] ?? 3, visible);
                case "find": return Tree.Find((string)b["text"] ?? "", (bool?)b["visible"] ?? true, Math.Min((int?)b["limit"] ?? 40, 200));
                case "get": return Tree.Details(Wanted(b));
                case "palette": return Palette(Wanted(b));
                case "select":
                    Picker.Select(b["h"] == null ? null : Wanted(b));
                    return State();
                case "pick":
                    if (b["x"] != null)
                    {
                        Widget hit = Tree.At(new Vector2((float)b["x"] * Screen.width, (1f - (float)b["y"]) * Screen.height));
                        if (hit != null) Picker.Select(hit);
                        return State();
                    }
                    Picker.Active = (bool?)b["on"] ?? true;
                    return State();
                case "allow":
                    if (job.Author == "claude") return Error("only the user can change this");
                    claudeMayEdit = (bool?)b["on"] ?? false;
                    return State();
                case "changes": return new JArray(LiveEditor.History.Done.Select(s => new JObject { ["n"] = s.Number, ["by"] = s.Author, ["text"] = s.Text, ["exact"] = s.Exact }));
                case "set": return Result(LiveEditor.Set(Wanted(b), (string)b["name"], (string)b["value"], job.Author));
                case "class": return Result(LiveEditor.Class(Wanted(b), (string)b["name"], (bool?)b["on"] ?? true, job.Author));
                case "add": return Add(b, job.Author);
                case "move":
                {
                    Widget w = Wanted(b);
                    return Result(LiveEditor.Move(w, b["parent"] != null ? Wanted(b, "parent") : w.Parent, (int?)b["index"] ?? -1, job.Author));
                }
                case "delete": return Result(LiveEditor.Delete(Wanted(b), job.Author));
                case "configs": return Actions.Configs();
                case "mod_actions": return new JArray(UiActions.All.Keys.OrderBy(x => x));
                case "panels": return Panels.Describe();
                case "places": return Places();
                case "action": return Act(b, job.Author);
                case "panel_new":
                {
                    string uid;
                    string problem = LiveEditor.NewPanel((string)b["title"], job.Author, out uid);
                    return problem != null ? Error(problem) : PanelResult(uid);
                }
                case "panel_open":
                {
                    string problem = Panels.Open((string)b["uid"]);
                    return problem != null ? Error(problem) : PanelResult((string)b["uid"]);
                }
                case "panel_close": Panels.Close((string)b["uid"]); return Result(null);
                case "panel_remove": return Result(LiveEditor.RemovePanel((string)b["uid"], job.Author));
                case "packages": return Packages.Describe();
                case "save": return Result(Packages.Save());
                case "package_new": return Result(Packages.New((string)b["name"]));
                case "package_use": return Result(Packages.Use((string)b["name"]));
                case "package_enable": return Result(Packages.Enable((string)b["name"], (bool?)b["on"] ?? true));
                case "package_drop": return Result(Packages.Drop((string)b["name"], (int?)b["index"] ?? -1));
                case "package_order": return Result(Packages.Order((string)b["name"], (int?)b["index"] ?? 0));
                case "undo":
                case "redo":
                {
                    Step step;
                    JObject o = Result(job.Path == "undo" ? LiveEditor.History.Undo(out step) : LiveEditor.History.Redo(out step));
                    if (step != null) o["step"] = step.Text;
                    return o;
                }
            }
            return Error("unknown request");
        }

        private static JObject Result(string problem)
        {
            return problem != null ? Error(problem) : new JObject { ["ok"] = true, ["version"] = LiveEditor.History.Version };
        }

        private JObject State()
        {
            Widget s = Picker.Selected;
            return new JObject
            {
                ["plugin"] = EditorPlugin.Version,
                ["game"] = Application.version,
                ["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                ["picking"] = Picker.Active,
                ["selected"] = s != null ? Tree.Handle(s) : 0,
                ["selectionSerial"] = Picker.Serial,
                ["version"] = LiveEditor.History.Version,
                ["done"] = LiveEditor.History.Done.Count,
                ["undone"] = LiveEditor.History.Undone.Count,
                ["claudeMayEdit"] = claudeMayEdit,
                ["package"] = Packages.Current != null ? Packages.Current.Name : null,
                ["unsaved"] = Packages.Unsaved,
                ["screen"] = new JObject { ["w"] = Screen.width, ["h"] = Screen.height },
            };
        }

        private static JObject Add(JObject b, string author)
        {
            Widget parent = Wanted(b, "parent");
            XElement element;
            try { element = XElement.Parse((string)b["xml"] ?? ""); }
            catch (Exception e) { return Error("the XML can't be read: " + e.Message); }
            Dictionary<string, string> texts = b["texts"] is JObject ? b["texts"].ToObject<Dictionary<string, string>>() : null;
            Widget made;
            string problem = LiveEditor.Add(parent, element, (int?)b["index"] ?? -1, texts, author, out made);
            if (problem != null) return Error(problem);
            Picker.Select(made);
            JObject o = Result(null);
            o["h"] = Tree.Handle(made);
            return o;
        }

        private static JObject PanelResult(string uid)
        {
            Widget content = Panels.Content(uid);
            if (content != null) Picker.Select(content);
            JObject o = Result(null);
            o["uid"] = uid;
            o["h"] = content != null ? Tree.Handle(content) : 0;
            return o;
        }

        private static JToken Act(JObject b, string author)
        {
            Widget w = Wanted(b);
            Dictionary<string, string> args = b["args"] is JObject ? b["args"].ToObject<Dictionary<string, string>>() : null;
            // an element is named by its number here, and saved by its identity
            if ((string)b["kind"] == "element" && args != null && args.ContainsKey("h"))
            {
                args["target"] = Identity.Of(Wanted(JObject.FromObject(new { h = int.Parse(args["h"]) })));
                args.Remove("h");
            }
            return Result(LiveEditor.Act(w, (string)b["kind"], args, author));
        }

        // Handy places to add things to, of the screens that exist now.
        private static JArray Places()
        {
            JArray list = new JArray();
            Action<string, Widget> add = (name, w) => { if (w != null) list.Add(new JObject { ["name"] = name, ["h"] = Tree.Handle(w) }); };
            foreach (Op p in Panels.Defs()) add("panel:" + p.Name, Panels.Content(p.Uid));
            foreach (var menu in UnityEngine.Object.FindObjectsByType<Assets.Scripts.Flight.UI.Panels.MenuPanelScript>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                add("flightMenu", menu.Widget);
            foreach (Widget root in Tree.Roots())
            {
                Widget design = root.FindWidget("button-ghost-view"), flight = root.FindWidget("fire-panel"), part = root.FindWidget("items-parent");
                if (design != null) add("designerBar", design.Parent);
                if (flight != null) add("flightScreen", root.FindWidget("main-ui"));
                if (part != null) add("partProperties", part);
            }
            return list;
        }

        private static JObject Palette(Widget w)
        {
            JArray templates = new JArray();
            foreach (KeyValuePair<string, XElement> t in Tree.Templates(w.Stylesheet).OrderBy(x => x.Key))
            {
                JObject o = new JObject();
                foreach (XAttribute a in t.Value.Attributes())
                    if (a.Name.LocalName != "templateId" && SafeXml.AttributeAllowed(a.Name.LocalName)) o[a.Name.LocalName] = a.Value;
                templates.Add(new JObject { ["id"] = t.Key, ["attributes"] = o });
            }
            return new JObject
            {
                ["templates"] = templates,
                ["classes"] = new JArray(Tree.Styles(w.Stylesheet).Keys.Where(x => !x.Contains(" ")).OrderBy(x => x)),
            };
        }

        // ---- the picture ----

        private IEnumerator Shot(Job job)
        {
            while (shooting) yield return null;
            shooting = true;
            bool clean = (bool?)job.Body["clean"] ?? false;
            if (clean)
            {
                Overlay.Hide = true;
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            Texture2D full = null, small = null;
            RenderTexture rt = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                Rect part = new Rect(0, 0, 1, 1);
                if (job.Body["h"] != null)
                {
                    Widget w = Tree.Get((int)job.Body["h"]);
                    Rect r;
                    if (w == null || !Tree.ScreenRect(w, out r)) throw new Exception("no such element on the screen");
                    const float pad = 12f;
                    float x0 = Mathf.Clamp(r.xMin - pad, 0, Screen.width), x1 = Mathf.Clamp(r.xMax + pad, 0, Screen.width);
                    float y0 = Mathf.Clamp(r.yMin - pad, 0, Screen.height), y1 = Mathf.Clamp(r.yMax + pad, 0, Screen.height);
                    if (x1 - x0 < 2 || y1 - y0 < 2) throw new Exception("the element is outside the screen");
                    part = new Rect(x0 / Screen.width, y0 / Screen.height, (x1 - x0) / Screen.width, (y1 - y0) / Screen.height);
                }
                float pw = part.width * full.width, ph = part.height * full.height;
                int width = Mathf.Clamp(Mathf.Min((int?)job.Body["w"] ?? 1280, (int)pw), 16, 2560);
                int height = Mathf.Clamp(Mathf.RoundToInt(width * ph / pw), 1, 4096);
                rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(full, rt, new Vector2(part.width, part.height), new Vector2(part.x, part.y));
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = rt;
                small = new Texture2D(width, height, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                small.Apply();
                RenderTexture.active = active;
                job.Image = small.EncodeToJPG(80);
                job.Done.Set();
            }
            catch (Exception e) { Finish(job, 400, Error(e.Message)); }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (full != null) UnityEngine.Object.Destroy(full);
                if (small != null) UnityEngine.Object.Destroy(small);
                if (clean) Overlay.Hide = false;
                shooting = false;
            }
        }
    }
}
