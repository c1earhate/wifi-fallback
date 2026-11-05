using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WifiFallback
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length >= 2 && args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run(args[1]);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\WifiFallbackUI", out created))
            {
                if (!created)
                {
                    try
                    {
                        using (EventWaitHandle showEvent = EventWaitHandle.OpenExisting("Local\\WifiFallbackUIShow"))
                        {
                            showEvent.Set();
                            return;
                        }
                    }
                    catch
                    {
                    }
                    MessageBox.Show("WiFi 优先级兜底已经在运行。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm());
            }
        }
    }

    internal static class SelfTest
    {
        public static int Run(string outputPath)
        {
            StringBuilder text = new StringBuilder();
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string config = Path.Combine(root, "config.json");
                EngineClient engine = new EngineClient(root, config);

                try { ConfigRoot cfg = ConfigStore.Load(config); text.AppendLine("configLoad=ok networks=" + cfg.networks.Count); } catch (Exception cfgEx) { text.AppendLine("configLoadError=" + cfgEx.ToString()); }
                text.AppendLine("root=" + root);
                text.AppendLine("script=" + engine.ScriptExists);
                text.AppendLine("config=" + config);

                string error;
                Snapshot snapshot = engine.GetSnapshot(out error);
                text.AppendLine("error=" + error);
                if (snapshot == null)
                {
                    text.AppendLine("snapshot=null");
                    File.WriteAllText(outputPath, text.ToString(), new UTF8Encoding(false));
                    return 2;
                }

                text.AppendLine("ok=" + snapshot.ok);
                text.AppendLine("adapter=" + (snapshot.adapter == null ? "" : snapshot.adapter.ssid));
                text.AppendLine("connected=" + (snapshot.adapter == null ? "" : snapshot.adapter.connected.ToString()));
                text.AppendLine("networks=" + (snapshot.networks == null ? 0 : snapshot.networks.Count));
                text.AppendLine("visible=" + (snapshot.visible == null ? 0 : snapshot.visible.Count));
                text.AppendLine("profiles=" + (snapshot.profiles == null ? 0 : snapshot.profiles.Count));
                text.AppendLine("plan=" + (snapshot.plan == null ? "" : snapshot.plan.reason));
                text.AppendLine("task=" + (snapshot.task == null ? "" : snapshot.task.installed.ToString()));
                text.AppendLine("configNetworks=" + snapshot.configPath);


                File.WriteAllText(outputPath, text.ToString(), new UTF8Encoding(false));
                return snapshot.ok ? 0 : 2;
            }
            catch (Exception exception)
            {
                text.AppendLine("exception=" + exception.ToString());
                try
                {
                    File.WriteAllText(outputPath, text.ToString(), new UTF8Encoding(false));
                }
                catch
                {
                }
                return 2;
            }
        }
    }
}
