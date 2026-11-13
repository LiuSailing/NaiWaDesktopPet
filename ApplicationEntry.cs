using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NaiwaDesktopPet
{
    internal static class ApplicationEntry
    {
        [STAThread]
        static void Main(string[] args)
        {
            var startup = Stopwatch.StartNew();
            bool created;
            using (var instance = new Mutex(true, "Local\\NaiwaDesktopPet-v2", out created))
            {
                if (!created) return;
                try
                {
                    DisplayQuality.Enable();
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    Application.EnableVisualStyles();
                    string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "naiwa");
                    using (var pet = new Pet(directory))
                    {
                        string report = null; bool check = false;
                        for (int i = 0; i < args.Length; i++)
                        {
                            if (args[i] == "--audio-settings") pet.OpenAudioSettingsOnStart = true;
                            if (args[i] == "--startup-check" && i + 1 < args.Length) { check = true; report = args[++i]; }
                        }
                        System.Windows.Forms.Timer finish = null;
                        if (check)
                        {
                            pet.Enabled = false;
                            finish = new System.Windows.Forms.Timer { Interval = 350 };
                            finish.Tick += (s, e) => { finish.Stop(); pet.Close(); };
                            pet.Shown += (s, e) => {
                                File.WriteAllText(report, "{\"first_frame_ms\":" + startup.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)
                                    + ",\"rendered_frames\":" + pet.RenderCount + "}");
                                finish.Start();
                            };
                        }
                        try { Application.Run(pet); }
                        finally { if (finish != null) finish.Dispose(); }
                    }
                }
                catch (Exception error)
                {
                    string log = Path.Combine(Path.GetTempPath(), "NaiwaDesktopPet-startup-error.txt");
                    try { File.WriteAllText(log, error.ToString()); } catch (IOException) { }
                    MessageBox.Show("奶蛙桌宠启动失败：" + error.Message + Environment.NewLine + "详细信息：" + log,
                        "奶蛙桌宠", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}
