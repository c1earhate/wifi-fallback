using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using System.Web.Script.Serialization;

namespace WifiFallback
{
    // ------------------------------------------------------------------
    // config.json 的对象模型（属性名与 JSON 字段一一对应）
    // ------------------------------------------------------------------

    public class ConfigRoot
    {
        public List<string> _说明 { get; set; }
        public string @interface { get; set; }
        public List<NetworkConfig> networks { get; set; }
        public ScheduleConfig schedule { get; set; }
        public BehaviorConfig behavior { get; set; }

        public ConfigRoot()
        {
            _说明 = new List<string>();
            _说明.Add("networks 按 priority 从小到大连接，数字越小优先级越高。");
            _说明.Add("profile 一般与 ssid 相同；只有配置文件名称和 SSID 不一致时才需要单独写。");
            _说明.Add("enabled=false 的条目只是示例，不会参与连接。");
            _说明.Add("schedule.windows 的 days 表示窗口“开始”的那一天；start 晚于 end 表示跨到第二天。");
            _说明.Add("修改本文件后不需要重装计划任务（任务间隔除外）。");
            @interface = "";
            networks = new List<NetworkConfig>();
            schedule = new ScheduleConfig();
            behavior = new BehaviorConfig();
        }
    }

    public class NetworkConfig
    {
        public string ssid { get; set; }
        public string profile { get; set; }
        public int priority { get; set; }
        public bool enabled { get; set; }
        public int minSignal { get; set; }
        public bool allowHidden { get; set; }

        public NetworkConfig()
        {
            ssid = "";
            profile = "";
            priority = 0;
            enabled = true;
            minSignal = 0;
            allowHidden = false;
        }
    }

    public class ScheduleConfig
    {
        public bool enabled { get; set; }
        public List<WindowConfig> windows { get; set; }

        public ScheduleConfig()
        {
            enabled = true;
            windows = new List<WindowConfig>();
        }
    }

    public class WindowConfig
    {
        public List<object> days { get; set; }
        public string start { get; set; }
        public string end { get; set; }

        public WindowConfig()
        {
            days = new List<object>();
            start = "23:00";
            end = "07:00";
        }
    }

    public class BehaviorConfig
    {
        public bool preempt { get; set; }
        public bool preemptUnknown { get; set; }
        public int preemptDelayChecks { get; set; }
        public bool respectManualMode { get; set; }
        public int manualGraceMinutes { get; set; }
        public int connectTimeoutSec { get; set; }
        public int postConnectDelaySec { get; set; }
        public int failedRetryMinutes { get; set; }
        public int maxAttemptsPerRun { get; set; }
        public bool verifyInternet { get; set; }
        public string verifyUrl { get; set; }
        public string verifyExpect { get; set; }
        public string verifyHost { get; set; }
        public int verifyPort { get; set; }
        public string onConnectCommand { get; set; }
        public int logKeepDays { get; set; }
        public int taskIntervalMinutes { get; set; }

        public BehaviorConfig()
        {
            preempt = true;
            preemptUnknown = true;
            preemptDelayChecks = 2;
            respectManualMode = true;
            manualGraceMinutes = 30;
            connectTimeoutSec = 15;
            postConnectDelaySec = 4;
            failedRetryMinutes = 3;
            maxAttemptsPerRun = 3;
            verifyInternet = false;
            verifyUrl = "http://www.msftconnecttest.com/connecttest.txt";
            verifyExpect = "Microsoft Connect Test";
            verifyHost = "223.5.5.5";
            verifyPort = 443;
            onConnectCommand = "";
            logKeepDays = 30;
            taskIntervalMinutes = 1;
        }
    }

    // ------------------------------------------------------------------
    // 引擎快照（WifiFallback.ps1 -SnapshotPath 生成）
    // ------------------------------------------------------------------

    public class Snapshot
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public string generatedAt { get; set; }
        public string configPath { get; set; }
        public string pausedUntil { get; set; }
        public string manualUntil { get; set; }
        public string lastResult { get; set; }
        public string lastRun { get; set; }
        public LastAppliedInfo lastApplied { get; set; }
        public TaskInfo task { get; set; }
        public AdapterInfo adapter { get; set; }
        public ScheduleInfo schedule { get; set; }
        public PlanInfo plan { get; set; }
        public ConnectivityInfo connectivity { get; set; }
        public List<NetworkStatus> networks { get; set; }
        public List<VisibleInfo> visible { get; set; }
        public List<string> profiles { get; set; }

        public Snapshot()
        {
            ok = false;
            error = "";
            generatedAt = "";
            configPath = "";
            pausedUntil = "";
            manualUntil = "";
            lastResult = "";
            lastRun = "";
            lastApplied = null;
            task = new TaskInfo();
            adapter = new AdapterInfo();
            schedule = new ScheduleInfo();
            plan = new PlanInfo();
            connectivity = new ConnectivityInfo();
            networks = new List<NetworkStatus>();
            visible = new List<VisibleInfo>();
            profiles = new List<string>();
        }
    }

    public class LastAppliedInfo
    {
        public string ssid { get; set; }
        public string profile { get; set; }
        public string time { get; set; }
    }

    public class TaskInfo
    {
        public bool installed { get; set; }
        public string state { get; set; }
        public string name { get; set; }
    }

    public class AdapterInfo
    {
        public bool found { get; set; }
        public string name { get; set; }
        public bool connected { get; set; }
        public string ssid { get; set; }
        public string profile { get; set; }
        public int signal { get; set; }
        public string mode { get; set; }
        public string state { get; set; }
    }

    public class ScheduleInfo
    {
        public bool enabled { get; set; }
        public bool active { get; set; }
        public string text { get; set; }
        public string currentEnd { get; set; }
        public string nextStart { get; set; }
    }

    public class PlanInfo
    {
        public string action { get; set; }
        public string reason { get; set; }
        public string message { get; set; }
        public string target { get; set; }
        public List<string> candidates { get; set; }

        public PlanInfo()
        {
            action = "none";
            reason = "";
            message = "";
            target = "";
            candidates = new List<string>();
        }
    }

    public class ConnectivityInfo
    {
        public bool @checked { get; set; }
        public bool internet { get; set; }
        public bool captivePortal { get; set; }
        public string portalUrl { get; set; }
        public string detail { get; set; }

        public ConnectivityInfo()
        {
            @checked = false;
            internet = false;
            captivePortal = false;
            portalUrl = "";
            detail = "";
        }
    }
    public class NetworkStatus
    {
        public string ssid { get; set; }
        public string profile { get; set; }
        public int priority { get; set; }
        public bool enabled { get; set; }
        public int minSignal { get; set; }
        public bool allowHidden { get; set; }
        public bool current { get; set; }
        public bool visible { get; set; }
        public int signal { get; set; }
        public bool failure { get; set; }
    }

    public class VisibleInfo
    {
        public string ssid { get; set; }
        public int signal { get; set; }
    }

    // ------------------------------------------------------------------
    // 配置读写
    // ------------------------------------------------------------------

    public static class DayUtil
    {
        public static int ToNumber(object value)
        {
            if (value == null)
            {
                return 0;
            }
            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                return (int)(long)value;
            }
            if (value is double)
            {
                return (int)(double)value;
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (text == null)
            {
                return 0;
            }
            text = text.Trim().ToLowerInvariant().TrimEnd('.');
            int number;
            if (int.TryParse(text, out number) && number >= 1 && number <= 7)
            {
                return number;
            }
            switch (text)
            {
                case "mon":
                case "monday":
                case "周一":
                case "星期一":
                case "一":
                    return 1;
                case "tue":
                case "tuesday":
                case "周二":
                case "星期二":
                case "二":
                    return 2;
                case "wed":
                case "wednesday":
                case "周三":
                case "星期三":
                case "三":
                    return 3;
                case "thu":
                case "thursday":
                case "周四":
                case "星期四":
                case "四":
                    return 4;
                case "fri":
                case "friday":
                case "周五":
                case "星期五":
                case "五":
                    return 5;
                case "sat":
                case "saturday":
                case "周六":
                case "星期六":
                case "六":
                    return 6;
                case "sun":
                case "sunday":
                case "周日":
                case "周天":
                case "星期日":
                case "星期天":
                case "日":
                case "七":
                    return 7;
                default:
                    return 0;
            }
        }
    }
    public static class ConfigStore
    {
        public static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 32 * 1024 * 1024;
            return serializer;
        }

        public static ConfigRoot CreateDefault()
        {
            ConfigRoot config = new ConfigRoot();
            config.schedule = new ScheduleConfig();
            config.schedule.enabled = true;
            WindowConfig window = new WindowConfig();
            window.days = new List<object>(new object[] { 1, 2, 3, 4, 5, 6, 7 });
            window.start = "23:00";
            window.end = "07:00";
            config.schedule.windows.Add(window);
            return config;
        }

        public static ConfigRoot Load(string path)
        {
            if (!File.Exists(path))
            {
                return CreateDefault();
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            ConfigRoot config = CreateSerializer().Deserialize<ConfigRoot>(text);
            if (config == null)
            {
                throw new InvalidOperationException("配置文件内容无法解析。");
            }
            Normalize(config);
            return config;
        }

        public static void Save(string path, ConfigRoot config)
        {
            Normalize(config);
            string json = CreateSerializer().Serialize(config);
            json = json.Replace("\\u003e", ">").Replace("\\u003c", "<").Replace("\\u0026", "&");
            json = JsonPretty.Format(json);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(path))
            {
                try
                {
                    File.Copy(path, path + ".bak", true);
                }
                catch
                {
                }
            }
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        public static void Normalize(ConfigRoot config)
        {
            if (config == null)
            {
                return;
            }
            if (config.networks == null)
            {
                config.networks = new List<NetworkConfig>();
            }

            List<NetworkConfig> valid = new List<NetworkConfig>();
            foreach (NetworkConfig network in config.networks)
            {
                if (network == null)
                {
                    continue;
                }
                if (network.ssid == null)
                {
                    network.ssid = "";
                }
                network.ssid = network.ssid.Trim();
                if (network.ssid.Length == 0)
                {
                    continue;
                }
                if (network.profile == null)
                {
                    network.profile = "";
                }
                network.profile = network.profile.Trim();
                if (network.profile.Length == 0)
                {
                    network.profile = network.ssid;
                }
                if (network.minSignal < 0)
                {
                    network.minSignal = 0;
                }
                if (network.minSignal > 100)
                {
                    network.minSignal = 100;
                }
                valid.Add(network);
            }

            valid.Sort(delegate(NetworkConfig left, NetworkConfig right)
            {
                return left.priority.CompareTo(right.priority);
            });
            for (int index = 0; index < valid.Count; index++)
            {
                valid[index].priority = index + 1;
            }
            config.networks = valid;

            if (config.schedule == null)
            {
                config.schedule = new ScheduleConfig();
            }
            if (config.schedule.windows == null)
            {
                config.schedule.windows = new List<WindowConfig>();
            }
            foreach (WindowConfig window in config.schedule.windows)
            {
                if (window == null)
                {
                    continue;
                }
                List<int> normalizedDays = new List<int>();
                if (window.days != null)
                {
                    foreach (object day in window.days)
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
                window.days = new List<object>();
                foreach (int day in normalizedDays)
                {
                    window.days.Add(day);
                }
                if (string.IsNullOrEmpty(window.start))
                {
                    window.start = "23:00";
                }
                if (string.IsNullOrEmpty(window.end))
                {
                    window.end = "07:00";
                }
            }

            if (config.behavior == null)
            {
                config.behavior = new BehaviorConfig();
            }
            BehaviorConfig behavior = config.behavior;
            if (behavior.preemptDelayChecks < 1)
            {
                behavior.preemptDelayChecks = 1;
            }
            if (behavior.manualGraceMinutes < 0)
            {
                behavior.manualGraceMinutes = 0;
            }
            if (behavior.connectTimeoutSec < 5)
            {
                behavior.connectTimeoutSec = 5;
            }
            if (behavior.postConnectDelaySec < 0)
            {
                behavior.postConnectDelaySec = 0;
            }
            if (behavior.failedRetryMinutes < 1)
            {
                behavior.failedRetryMinutes = 1;
            }
            if (behavior.maxAttemptsPerRun < 1)
            {
                behavior.maxAttemptsPerRun = 1;
            }
            if (behavior.logKeepDays < 1)
            {
                behavior.logKeepDays = 1;
            }
            if (behavior.taskIntervalMinutes < 1)
            {
                behavior.taskIntervalMinutes = 1;
            }
            if (behavior.taskIntervalMinutes > 15)
            {
                behavior.taskIntervalMinutes = 15;
            }
            if (string.IsNullOrWhiteSpace(behavior.verifyUrl))
            {
                behavior.verifyUrl = "http://www.msftconnecttest.com/connecttest.txt";
            }
            if (behavior.verifyExpect == null)
            {
                behavior.verifyExpect = "Microsoft Connect Test";
            }
            if (behavior.verifyHost == null)
            {
                behavior.verifyHost = "223.5.5.5";
            }
            if (behavior.onConnectCommand == null)
            {
                behavior.onConnectCommand = "";
            }
        }
    }

    public static class JsonPretty
    {
        public static string Format(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }
            StringBuilder builder = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = 0; index < json.Length; index++)
            {
                char character = json[index];
                if (inString)
                {
                    builder.Append(character);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                    continue;
                }
                switch (character)
                {
                    case '"':
                        inString = true;
                        builder.Append(character);
                        break;
                    case '{':
                    case '[':
                        {
                            int next = index + 1;
                            while (next < json.Length && char.IsWhiteSpace(json[next]))
                            {
                                next++;
                            }
                            char closing = character == '{' ? '}' : ']';
                            if (next < json.Length && json[next] == closing)
                            {
                                builder.Append(character);
                                builder.Append(closing);
                                index = next;
                            }
                            else
                            {
                                builder.Append(character);
                                builder.AppendLine();
                                indent++;
                                AppendIndent(builder, indent);
                            }
                            break;
                        }
                    case '}':
                    case ']':
                        builder.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        AppendIndent(builder, indent);
                        builder.Append(character);
                        break;
                    case ',':
                        builder.Append(character);
                        builder.AppendLine();
                        AppendIndent(builder, indent);
                        break;
                    case ':':
                        builder.Append(": ");
                        break;
                    case ' ':
                    case '\r':
                    case '\n':
                    case '\t':
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }
    }
    public static class SnapshotStore
    {
        public static Snapshot Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            Snapshot snapshot = ConfigStore.CreateSerializer().Deserialize<Snapshot>(text);
            return snapshot;
        }
    }
}
