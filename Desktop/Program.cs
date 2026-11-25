using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SP2UIEditor.Desktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    // Follows the game: the plugin writes its address and key to a file in the user's folder each time it starts (also after F6).
    internal sealed class MainForm : Form
    {
        private readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        private readonly Label wait = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 13) };
        private readonly Timer timer = new Timer { Interval = 1000 };
        private string shownKey;

        public MainForm()
        {
            Text = "SP2 UI Editor";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1500, 900);
            MinimumSize = new Size(1000, 600);
            BackColor = Color.FromArgb(0x17, 0x19, 0x1d);
            wait.ForeColor = Color.Gainsboro;
            wait.Text = "Abra o SimplePlanes 2 com o plugin SP2 UI Editor.\nEsperando o jogo... Se ele perguntar se pode conectar, clique em Okay.\n\nOpen SimplePlanes 2 with the SP2 UI Editor plugin.\nWaiting for the game... If it asks whether to connect, click Okay.";
            Controls.Add(view);
            Controls.Add(wait);
            Load += async (s, e) => await Start();
        }

        private async Task Start()
        {
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SP2UIEditor", "WebView2");
                await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, data));
                view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                // only the editor's own page is shown here
                view.CoreWebView2.NavigationStarting += (s, e) => { if (!e.Uri.StartsWith("http://127.0.0.1:")) e.Cancel = true; };
                view.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;
                timer.Tick += (s, e) => Look();
                timer.Start();
                Look();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (MessageBox.Show("This program needs WebView2, part of Microsoft Edge. Open the download page?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
                Close();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, Text);
                Close();
            }
        }

        // Tells the plugin that the editor is open: with its server off, the game then asks the player.
        private static void Touch()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SP2UIEditor");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "editor-open"), "");
            }
            catch { }
        }

        private void Look()
        {
            Touch();
            string key = null;
            int port = 0;
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SP2UIEditor", "session.json");
                if (File.Exists(file))
                {
                    JsonNode s = JsonNode.Parse(File.ReadAllText(file));
                    using Process game = Process.GetProcessById((int)s["pid"]);      // throws when the game is closed
                    key = (string)s["key"];
                    port = (int)s["port"];
                }
            }
            catch { key = null; }
            if (key == shownKey) return;
            shownKey = key;
            view.Visible = key != null;
            wait.Visible = key == null;
            // The key goes after the #, so it is never sent in an address. The number makes it a new address: a change after the # alone would not reload.
            if (key != null) view.CoreWebView2.Navigate("http://127.0.0.1:" + port + "/?" + Environment.TickCount64 + "#" + key);
        }
    }
}
