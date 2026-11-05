using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace WifiFallback
{
    public partial class MainForm : Form
    {
        private readonly string _rootPath;
        private readonly string _configPath;
        private readonly EngineClient _engine;
        private readonly string _uiSettingsPath;
        private UiSettings _uiSettings;
        private ConfigRoot _config;
        private Snapshot _snapshot;
        private bool _dirty;
        private bool _loading;
        private bool _loadingList;
        private bool _busy;
        private bool _taskInstalled;
        private bool _engineErrorShown;
        private bool _allowExit;
        private bool _trayHintShown;
        private WinFormsTimer _refreshTimer;
        private WinFormsTimer _timedTimer;
        private bool _timedRunning;
        private HashSet<string> _timedDone = new HashSet<string>();
        private string _lastTimedText = "";
        private EventWaitHandle _showEvent;
        private Thread _showThread;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayStatusItem;
        private ToolStripMenuItem _trayTimedItem;
        private ToolStripMenuItem _trayCloseItem;

        // 头部 / 导航
        private StatusPill _statusPill;
        private Button _refreshButton;
        private Button[] _navButtons;
        private Panel[] _pages;
        private Label _taskStatusLabel;
        private Label _statusLabel;
        private Label _dirtyLabel;
        private Button _saveButton;
        private Button _reloadButton;

        // 总览
        private Label _currentSsid;
        private Label _currentMeta;
        private Label _currentMode;
        private Label _signalPercent;
        private SignalBars _signalBars;
        private Label _decisionTitle;
        private Label _decisionMessage;
        private Label _decisionSchedule;
        private TextBox _recentLog;
        private Button _checkButton;
        private Button _pauseButton;
        private Button _resumeButton;
        private Button _taskButton;

        // 优先级
        private ListView _networkList;
        private Button _addNetworkButton;
        private Button _editNetworkButton;
        private Button _deleteNetworkButton;
        private Button _upNetworkButton;
        private Button _downNetworkButton;

        // 时间与行为
        private ListView _windowList;
        private CheckBox _scheduleEnabled;
        private Label _scheduleWarning;
        private Button _addWindowButton;
        private Button _editWindowButton;
        private Button _deleteWindowButton;
        private CheckBox _chkPreempt;
        private CheckBox _chkPreemptUnknown;
        private CheckBox _chkManual;
        private CheckBox _chkVerify;
        private NumericUpDown _numDelay;
        private NumericUpDown _numGrace;
        private NumericUpDown _numTimeout;
        private NumericUpDown _numCooldown;
        private NumericUpDown _numAttempts;
        private NumericUpDown _numInterval;
        private NumericUpDown _numPostDelay;
        private NumericUpDown _numLogDays;
        private TextBox _txtVerifyUrl;
        private TextBox _txtVerifyExpect;
        private TextBox _txtOnConnect;

        // 定时检测
        private CheckBox _chkTimedEnabled;
        private CheckBox _chkCloseToTray;
        private ListView _timedList;
        private Button _addTimedButton;
        private Button _editTimedButton;
        private Button _deleteTimedButton;
        private Label _timedNextLabel;
        private Label _timedLastLabel;
        private Label _trayStateLabel;

        // 日志
        private TextBox _logBox;
        private Label _logPathLabel;
        private ToolTip _toolTip;

        public MainForm()
        {
            _rootPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            _configPath = Path.Combine(_rootPath, "config.json");
            _engine = new EngineClient(_rootPath, _configPath);
            _uiSettingsPath = Path.Combine(_rootPath, "ui-settings.json");
            _uiSettings = UiSettingsStore.Load(_uiSettingsPath);

            try
            {
                _config = ConfigStore.Load(_configPath);
            }
            catch (Exception exception)
            {
                _config = ConfigStore.CreateDefault();
                MessageBox.Show("读取 config.json 失败，已使用默认配置：\r\n" + exception.Message,
                    "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Text = "WiFi 优先级兜底";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);
            ClientSize = new Size(1120, 760);
            MinimumSize = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            KeyPreview = true;

            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    Icon = icon;
                }
            }
            catch
            {
            }

            BuildUi();
            LoadConfigIntoControls();
            LoadUiSettingsIntoControls();
            StartShowListener();

            Shown += OnShown;
            FormClosing += OnFormClosing;
            KeyDown += OnKeyDown;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            if (!_engine.ScriptExists)
            {
                MessageBox.Show("找不到 WifiFallback.ps1。\r\n\r\n请把 WifiFallbackUI.exe 和 WifiFallback.ps1、config.json 放在同一个目录。",
                    "缺少引擎文件", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            await RefreshSnapshotAsync(false);

            _refreshTimer = new WinFormsTimer();
            _refreshTimer.Interval = 60000;
            _refreshTimer.Tick += async delegate
            {
                if (!_busy)
                {
                    await RefreshSnapshotAsync(true);
                }
            };
            _refreshTimer.Start();

            _timedTimer = new WinFormsTimer();
            _timedTimer.Interval = 15000;
            _timedTimer.Tick += OnTimedTimerTick;
            _timedTimer.Start();
            UpdateTimedStatus();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowExit && _uiSettings.closeToTray &&
                e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            if (_dirty)
            {
                DialogResult result = MessageBox.Show("有未保存的配置更改，是否保存后退出？",
                    "WiFi 优先级兜底", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Cancel)
                {
                    _allowExit = false;
                    e.Cancel = true;
                    return;
                }
                if (result == DialogResult.Yes)
                {
                    try
                    {
                        ConfigStore.Save(_configPath, _config);
                    }
                    catch (Exception exception)
                    {
                        MessageBox.Show("保存失败：" + exception.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        _allowExit = false;
                        e.Cancel = true;
                        return;
                    }
                }
            }
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
            }
            if (_timedTimer != null)
            {
                _timedTimer.Stop();
                _timedTimer.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_showEvent != null)
            {
                try { _showEvent.Close(); } catch { }
                _showEvent = null;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                BeginRefresh();
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                e.Handled = true;
                SaveAndRefresh();
            }
        }

        // ------------------------------------------------------------------
        // 托盘 / 关闭到托盘
        // ------------------------------------------------------------------

        private void StartShowListener()
        {
            bool created;
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                "Local\\WifiFallbackUIShow", out created);
            _showThread = new Thread(new ThreadStart(delegate
            {
                while (true)
                {
                    try
                    {
                        if (!_showEvent.WaitOne())
                        {
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                    try
                    {
                        if (IsDisposed)
                        {
                            break;
                        }
                        BeginInvoke(new Action(RestoreFromTray));
                    }
                    catch
                    {
                        break;
                    }
                }
            }));
            _showThread.IsBackground = true;
            _showThread.Start();
        }
        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
            UpdateTimedStatus();
            if (!_trayHintShown && _trayIcon != null)
            {
                _trayHintShown = true;
                _trayIcon.ShowBalloonTip(4000, "仍在后台运行",
                    "定时检测和自动兜底会继续运行。双击托盘图标可重新打开。",
                    ToolTipIcon.Info);
            }
            if (_dirty && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(5000, "有未保存的配置",
                    "打开主界面后点“保存配置”。", ToolTipIcon.Warning);
            }
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            UpdateTimedStatus();
        }

        private string GetTrayStatusText()
        {
            if (_snapshot == null || _snapshot.adapter == null)
            {
                return "当前：读取中";
            }
            AdapterInfo adapter = _snapshot.adapter;
            string text;
            if (!adapter.found)
            {
                text = "当前：没有无线网卡";
            }
            else if (adapter.connected)
            {
                text = "当前：" + adapter.ssid + "  " + adapter.signal + "%";
            }
            else
            {
                text = "当前：未连接";
            }
            if (IsFuture(_snapshot.pausedUntil))
            {
                text += "  ·  已暂停";
            }
            return text;
        }

        private void UpdateTrayStatus()
        {
            string text = GetTrayStatusText();
            if (_trayStatusItem != null)
            {
                _trayStatusItem.Text = text;
            }
            if (_trayIcon != null)
            {
                string tip = "WiFi 优先级兜底  ·  " + text;
                if (tip.Length > 63)
                {
                    tip = tip.Substring(0, 63);
                }
                _trayIcon.Text = tip;
            }
        }

        private void UpdateTrayMenu()
        {
            UpdateTrayStatus();
            if (_trayTimedItem != null)
            {
                _trayTimedItem.Checked = _uiSettings.timedEnabled;
            }
            if (_trayCloseItem != null)
            {
                _trayCloseItem.Checked = _uiSettings.closeToTray;
            }
        }

        private void ToggleTimedFromTray()
        {
            _uiSettings.timedEnabled = !_uiSettings.timedEnabled;
            SaveUiSettings();
            if (_chkTimedEnabled != null)
            {
                _chkTimedEnabled.Checked = _uiSettings.timedEnabled;
            }
            UpdateTimedStatus();
            if (_uiSettings.timedEnabled && _uiSettings.timedChecks.Count == 0 && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(4000, "还没有定时时间",
                    "打开主界面，在“定时检测”里添加时间。", ToolTipIcon.Warning);
            }
        }

        private void ToggleCloseToTrayFromTray()
        {
            _uiSettings.closeToTray = !_uiSettings.closeToTray;
            SaveUiSettings();
            if (_chkCloseToTray != null)
            {
                _chkCloseToTray.Checked = _uiSettings.closeToTray;
            }
            if (!_uiSettings.closeToTray && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(3000, "已关闭托盘驻留",
                    "以后点击右上角关闭将直接退出程序。", ToolTipIcon.Info);
            }
        }
        // ------------------------------------------------------------------
        // 状态刷新
        // ------------------------------------------------------------------

        private async void BeginRefresh()
        {
            await RefreshSnapshotAsync(false);
        }

        private async Task RefreshSnapshotAsync(bool silent)
        {
            if (_busy)
            {
                return;
            }
            if (!_engine.ScriptExists)
            {
                ShowEngineError("找不到 WifiFallback.ps1。", silent);
                return;
            }

            SetBusy(true, "正在读取 WiFi 状态...");
            string finalStatus = "就绪";
            try
            {
                string error = "";
                Snapshot snapshot = await Task.Run<Snapshot>(delegate
                {
                    string localError;
                    Snapshot value = _engine.GetSnapshot(out localError);
                    error = localError;
                    return value;
                });

                if (snapshot == null)
                {
                    _snapshot = null;
                    ShowEngineError(error, silent);
                    finalStatus = "状态读取失败";
                }
                else
                {
                    _snapshot = snapshot;
                    ApplySnapshot(snapshot);
                    if (!snapshot.ok)
                    {
                        ShowEngineError(string.IsNullOrEmpty(error) ? snapshot.error : error, silent);
                        finalStatus = "引擎返回错误";
                    }
                    else
                    {
                        _engineErrorShown = false;
                        _taskInstalled = snapshot.task != null && snapshot.task.installed;
                        UpdateTaskUi();
                        finalStatus = "已更新 " + DateTime.Now.ToString("HH:mm:ss");
                    }
                }
            }
            catch (Exception exception)
            {
                ShowEngineError(exception.Message, silent);
                finalStatus = "状态读取失败";
            }
            finally
            {
                SetBusy(false, finalStatus);
            }
        }

        private void ApplySnapshot(Snapshot snapshot)
        {
            if (IsDisposed || snapshot == null)
            {
                return;
            }

            if (!snapshot.ok)
            {
                SetPill("状态异常", Color.FromArgb(253, 236, 236), Color.FromArgb(184, 40, 40));
                _currentSsid.Text = "读取失败";
                _currentMeta.Text = string.IsNullOrEmpty(snapshot.error) ? "请检查引擎日志" : snapshot.error;
                _currentMode.Text = "";
                _signalBars.Signal = 0;
                _signalPercent.Text = "--";
                _decisionTitle.Text = "引擎返回错误";
                _decisionMessage.Text = snapshot.error;
                _decisionSchedule.Text = "";
                _recentLog.Text = _engine.GetLogTail(8);
                UpdateTrayStatus();
                return;
            }

            AdapterInfo adapter = snapshot.adapter ?? new AdapterInfo();
            if (!adapter.found)
            {
                _currentSsid.Text = "没有无线网卡";
                _currentMeta.Text = "请确认 WLAN AutoConfig 服务正在运行";
                _currentMode.Text = "";
                _signalBars.Signal = 0;
                _signalPercent.Text = "--";
            }
            else if (adapter.connected)
            {
                _currentSsid.Text = adapter.ssid;
                _currentMeta.Text = adapter.name + "  ·  " + adapter.mode;
                _signalBars.Signal = adapter.signal;
                _signalPercent.Text = adapter.signal + "%";
                _currentMode.Text = "";
            }
            else
            {
                _currentSsid.Text = "未连接";
                _currentMeta.Text = adapter.name + "  ·  等待连接";
                _signalBars.Signal = 0;
                _signalPercent.Text = "--";
                _currentMode.Text = "";
            }

            PlanInfo plan = snapshot.plan ?? new PlanInfo();
            _decisionTitle.Text = ReasonText(plan.reason);
            _decisionMessage.Text = plan.message;
            _decisionTitle.ForeColor = plan.action == "connect" ? Accent : TextPrimary;

            ScheduleInfo schedule = snapshot.schedule ?? new ScheduleInfo();
            string scheduleText = string.IsNullOrEmpty(schedule.text) ? "未配置时间窗口" : schedule.text;
            if (!schedule.enabled)
            {
                scheduleText += "  ·  全天生效";
            }
            else if (schedule.active)
            {
                scheduleText += "  ·  当前在窗口内";
            }
            else
            {
                scheduleText += "  ·  当前在窗口外";
            }
            if (IsFuture(snapshot.pausedUntil))
            {
                scheduleText = "已暂停到 " + FormatIso(snapshot.pausedUntil, "MM-dd HH:mm") + "  ·  " + scheduleText;
            }
            else if (IsFuture(snapshot.manualUntil))
            {
                scheduleText = "手动连接保护到 " + FormatIso(snapshot.manualUntil, "HH:mm") + "  ·  " + scheduleText;
            }
            ConnectivityInfo connectivity = snapshot.connectivity;
            if (connectivity != null && connectivity.@checked && !connectivity.internet)
            {
                if (connectivity.captivePortal)
                {
                    _currentMeta.Text = _currentMeta.Text + "  ·  需要网页认证";
                    if (!string.IsNullOrEmpty(connectivity.portalUrl))
                    {
                        scheduleText = "认证页：" + connectivity.portalUrl + "  ·  " + scheduleText;
                    }
                }
                else
                {
                    _currentMeta.Text = _currentMeta.Text + "  ·  外网不可用";
                }
            }
            _decisionSchedule.Text = scheduleText;

            if (IsFuture(snapshot.pausedUntil))
            {
                SetPill("已暂停", Color.FromArgb(255, 242, 224), Color.FromArgb(166, 94, 0));
            }
            else if (schedule.enabled && schedule.active)
            {
                SetPill("窗口内", Color.FromArgb(231, 246, 236), Color.FromArgb(18, 122, 69));
            }
            else
            {
                SetPill("窗口外", Color.FromArgb(238, 241, 245), Color.FromArgb(92, 101, 112));
            }

            _recentLog.Text = _engine.GetLogTail(8);
            RefreshNetworkList();

            if (snapshot.task != null)
            {
                _taskInstalled = snapshot.task.installed;
            }
            UpdateTaskUi();
            UpdateTimedStatus();

            if (_pages != null && _pages.Length > 4 && _pages[4].Visible)
            {
                RefreshLog();
            }
        }

        private void ShowEngineError(string message, bool silent)
        {
            SetPill("状态异常", Color.FromArgb(253, 236, 236), Color.FromArgb(184, 40, 40));
            SetStatus("状态读取失败");
            if (!silent && !_engineErrorShown)
            {
                _engineErrorShown = true;
                MessageBox.Show(message, "无法读取 WiFi 状态", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetPill(string text, Color fill, Color textColor)
        {
            _statusPill.Text = text;
            _statusPill.FillColor = fill;
            _statusPill.TextColor = textColor;
        }

        private static string ReasonText(string reason)
        {
            switch (reason)
            {
                case "outside-window": return "不在触发时间段";
                case "already-best": return "已连接最高优先级";
                case "waiting": return "等待确认更高优先级";
                case "preempt": return "切换到更高优先级";
                case "portal-fallback": return "网页认证失败，降级";
                case "portal-only": return "需要网页认证，无其他候选";
                case "fallback": return "断线兜底";
                case "no-candidate": return "没有可用候选";
                case "paused": return "已暂停";
                case "manual":
                case "manual-grace": return "手动连接保护";
                case "unknown-network": return "当前为非列表网络";
                case "preempt-disabled": return "保持当前连接";
                case "no-adapter": return "没有无线网卡";
                default: return string.IsNullOrEmpty(reason) ? "等待状态" : reason;
            }
        }

        private static bool IsFuture(string iso)
        {
            if (string.IsNullOrEmpty(iso))
            {
                return false;
            }
            DateTime value;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return value > DateTime.Now;
            }
            return false;
        }

        private static string FormatIso(string iso, string format)
        {
            DateTime value;
            if (!string.IsNullOrEmpty(iso) &&
                DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return value.ToString(format);
            }
            return "";
        }

        // ------------------------------------------------------------------
        // 忙碌状态 / 保存
        // ------------------------------------------------------------------

        private void SetBusy(bool busy, string message)
        {
            if (IsDisposed)
            {
                return;
            }
            _busy = busy;
            UseWaitCursor = busy;
            if (_checkButton != null) _checkButton.Enabled = !busy && _engine.ScriptExists;
            if (_pauseButton != null) _pauseButton.Enabled = !busy && _engine.ScriptExists;
            if (_resumeButton != null) _resumeButton.Enabled = !busy && _engine.ScriptExists;
            if (_taskButton != null) _taskButton.Enabled = !busy && _engine.ScriptExists;
            if (_refreshButton != null) _refreshButton.Enabled = !busy;
            if (_saveButton != null) _saveButton.Enabled = !busy;
            if (_reloadButton != null) _reloadButton.Enabled = !busy;
            if (!string.IsNullOrEmpty(message))
            {
                _statusLabel.Text = message;
            }
        }

        private void SetStatus(string message)
        {
            if (IsDisposed)
            {
                return;
            }
            _statusLabel.Text = message;
        }

        private void MarkDirty()
        {
            if (_loading)
            {
                return;
            }
            _dirty = true;
            _dirtyLabel.Visible = true;
            _saveButton.Text = "保存配置 *";
        }

        private void UpdateDirtyUi()
        {
            _dirtyLabel.Visible = _dirty;
            _saveButton.Text = _dirty ? "保存配置 *" : "保存配置";
        }

        private async void SaveAndRefresh()
        {
            await SaveConfigAsync();
        }

        private async Task<bool> SaveConfigAsync()
        {
            if (_busy)
            {
                return false;
            }
            if (!ValidateConfig())
            {
                return false;
            }

            int oldInterval = _config.behavior.taskIntervalMinutes;
            try
            {
                for (int index = 0; index < _config.networks.Count; index++)
                {
                    _config.networks[index].priority = index + 1;
                }
                ConfigStore.Save(_configPath, _config);
                _dirty = false;
                UpdateDirtyUi();
                SetStatus("配置已保存");
            }
            catch (Exception exception)
            {
                MessageBox.Show("保存配置失败：\r\n" + exception.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            LoadConfigIntoControls();
            await RefreshSnapshotAsync(true);
            SetStatus("配置已保存");

            if (_taskInstalled && oldInterval != _config.behavior.taskIntervalMinutes)
            {
                DialogResult result = MessageBox.Show(
                    "检查间隔已修改，需要重新安装计划任务才能生效。\r\n现在重新安装吗？",
                    "检查间隔", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    await InstallOrUninstallTaskAsync(true, true);
                }
            }
            return true;
        }

        private bool ValidateConfig()
        {
            if (_config.networks.Count == 0)
            {
                MessageBox.Show("请至少添加一个 WiFi。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SwitchPage(1);
                return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkConfig network in _config.networks)
            {
                if (string.IsNullOrEmpty(network.ssid))
                {
                    MessageBox.Show("有 WiFi 的 SSID 为空，请检查。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SwitchPage(1);
                    return false;
                }
                if (!seen.Add(network.ssid))
                {
                    MessageBox.Show("SSID 重复：" + network.ssid, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SwitchPage(1);
                    return false;
                }
            }

            if (_config.schedule.enabled && (_config.schedule.windows == null || _config.schedule.windows.Count == 0))
            {
                MessageBox.Show("启用了时间窗口，但还没有添加任何窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SwitchPage(2);
                return false;
            }
            if (_config.schedule.windows != null)
            {
                foreach (WindowConfig window in _config.schedule.windows)
                {
                    if (window.days == null || window.days.Count == 0)
                    {
                        MessageBox.Show("有一个时间窗口没有选择星期。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        SwitchPage(2);
                        return false;
                    }
                    if (!IsValidTime(window.start) || !IsValidTime(window.end))
                    {
                        MessageBox.Show("时间格式必须是 HH:mm。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        SwitchPage(2);
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsValidTime(string text)
        {
            DateTime value;
            return DateTime.TryParseExact(text, new string[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        // ------------------------------------------------------------------
        // 操作按钮
        // ------------------------------------------------------------------

        private async void OnCheckNowClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (_dirty)
            {
                if (!await SaveConfigAsync())
                {
                    return;
                }
            }
            DialogResult result = MessageBox.Show(
                "将忽略时间窗口，立即检查并按优先级连接/切换 WiFi。\r\n这可能会短暂断开当前网络。继续吗？",
                "立即检查", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true, "正在检查并连接...");
            ProcessResult processResult = await Task.Run<ProcessResult>(delegate { return _engine.RunCheckNow(); });
            SetBusy(false, "正在刷新状态...");
            await RefreshSnapshotAsync(true);
            if (processResult.ExitCode == 0)
            {
                SetStatus("检查完成");
            }
            else if (processResult.ExitCode == 1)
            {
                SetStatus("检查完成，但连接未成功");
                MessageBox.Show("候选网络连接失败，请查看运行日志。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetStatus("检查失败");
                MessageBox.Show("执行检查时出错，请查看运行日志。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void OnPauseClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            SetBusy(true, "正在暂停自动切换...");
            ProcessResult result = await Task.Run<ProcessResult>(delegate { return _engine.Pause(60); });
            SetBusy(false, "正在刷新状态...");
            await RefreshSnapshotAsync(true);
            if (result.ExitCode == 0)
            {
                SetStatus("已暂停 1 小时");
            }
            else
            {
                SetStatus("暂停失败");
            }
        }

        private async void OnResumeClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            SetBusy(true, "正在恢复自动切换...");
            ProcessResult result = await Task.Run<ProcessResult>(delegate { return _engine.Resume(); });
            SetBusy(false, "正在刷新状态...");
            await RefreshSnapshotAsync(true);
            if (result.ExitCode == 0)
            {
                SetStatus("已恢复自动切换");
            }
            else
            {
                SetStatus("恢复失败");
            }
        }

        private async void OnTaskButtonClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            if (_dirty)
            {
                if (!await SaveConfigAsync())
                {
                    return;
                }
            }
            await InstallOrUninstallTaskAsync(!_taskInstalled, false);
        }

        private async Task InstallOrUninstallTaskAsync(bool install, bool skipConfirm)
        {
            if (!skipConfirm)
            {
                string message = install
                    ? "将创建计划任务 WifiFallback：登录后每分钟检查一次，只在时间窗口内动作。\r\n\r\n继续安装吗？"
                    : "将删除计划任务 WifiFallback，自动兜底会停止（Windows 自带的自动连接仍然有效）。\r\n\r\n继续卸载吗？";
                DialogResult confirm = MessageBox.Show(message, install ? "安装计划任务" : "卸载计划任务",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            SetBusy(true, install ? "正在安装计划任务..." : "正在卸载计划任务...");
            ProcessResult result = await Task.Run<ProcessResult>(delegate
            {
                return install ? _engine.InstallTask() : _engine.UninstallTask();
            });

            if (result.ExitCode != 0)
            {
                string detail = string.IsNullOrEmpty(result.Error) ? result.Output : result.Error;
                MessageBox.Show((install ? "安装计划任务失败。" : "卸载计划任务失败。") +
                    (string.IsNullOrEmpty(detail) ? "" : "\r\n\r\n" + detail.Trim()),
                    "计划任务", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetBusy(false, "计划任务操作失败");
            }
            else
            {
                SetBusy(false, install ? "计划任务已安装" : "计划任务已卸载");
            }

            _taskInstalled = _engine.IsTaskInstalled();
            UpdateTaskUi();
            await RefreshSnapshotAsync(true);
            SetStatus(install ? "计划任务已安装，约一分钟后生效" : "计划任务已卸载");
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            BeginRefresh();
        }

        private void OnReloadClick(object sender, EventArgs e)
        {
            if (_dirty)
            {
                DialogResult result = MessageBox.Show("放弃未保存的更改并重新加载配置？",
                    "重新加载", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }
            try
            {
                _config = ConfigStore.Load(_configPath);
                _dirty = false;
                LoadConfigIntoControls();
                SetStatus("配置已重新加载");
                BeginRefresh();
            }
            catch (Exception exception)
            {
                MessageBox.Show("重新加载失败：" + exception.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnViewLogClick(object sender, EventArgs e)
        {
            SwitchPage(4);
            RefreshLog();
        }

        private void OnOpenLogFolderClick(object sender, EventArgs e)
        {
            if (!Directory.Exists(_engine.LogDirectory))
            {
                Directory.CreateDirectory(_engine.LogDirectory);
            }
            EngineClient.OpenFolder(_engine.LogDirectory);
        }

        private void OnOpenConfigFolderClick(object sender, EventArgs e)
        {
            EngineClient.OpenFolder(_rootPath);
        }

        private void OnOpenLogFileClick(object sender, EventArgs e)
        {
            string path = _engine.LatestLogPath;
            if (string.IsNullOrEmpty(path))
            {
                OnOpenLogFolderClick(sender, e);
                return;
            }
            EngineClient.OpenFile(path);
        }

        private void RefreshLog()
        {
            if (_logBox == null)
            {
                return;
            }
            string path = _engine.LatestLogPath;
            if (_logPathLabel != null)
            {
                _logPathLabel.Text = string.IsNullOrEmpty(path) ? "暂无日志文件" : path;
            }
            string text = _engine.GetLogTail(600);
            _logBox.Text = string.IsNullOrEmpty(text) ? "暂无日志。" : text;
            if (_logBox.TextLength > 0)
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
        }

        private void UpdateTaskUi()
        {
            if (_taskStatusLabel != null)
            {
                _taskStatusLabel.Text = _taskInstalled ? "计划任务：已安装" : "计划任务：未安装";
                _taskStatusLabel.ForeColor = _taskInstalled ? Success : TextSecondary;
            }
            if (_taskButton != null)
            {
                _taskButton.Text = _taskInstalled ? "卸载计划任务" : "安装计划任务";
            }
        }

        // ------------------------------------------------------------------
        // 定时检测（桌面版运行时生效）
        // ------------------------------------------------------------------

        private void LoadUiSettingsIntoControls()
        {
            bool oldLoading = _loading;
            _loading = true;
            try
            {
                _chkTimedEnabled.Checked = _uiSettings.timedEnabled;
                _chkCloseToTray.Checked = _uiSettings.closeToTray;
            }
            finally
            {
                _loading = oldLoading;
            }
            RefreshTimedList();
            UpdateTimedStatus();
        }

        private void SaveUiSettings()
        {
            try
            {
                UiSettingsStore.Save(_uiSettingsPath, _uiSettings);
            }
            catch (Exception exception)
            {
                SetStatus("保存定时设置失败：" + exception.Message);
            }
        }

        private void OnTimedEnabledChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }
            _uiSettings.timedEnabled = _chkTimedEnabled.Checked;
            SaveUiSettings();
            UpdateTimedStatus();
            SetStatus(_uiSettings.timedEnabled ? "定时检测已启用" : "定时检测已关闭");
        }

        private void OnCloseToTrayChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }
            _uiSettings.closeToTray = _chkCloseToTray.Checked;
            SaveUiSettings();
            UpdateTimedStatus();
            SetStatus(_uiSettings.closeToTray ? "关闭窗口后将留在托盘" : "关闭窗口将直接退出");
        }

        private void RefreshTimedList()
        {
            if (_timedList == null)
            {
                return;
            }
            string selectedKey = "";
            if (_timedList.SelectedItems.Count > 0)
            {
                selectedKey = _timedList.SelectedItems[0].Name;
            }

            _loadingList = true;
            _timedList.BeginUpdate();
            try
            {
                _timedList.Items.Clear();
                int index = 0;
                foreach (TimedCheckConfig item in _uiSettings.timedChecks)
                {
                    index++;
                    ListViewItem row = new ListViewItem(item.enabled ? item.time : item.time + "（停用）");
                    row.SubItems.Add(BuildDaysText(item.days));
                    row.SubItems.Add(ActionText(item.action));
                    row.Tag = item;
                    row.Name = index.ToString();
                    if (!item.enabled)
                    {
                        row.ForeColor = Color.FromArgb(150, 156, 165);
                    }
                    _timedList.Items.Add(row);
                    if (row.Name == selectedKey)
                    {
                        row.Selected = true;
                    }
                }
            }
            finally
            {
                _timedList.EndUpdate();
                _loadingList = false;
            }
            UpdateTimedButtons();
        }

        private void UpdateTimedButtons()
        {
            if (_timedList == null)
            {
                return;
            }
            bool selected = _timedList.SelectedItems.Count > 0;
            _editTimedButton.Enabled = selected;
            _deleteTimedButton.Enabled = selected;
        }

        private void OnTimedSelectionChanged(object sender, EventArgs e)
        {
            UpdateTimedButtons();
        }

        private void OnAddTimedClick(object sender, EventArgs e)
        {
            using (TimedCheckDialog dialog = new TimedCheckDialog(null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _uiSettings.timedChecks.Add(dialog.Result);
                SaveUiSettings();
                RefreshTimedList();
                UpdateTimedStatus();
                SetStatus("已添加定时检测");
            }
        }

        private void OnEditTimedClick(object sender, EventArgs e)
        {
            if (_timedList.SelectedItems.Count == 0)
            {
                return;
            }
            TimedCheckConfig item = _timedList.SelectedItems[0].Tag as TimedCheckConfig;
            if (item == null)
            {
                return;
            }
            using (TimedCheckDialog dialog = new TimedCheckDialog(item))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                item.time = dialog.Result.time;
                item.days = dialog.Result.days;
                item.action = dialog.Result.action;
                item.enabled = dialog.Result.enabled;
                SaveUiSettings();
                RefreshTimedList();
                UpdateTimedStatus();
                SetStatus("已更新定时检测");
            }
        }

        private void OnDeleteTimedClick(object sender, EventArgs e)
        {
            if (_timedList.SelectedItems.Count == 0)
            {
                return;
            }
            TimedCheckConfig item = _timedList.SelectedItems[0].Tag as TimedCheckConfig;
            if (item == null)
            {
                return;
            }
            if (MessageBox.Show("确定删除 " + item.time + " 的定时检测吗？", "删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            _uiSettings.timedChecks.Remove(item);
            SaveUiSettings();
            RefreshTimedList();
            UpdateTimedStatus();
            SetStatus("已删除定时检测");
        }

        private void OnTimedDoubleClick(object sender, EventArgs e)
        {
            OnEditTimedClick(sender, e);
        }

        private async void OnTimedTimerTick(object sender, EventArgs e)
        {
            if (_uiSettings == null || !_uiSettings.timedEnabled || _busy || _timedRunning)
            {
                return;
            }

            DateTime now = DateTime.Now;
            string timeKey = now.ToString("HH:mm");
            int isoDay = now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;
            string dayKey = now.ToString("yyyyMMdd");

            for (int index = 0; index < _uiSettings.timedChecks.Count; index++)
            {
                TimedCheckConfig item = _uiSettings.timedChecks[index];
                if (item == null || !item.enabled || item.time != timeKey)
                {
                    continue;
                }
                if (!HasDay(item.days, isoDay))
                {
                    continue;
                }
                string key = dayKey + "|" + timeKey + "|" + index;
                if (_timedDone.Contains(key))
                {
                    continue;
                }
                _timedDone.Add(key);
                CleanupTimedDone(dayKey);
                await ExecuteTimedCheckAsync(item, now);
                return;
            }
        }

        private static bool HasDay(List<object> days, int isoDay)
        {
            if (days == null)
            {
                return false;
            }
            foreach (object day in days)
            {
                if (DayUtil.ToNumber(day) == isoDay)
                {
                    return true;
                }
            }
            return false;
        }

        private void CleanupTimedDone(string todayKey)
        {
            List<string> obsolete = new List<string>();
            foreach (string key in _timedDone)
            {
                if (!key.StartsWith(todayKey, StringComparison.Ordinal))
                {
                    obsolete.Add(key);
                }
            }
            foreach (string key in obsolete)
            {
                _timedDone.Remove(key);
            }
        }

        private async Task ExecuteTimedCheckAsync(TimedCheckConfig item, DateTime now)
        {
            _timedRunning = true;
            string action = ActionText(item.action);
            ProcessResult result = new ProcessResult();
            WriteTimedLog("开始：" + action + "（" + item.time + "）");
            try
            {
                if (string.Equals(item.action, "check", StringComparison.OrdinalIgnoreCase))
                {
                    await RefreshSnapshotAsync(true);
                    result.ExitCode = 0;
                }
                else
                {
                    SetBusy(true, "定时检测：正在检查并连接...");
                    result = await Task.Run<ProcessResult>(delegate { return _engine.RunCheckNow(); });
                    SetBusy(false, "正在刷新状态...");
                    await RefreshSnapshotAsync(true);
                }

                string reason = "";
                if (string.Equals(item.action, "check", StringComparison.OrdinalIgnoreCase) &&
                    _snapshot != null && _snapshot.plan != null)
                {
                    reason = _snapshot.plan.reason + " " + _snapshot.plan.message;
                }
                string resultText;
                if (result.ExitCode == 0)
                {
                    resultText = "完成";
                }
                else if (result.ExitCode == 1)
                {
                    resultText = "有候选连接失败";
                }
                else
                {
                    resultText = "执行失败";
                }
                WriteTimedLog(resultText + (string.IsNullOrEmpty(reason) ? "" : "；" + reason));
                _lastTimedText = now.ToString("MM-dd HH:mm") + "  " + action + " · " + resultText;
                SetStatus("定时检测：" + resultText);
                if (result.ExitCode != 0 && _trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(4000, "定时检测失败",
                        "请打开主界面查看日志。", ToolTipIcon.Warning);
                }
            }
            catch (Exception exception)
            {
                WriteTimedLog("异常：" + exception.Message);
                _lastTimedText = now.ToString("MM-dd HH:mm") + "  " + action + " · 异常";
                SetStatus("定时检测异常：" + exception.Message);
            }
            finally
            {
                _timedRunning = false;
                SetBusy(false, "");
                UpdateTimedStatus();
            }
        }

        private static string ActionText(string action)
        {
            return string.Equals(action, "check", StringComparison.OrdinalIgnoreCase) ? "仅检测" : "检测并连接";
        }

        private void WriteTimedLog(string message)
        {
            try
            {
                string directory = _engine.LogDirectory;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string path = Path.Combine(directory, "ui-timed.log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + "\r\n",
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private void UpdateTimedStatus()
        {
            if (_timedNextLabel != null)
            {
                _timedNextLabel.Text = GetNextTimedText();
            }
            if (_timedLastLabel != null)
            {
                _timedLastLabel.Text = string.IsNullOrEmpty(_lastTimedText)
                    ? "上次执行：—"
                    : "上次执行：" + _lastTimedText;
            }
            if (_trayStateLabel != null)
            {
                bool hiddenToTray = !Visible && !ShowInTaskbar;
                _trayStateLabel.Text = hiddenToTray
                    ? "已隐藏到系统托盘，定时检测继续运行"
                    : "主窗口运行中，定时检测已就绪";
            }
            if (_trayTimedItem != null)
            {
                _trayTimedItem.Checked = _uiSettings.timedEnabled;
            }
            UpdateTrayStatus();
            UpdateTimedButtons();
        }

        private string GetNextTimedText()
        {
            if (!_uiSettings.timedEnabled)
            {
                return "定时检测：未启用";
            }
            if (_uiSettings.timedChecks.Count == 0)
            {
                return "定时检测：还没有添加时间";
            }

            DateTime now = DateTime.Now;
            DateTime best = DateTime.MaxValue;
            TimedCheckConfig bestItem = null;
            for (int offset = 0; offset <= 7; offset++)
            {
                DateTime date = now.Date.AddDays(offset);
                int isoDay = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
                foreach (TimedCheckConfig item in _uiSettings.timedChecks)
                {
                    if (item == null || !item.enabled || !HasDay(item.days, isoDay))
                    {
                        continue;
                    }
                    TimeSpan time;
                    if (!TryParseTime(item.time, out time))
                    {
                        continue;
                    }
                    DateTime moment = date.Add(time);
                    if (moment <= now)
                    {
                        continue;
                    }
                    if (moment < best)
                    {
                        best = moment;
                        bestItem = item;
                    }
                }
            }
            if (bestItem == null)
            {
                return "定时检测：没有可执行的时间";
            }
            return "下次执行：" + best.ToString("MM-dd HH:mm") + " · " + ActionText(bestItem.action);
        }

        private static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            DateTime parsed;
            if (DateTime.TryParseExact(text, new string[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                time = parsed.TimeOfDay;
                return true;
            }
            return false;
        }
        // ------------------------------------------------------------------
        // 配置 -> 控件
        // ------------------------------------------------------------------

        private void LoadConfigIntoControls()
        {
            _loading = true;
            try
            {
                _scheduleEnabled.Checked = _config.schedule.enabled;
                _scheduleWarning.Visible = !_config.schedule.enabled;

                SetNumeric(_numDelay, _config.behavior.preemptDelayChecks);
                SetNumeric(_numGrace, _config.behavior.manualGraceMinutes);
                SetNumeric(_numTimeout, _config.behavior.connectTimeoutSec);
                SetNumeric(_numCooldown, _config.behavior.failedRetryMinutes);
                SetNumeric(_numAttempts, _config.behavior.maxAttemptsPerRun);
                SetNumeric(_numInterval, _config.behavior.taskIntervalMinutes);
                SetNumeric(_numPostDelay, _config.behavior.postConnectDelaySec);
                SetNumeric(_numLogDays, _config.behavior.logKeepDays);


                _chkPreempt.Checked = _config.behavior.preempt;
                _chkPreemptUnknown.Checked = _config.behavior.preemptUnknown;
                _chkManual.Checked = _config.behavior.respectManualMode;
                _chkVerify.Checked = _config.behavior.verifyInternet;
                _txtVerifyUrl.Text = _config.behavior.verifyUrl;
                _txtVerifyExpect.Text = _config.behavior.verifyExpect;
                _txtOnConnect.Text = _config.behavior.onConnectCommand;

                RefreshNetworkList();
                RefreshWindowList();
            }
            finally
            {
                _loading = false;
            }
            _dirty = false;
            UpdateDirtyUi();
            UpdateTaskUi();
        }

        private static void SetNumeric(NumericUpDown box, int value)
        {
            int clamped = value;
            if (clamped < (int)box.Minimum) clamped = (int)box.Minimum;
            if (clamped > (int)box.Maximum) clamped = (int)box.Maximum;
            box.Value = clamped;
        }

        // ------------------------------------------------------------------
        // 优先级列表
        // ------------------------------------------------------------------

        private void RefreshNetworkList()
        {
            if (_networkList == null)
            {
                return;
            }
            string selectedSsid = "";
            if (_networkList.SelectedItems.Count > 0)
            {
                NetworkConfig selected = _networkList.SelectedItems[0].Tag as NetworkConfig;
                if (selected != null)
                {
                    selectedSsid = selected.ssid;
                }
            }

            _loadingList = true;
            _networkList.BeginUpdate();
            try
            {
                _networkList.Items.Clear();
                for (int index = 0; index < _config.networks.Count; index++)
                {
                    NetworkConfig network = _config.networks[index];
                    network.priority = index + 1;
                    NetworkStatus status = FindNetworkStatus(network.ssid);
                    ListViewItem item = new ListViewItem((index + 1).ToString());
                    item.SubItems.Add(network.ssid);
                    item.SubItems.Add(network.profile);
                    item.SubItems.Add(network.minSignal + "%");
                    item.SubItems.Add(BuildNetworkStatusText(network, status));
                    item.Checked = network.enabled;
                    item.Tag = network;
                    if (status != null && status.current)
                    {
                        item.BackColor = Color.FromArgb(234, 247, 239);
                    }
                    else if (!network.enabled)
                    {
                        item.ForeColor = Color.FromArgb(150, 156, 165);
                    }
                    _networkList.Items.Add(item);
                    if (!string.IsNullOrEmpty(selectedSsid) &&
                        string.Equals(network.ssid, selectedSsid, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Selected = true;
                        item.Focused = true;
                    }
                }
            }
            finally
            {
                _networkList.EndUpdate();
                _loadingList = false;
            }
            UpdateNetworkButtons();
        }

        private NetworkStatus FindNetworkStatus(string ssid)
        {
            if (_snapshot == null || _snapshot.networks == null)
            {
                return null;
            }
            foreach (NetworkStatus status in _snapshot.networks)
            {
                if (status != null && string.Equals(status.ssid, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    return status;
                }
            }
            return null;
        }

        private static string BuildNetworkStatusText(NetworkConfig network, NetworkStatus status)
        {
            if (!network.enabled)
            {
                return "已禁用";
            }
            if (status == null)
            {
                return "未检查";
            }
            List<string> parts = new List<string>();
            if (status.current)
            {
                parts.Add("当前");
            }
            if (status.visible)
            {
                parts.Add("可见 " + status.signal + "%");
            }
            else
            {
                parts.Add("不可见");
            }
            if (status.failure)
            {
                parts.Add("失败冷却");
            }
            return string.Join(" · ", parts.ToArray());
        }

        private void OnNetworkSelectionChanged(object sender, EventArgs e)
        {
            UpdateNetworkButtons();
        }

        private void UpdateNetworkButtons()
        {
            if (_networkList == null)
            {
                return;
            }
            bool hasSelection = _networkList.SelectedItems.Count > 0;
            int selectedIndex = hasSelection ? _networkList.SelectedIndices[0] : -1;
            _editNetworkButton.Enabled = hasSelection;
            _deleteNetworkButton.Enabled = hasSelection;
            _upNetworkButton.Enabled = hasSelection && selectedIndex > 0;
            _downNetworkButton.Enabled = hasSelection && selectedIndex >= 0 && selectedIndex < _config.networks.Count - 1;
        }

        private void OnNetworkItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loadingList)
            {
                return;
            }
            NetworkConfig network = _networkList.Items[e.Index].Tag as NetworkConfig;
            if (network == null)
            {
                return;
            }
            bool newEnabled = e.NewValue == CheckState.Checked;
            if (network.enabled == newEnabled)
            {
                return;
            }
            network.enabled = newEnabled;
            ListViewItem item = _networkList.Items[e.Index];
            if (item.SubItems.Count > 4)
            {
                item.SubItems[4].Text = BuildNetworkStatusText(network, FindNetworkStatus(network.ssid));
            }
            item.ForeColor = network.enabled ? TextPrimary : Color.FromArgb(150, 156, 165);
            MarkDirty();
        }

        private void OnNetworkDoubleClick(object sender, EventArgs e)
        {
            EditSelectedNetwork();
        }

        private void OnAddNetworkClick(object sender, EventArgs e)
        {
            List<VisibleInfo> visible = new List<VisibleInfo>();
            List<string> profiles = new List<string>();
            if (_snapshot != null)
            {
                if (_snapshot.visible != null) visible = _snapshot.visible;
                if (_snapshot.profiles != null) profiles = _snapshot.profiles;
            }
            using (NetworkDialog dialog = new NetworkDialog(null, visible, profiles))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (ContainsSsid(dialog.Result.ssid, null))
                {
                    MessageBox.Show("这个 SSID 已经在列表里了。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                dialog.Result.priority = _config.networks.Count + 1;
                _config.networks.Add(dialog.Result);
                RenumberNetworks();
                _dirty = true;
                UpdateDirtyUi();
                RefreshNetworkList();
                SelectNetwork(dialog.Result.ssid);
            }
        }

        private void OnEditNetworkClick(object sender, EventArgs e)
        {
            EditSelectedNetwork();
        }

        private void EditSelectedNetwork()
        {
            if (_networkList.SelectedItems.Count == 0)
            {
                return;
            }
            NetworkConfig network = _networkList.SelectedItems[0].Tag as NetworkConfig;
            if (network == null)
            {
                return;
            }
            List<VisibleInfo> visible = new List<VisibleInfo>();
            List<string> profiles = new List<string>();
            if (_snapshot != null)
            {
                if (_snapshot.visible != null) visible = _snapshot.visible;
                if (_snapshot.profiles != null) profiles = _snapshot.profiles;
            }
            using (NetworkDialog dialog = new NetworkDialog(network, visible, profiles))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (ContainsSsid(dialog.Result.ssid, network))
                {
                    MessageBox.Show("这个 SSID 已经在列表里了。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                network.ssid = dialog.Result.ssid;
                network.profile = dialog.Result.profile;
                network.minSignal = dialog.Result.minSignal;
                network.allowHidden = dialog.Result.allowHidden;
                network.enabled = dialog.Result.enabled;
                _dirty = true;
                UpdateDirtyUi();
                RefreshNetworkList();
                SelectNetwork(network.ssid);
            }
        }

        private void OnDeleteNetworkClick(object sender, EventArgs e)
        {
            if (_networkList.SelectedItems.Count == 0)
            {
                return;
            }
            NetworkConfig network = _networkList.SelectedItems[0].Tag as NetworkConfig;
            if (network == null)
            {
                return;
            }
            if (MessageBox.Show("确定删除 WiFi “" + network.ssid + "” 吗？", "删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            _config.networks.Remove(network);
            RenumberNetworks();
            _dirty = true;
            UpdateDirtyUi();
            RefreshNetworkList();
        }

        private void OnMoveNetworkUp(object sender, EventArgs e)
        {
            MoveSelectedNetwork(-1);
        }

        private void OnMoveNetworkDown(object sender, EventArgs e)
        {
            MoveSelectedNetwork(1);
        }

        private void MoveSelectedNetwork(int delta)
        {
            if (_networkList.SelectedItems.Count == 0)
            {
                return;
            }
            int index = _networkList.SelectedIndices[0];
            int target = index + delta;
            if (target < 0 || target >= _config.networks.Count)
            {
                return;
            }
            NetworkConfig network = _config.networks[index];
            _config.networks.RemoveAt(index);
            _config.networks.Insert(target, network);
            RenumberNetworks();
            _dirty = true;
            UpdateDirtyUi();
            RefreshNetworkList();
            SelectNetwork(network.ssid);
        }

        private void RenumberNetworks()
        {
            for (int index = 0; index < _config.networks.Count; index++)
            {
                _config.networks[index].priority = index + 1;
            }
        }

        private bool ContainsSsid(string ssid, NetworkConfig except)
        {
            foreach (NetworkConfig network in _config.networks)
            {
                if (network == except)
                {
                    continue;
                }
                if (string.Equals(network.ssid, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void SelectNetwork(string ssid)
        {
            foreach (ListViewItem item in _networkList.Items)
            {
                NetworkConfig network = item.Tag as NetworkConfig;
                if (network != null && string.Equals(network.ssid, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }
        }

        private void OnNetworkKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                OnDeleteNetworkClick(sender, EventArgs.Empty);
            }
        }

        // ------------------------------------------------------------------
        // 时间窗口
        // ------------------------------------------------------------------

        private void RefreshWindowList()
        {
            if (_windowList == null)
            {
                return;
            }
            _loadingList = true;
            _windowList.BeginUpdate();
            try
            {
                _windowList.Items.Clear();
                foreach (WindowConfig window in _config.schedule.windows)
                {
                    ListViewItem item = new ListViewItem(BuildDaysText(window.days));
                    item.SubItems.Add(window.start);
                    item.SubItems.Add(window.end);
                    item.Tag = window;
                    _windowList.Items.Add(item);
                }
                if (_windowList.Items.Count > 0)
                {
                    _windowList.Items[0].Selected = true;
                }
            }
            finally
            {
                _windowList.EndUpdate();
                _loadingList = false;
            }
            UpdateWindowButtons();
        }

        private static string BuildDaysText(List<object> days)
        {
            if (days == null || days.Count == 0)
            {
                return "—";
            }
            if (days.Count == 7)
            {
                return "每天";
            }
            string[] names = new string[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            List<string> parts = new List<string>();
            foreach (object item in days)
            {
                int day = DayUtil.ToNumber(item);
                if (day >= 1 && day <= 7)
                {
                    parts.Add(names[day]);
                }
            }
            return string.Join("、", parts.ToArray());
        }

        private void OnWindowSelectionChanged(object sender, EventArgs e)
        {
            UpdateWindowButtons();
        }

        private void UpdateWindowButtons()
        {
            if (_windowList == null)
            {
                return;
            }
            bool hasSelection = _windowList.SelectedItems.Count > 0;
            _editWindowButton.Enabled = hasSelection;
            _deleteWindowButton.Enabled = hasSelection;
        }

        private void OnScheduleEnabledChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }
            _config.schedule.enabled = _scheduleEnabled.Checked;
            _scheduleWarning.Visible = !_scheduleEnabled.Checked;
            MarkDirty();
        }

        private void OnAddWindowClick(object sender, EventArgs e)
        {
            using (WindowDialog dialog = new WindowDialog(null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _config.schedule.windows.Add(dialog.Result);
                _dirty = true;
                UpdateDirtyUi();
                RefreshWindowList();
            }
        }

        private void OnEditWindowClick(object sender, EventArgs e)
        {
            if (_windowList.SelectedItems.Count == 0)
            {
                return;
            }
            WindowConfig window = _windowList.SelectedItems[0].Tag as WindowConfig;
            if (window == null)
            {
                return;
            }
            using (WindowDialog dialog = new WindowDialog(window))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                window.days = dialog.Result.days;
                window.start = dialog.Result.start;
                window.end = dialog.Result.end;
                _dirty = true;
                UpdateDirtyUi();
                RefreshWindowList();
            }
        }

        private void OnDeleteWindowClick(object sender, EventArgs e)
        {
            if (_windowList.SelectedItems.Count == 0)
            {
                return;
            }
            WindowConfig window = _windowList.SelectedItems[0].Tag as WindowConfig;
            if (window == null)
            {
                return;
            }
            if (MessageBox.Show("确定删除这个时间窗口吗？", "删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            _config.schedule.windows.Remove(window);
            _dirty = true;
            UpdateDirtyUi();
            RefreshWindowList();
        }

        // ------------------------------------------------------------------
        // 行为选项
        // ------------------------------------------------------------------

        private void OnBehaviorChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }
            _config.behavior.preempt = _chkPreempt.Checked;
            _config.behavior.preemptUnknown = _chkPreemptUnknown.Checked;
            _config.behavior.respectManualMode = _chkManual.Checked;
            _config.behavior.verifyInternet = _chkVerify.Checked;
            _config.behavior.preemptDelayChecks = (int)_numDelay.Value;
            _config.behavior.manualGraceMinutes = (int)_numGrace.Value;
            _config.behavior.connectTimeoutSec = (int)_numTimeout.Value;
            _config.behavior.failedRetryMinutes = (int)_numCooldown.Value;
            _config.behavior.maxAttemptsPerRun = (int)_numAttempts.Value;
            _config.behavior.taskIntervalMinutes = (int)_numInterval.Value;
            _config.behavior.postConnectDelaySec = (int)_numPostDelay.Value;
            _config.behavior.logKeepDays = (int)_numLogDays.Value;
            _config.behavior.verifyUrl = _txtVerifyUrl.Text.Trim();
            _config.behavior.verifyExpect = _txtVerifyExpect.Text.Trim();
            _config.behavior.onConnectCommand = _txtOnConnect.Text;
            MarkDirty();
        }
    }
}
