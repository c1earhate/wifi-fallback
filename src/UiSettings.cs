using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WifiFallback
{
    // 桌面版自己的设置，不影响引擎的 config.json
    public class UiSettings
    {
        public List<string> _说明 { get; set; }
        public bool closeToTray { get; set; }
        public bool timedEnabled { get; set; }
        public List<TimedCheckConfig> timedChecks { get; set; }

        public UiSettings()
        {
            _说明 = new List<string>();
            _说明.Add("本文件只保存桌面版的显示/定时设置，WiFi 优先级和时间窗口仍在 config.json。");
            _说明.Add("定时检测只在 WifiFallbackUI.exe 运行时生效；关闭窗口后程序会留在系统托盘继续执行。");
            _说明.Add("如果需要在退出桌面版后仍然定时执行，请安装 Windows 计划任务（引擎的时间窗口）。");
            closeToTray = true;
            timedEnabled = false;
            timedChecks = new List<TimedCheckConfig>();
        }
    }

    public class TimedCheckConfig
    {
        public string time { get; set; }
        public List<object> days { get; set; }
        public string action { get; set; }
        public bool enabled { get; set; }

        public TimedCheckConfig()
        {
            time = "23:00";
            days = new List<object>(new object[] { 1, 2, 3, 4, 5, 6, 7 });
            action = "connect";
            enabled = true;
        }
    }

    public static class UiSettingsStore
    {
        public static UiSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new UiSettings();
            }
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                UiSettings settings = ConfigStore.CreateSerializer().Deserialize<UiSettings>(text);
                if (settings == null)
                {
                    return new UiSettings();
                }
                Normalize(settings);
                return settings;
            }
            catch
            {
                return new UiSettings();
            }
        }

        public static void Save(string path, UiSettings settings)
        {
            Normalize(settings);
            string json = ConfigStore.CreateSerializer().Serialize(settings);
            json = json.Replace("\\u003e", ">").Replace("\\u003c", "<").Replace("\\u0026", "&");
            json = JsonPretty.Format(json);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        public static void Normalize(UiSettings settings)
        {
            if (settings == null)
            {
                return;
            }
            if (settings.timedChecks == null)
            {
                settings.timedChecks = new List<TimedCheckConfig>();
            }

            List<TimedCheckConfig> valid = new List<TimedCheckConfig>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TimedCheckConfig item in settings.timedChecks)
            {
                if (item == null)
                {
                    continue;
                }
                DateTime parsed;
                if (string.IsNullOrEmpty(item.time) ||
                    !DateTime.TryParseExact(item.time.Trim(), new string[] { "H:mm", "HH:mm" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    item.time = "23:00";
                }
                else
                {
                    item.time = parsed.ToString("HH:mm");
                }

                List<int> normalizedDays = new List<int>();
                if (item.days != null)
                {
                    foreach (object day in item.days)
                    {
                        int number = DayUtil.ToNumber(day);
                        if (number >= 1 && number <= 7 && !normalizedDays.Contains(number))
                        {
                            normalizedDays.Add(number);
                        }
                    }
                }
                if (normalizedDays.Count == 0)
                {
                    normalizedDays.AddRange(new int[] { 1, 2, 3, 4, 5, 6, 7 });
                }
                normalizedDays.Sort();
                item.days = new List<object>();
                foreach (int day in normalizedDays)
                {
                    item.days.Add(day);
                }

                if (!string.Equals(item.action, "check", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.action, "connect", StringComparison.OrdinalIgnoreCase))
                {
                    item.action = "connect";
                }
                else
                {
                    item.action = item.action.ToLowerInvariant();
                }

                string key = item.time + "|" + string.Join(",", normalizedDays.ConvertAll(delegate(int value) { return value.ToString(); }).ToArray()) + "|" + item.action;
                if (seen.Add(key))
                {
                    valid.Add(item);
                }
            }

            valid.Sort(delegate(TimedCheckConfig left, TimedCheckConfig right)
            {
                int compare = string.CompareOrdinal(left.time, right.time);
                if (compare != 0)
                {
                    return compare;
                }
                return left.action.CompareTo(right.action);
            });
            settings.timedChecks = valid;
        }
    }
}
