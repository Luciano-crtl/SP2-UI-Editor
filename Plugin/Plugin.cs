using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SP2UIEditor
{
    [BepInPlugin("com.luciano.sp2uieditor", "SP2 UI Editor", Version)]
    public sealed class EditorPlugin : BaseUnityPlugin
    {
        public const string Version = "0.1.0";
        internal static ManualLogSource Log;
        internal static EditorPlugin Instance;

        private Harmony harmony;
        private Server server;
        private bool quitting;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ConfigEntry<bool> enabled = Config.Bind("Editor", "Enabled", true, "Run the editor's server (only reachable from this computer).");

            // Its own id for each loaded copy: on a reload (F6) the new copy is made before the old one is removed, and the old one must only
            // take its own patches away.
            harmony = new Harmony("com.luciano.sp2uieditor." + Guid.NewGuid().ToString("N"));
            Patches.Apply(harmony);
            try { Packages.Load(); }
            catch (Exception e) { Log.LogError("[UIEDIT] packages: " + e); }
            if (enabled.Value)
            {
                try
                {
                    server = new Server();
                    server.Start();
                }
                catch (Exception e)
                {
                    server = null;
                    Log.LogError("[UIEDIT] the server could not start: " + e);
                }
            }
            Application.quitting += OnQuitting;
            Log.LogInfo("SP2 UI Editor " + Version + " loaded.");
        }

        private void OnQuitting() { quitting = true; }

        private void Update()
        {
            Packages.Tick();
            if (server == null) return;
            server.Pump();
            Picker.Tick();
            Overlay.Tick(server.Connected);
        }

        private void OnDestroy()
        {
            Application.quitting -= OnQuitting;
            Try("server", () => { if (server != null) server.Stop(); });
            if (quitting) return;
            Try("patches", () => harmony.UnpatchSelf());
            Try("overlay", Overlay.Destroy);
            Try("changes", LiveEditor.UndoAll);
            Try("packages", Packages.UnapplyAll);
        }

        private static void Try(string name, Action action)
        {
            try { action(); }
            catch (Exception e) { Log.LogWarning("[UIEDIT] unloading, '" + name + "' failed: " + e.Message); }
        }
    }
}
