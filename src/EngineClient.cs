using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WifiFallback
{
    public class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }

        public ProcessResult()
        {
            ExitCode = -1;
            Output = "";
            Error = "";
        }
    }

    public class EngineClient
    {
        public string RootPath { get; private set; }
        public string ScriptPath { get; private set; }
        public string ConfigPath { get; private set; }

        public EngineClient(string rootPath, string configPath)
        {
            RootPath = rootPath;
            ConfigPath = configPath;
            ScriptPath = Path.Combine(rootPath, "WifiFallback.ps1");
        }

        public bool ScriptExists
        {
            get { return File.Exists(ScriptPath); }
        }

        public string StatePath
        {
            get { return Path.Combine(RootPath, "state.json"); }
        }

        public string LogDirectory
        {
            get { return Path.Combine(RootPath, "logs"); }
        }

        public string LatestLogPath
        {
            get
            {
                try
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        return "";
                    }
                    string[] files = Directory.GetFiles(LogDirectory, "wifi-fallback-*.log");
                    if (files.Length == 0)
                    {
                        return "";
                    }
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    return files[files.Length - 1];
                }
                catch
                {
                    return "";
                }
            }
        }

        public Snapshot GetSnapshot(out string error)
        {
            error = "";
            if (!ScriptExists)
            {
                error = "找不到 WifiFallback.ps1。请把本程序和它放在同一个目录。";
                return null;
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "WifiFallbackUI");
            Directory.CreateDirectory(tempDirectory);
            string snapshotPath = Path.Combine(tempDirectory, "snapshot-" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                string arguments = "-ConfigPath " + Quote(ConfigPath) + " -SnapshotPath " + Quote(snapshotPath);
                ProcessResult result = RunPowerShell(arguments, 30000);
                if (!File.Exists(snapshotPath))
                {
                    error = "无法读取 WiFi 状态（退出码 " + result.ExitCode + "）。";
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        error += " " + result.Error.Trim();
                    }
                    else if (!string.IsNullOrEmpty(result.Output))
                    {
                        error += " " + result.Output.Trim();
                    }
                    return null;
                }

                Snapshot snapshot = SnapshotStore.Load(snapshotPath);
                if (snapshot == null)
                {
                    error = "状态数据格式无法识别。";
                    return null;
                }
                if (!snapshot.ok)
                {
                    error = string.IsNullOrEmpty(snapshot.error) ? "引擎返回了错误。" : snapshot.error;
                }
                return snapshot;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
            finally
            {
                try
                {
                    File.Delete(snapshotPath);
                }
                catch
                {
                }
            }
        }

        public ProcessResult RunAction(string arguments, int timeoutMs)
        {
            return RunPowerShell(arguments, timeoutMs);
        }

        public ProcessResult RunCheckNow()
        {
            return RunPowerShell("-ConfigPath " + Quote(ConfigPath) + " -Run -Force", 180000);
        }

        public ProcessResult Pause(int minutes)
        {
            return RunPowerShell("-ConfigPath " + Quote(ConfigPath) + " -Pause -Minutes " + minutes, 30000);
        }

        public ProcessResult Resume()
        {
            return RunPowerShell("-ConfigPath " + Quote(ConfigPath) + " -Resume", 30000);
        }

        public ProcessResult InstallTask()
        {
            return RunPowerShell("-ConfigPath " + Quote(ConfigPath) + " -InstallTask", 90000);
        }

        public ProcessResult UninstallTask()
        {
            return RunPowerShell("-ConfigPath " + Quote(ConfigPath) + " -UninstallTask", 30000);
        }

        public bool IsTaskInstalled()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "schtasks.exe";
                startInfo.Arguments = "/Query /TN \"WifiFallback\"";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }
                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public string GetLogTail(int maxLines)
        {
            string path = LatestLogPath;
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - maxLines);
                StringBuilder builder = new StringBuilder();
                for (int index = start; index < lines.Length; index++)
                {
                    builder.AppendLine(lines[index]);
                }
                return builder.ToString();
            }
            catch (Exception exception)
            {
                return "读取日志失败：" + exception.Message;
            }
        }

        public static void OpenFolder(string path)
        {
            try
            {
                Process.Start("explorer.exe", Quote(path));
            }
            catch
            {
            }
        }

        public static void OpenFile(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select," + Quote(path));
            }
            catch
            {
            }
        }

        private ProcessResult RunPowerShell(string arguments, int timeoutMs)
        {
            ProcessResult result = new ProcessResult();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
                                  Quote(ScriptPath) + " " + arguments;
            startInfo.WorkingDirectory = RootPath;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        result.ExitCode = -1;
                        result.Error = "操作超时。";
                        return result;
                    }

                    // 等待异步读取结束，避免残留重定向管道
                    try { Task.WaitAll(new Task[] { outputTask, errorTask }, 3000); } catch { }
                    result.ExitCode = process.ExitCode;
                    try { result.Output = outputTask.Result; } catch { }
                    try { result.Error = errorTask.Result; } catch { }
                    return result;
                }
            }
            catch (Exception exception)
            {
                result.ExitCode = -1;
                result.Error = exception.Message;
                return result;
            }
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            return "\"" + value + "\"";
        }
    }
}
