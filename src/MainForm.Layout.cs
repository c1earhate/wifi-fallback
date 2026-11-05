using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WifiFallback
{
    public partial class MainForm
    {
        private static readonly Color Accent = Color.FromArgb(0, 103, 192);
        private static readonly Color AccentDark = Color.FromArgb(0, 86, 160);
        private static readonly Color PageBg = Color.FromArgb(245, 247, 250);
        private static readonly Color TextPrimary = Color.FromArgb(32, 36, 40);
        private static readonly Color TextSecondary = Color.FromArgb(105, 113, 123);
        private static readonly Color Success = Color.FromArgb(18, 122, 69);
        private static readonly Color Warning = Color.FromArgb(176, 104, 0);
        private static readonly Color Danger = Color.FromArgb(184, 40, 40);
        private static readonly Color BorderColor = Color.FromArgb(227, 232, 238);

        private void BuildUi()
        {
            SuspendLayout();

            _toolTip = new ToolTip();
            _toolTip.AutoPopDelay = 12000;
            _toolTip.InitialDelay = 400;
            _toolTip.ReshowDelay = 100;

            Panel header = BuildHeader();
            Panel bottom = BuildBottomBar();
            Panel main = new Panel();
            main.Dock = DockStyle.Fill;
            main.BackColor = PageBg;

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = PageBg;
            content.Padding = new Padding(20, 16, 20, 16);
            main.Controls.Add(content);

            Panel sidebar = BuildSidebar();
            main.Controls.Add(sidebar);

            _pages = new Panel[5];
            _pages[0] = BuildOverviewPage();
            _pages[1] = BuildNetworksPage();
            _pages[2] = BuildSchedulePage();
            _pages[3] = BuildTimedPage();
            _pages[4] = BuildLogPage();
            for (int index = 0; index < _pages.Length; index++)
            {
                _pages[index].Dock = DockStyle.Fill;
                _pages[index].Visible = index == 0;
                content.Controls.Add(_pages[index]);
            }

            Controls.Add(main);
            Controls.Add(header);
            Controls.Add(bottom);

            BuildTrayIcon();
            SwitchPage(0);
            ResumeLayout(true);
        }

        private void BuildTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Font = new Font("Microsoft YaHei UI", 9F);

            _trayStatusItem = new ToolStripMenuItem("当前：读取中");
            _trayStatusItem.Enabled = false;
            _trayMenu.Items.Add(_trayStatusItem);
            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem openItem = new ToolStripMenuItem("打开主界面");
            openItem.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            openItem.Click += delegate { RestoreFromTray(); };
            _trayMenu.Items.Add(openItem);

            ToolStripMenuItem checkItem = new ToolStripMenuItem("立即检查并连接");
            checkItem.Click += delegate
            {
                RestoreFromTray();
                OnCheckNowClick(this, EventArgs.Empty);
            };
            _trayMenu.Items.Add(checkItem);

            ToolStripMenuItem pauseItem = new ToolStripMenuItem("暂停 1 小时");
            pauseItem.Click += delegate { OnPauseClick(this, EventArgs.Empty); };
            _trayMenu.Items.Add(pauseItem);

            ToolStripMenuItem resumeItem = new ToolStripMenuItem("恢复自动切换");
            resumeItem.Click += delegate { OnResumeClick(this, EventArgs.Empty); };
            _trayMenu.Items.Add(resumeItem);

            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayTimedItem = new ToolStripMenuItem("启用定时检测");
            _trayTimedItem.Click += delegate { ToggleTimedFromTray(); };
            _trayMenu.Items.Add(_trayTimedItem);

            _trayCloseItem = new ToolStripMenuItem("关闭窗口时最小化到托盘");
            _trayCloseItem.Click += delegate { ToggleCloseToTrayFromTray(); };
            _trayMenu.Items.Add(_trayCloseItem);

            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate
            {
                _allowExit = true;
                Close();
            };
            _trayMenu.Items.Add(exitItem);

            _trayMenu.Opening += delegate { UpdateTrayMenu(); };

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = this.Icon != null ? this.Icon : SystemIcons.Application;
            _trayIcon.Text = "WiFi 优先级兜底";
            _trayIcon.Visible = true;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
            _trayIcon.BalloonTipClicked += delegate { RestoreFromTray(); };
        }
        private Panel BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 88;
            header.BackColor = Color.White;
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };

            PictureBox iconBox = new PictureBox();
            iconBox.SetBounds(24, 20, 44, 44);
            iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            iconBox.BackColor = Color.White;
            try
            {
                Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null)
                {
                    iconBox.Image = appIcon.ToBitmap();
                }
            }
            catch
            {
            }
            header.Controls.Add(iconBox);

            Label title = new Label();
            title.Text = "WiFi 优先级兜底";
            title.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.AutoSize = true;
            title.SetBounds(82, 18, 400, 34);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "按优先级自动连接  ·  定时窗口  ·  断线逐级兜底";
            subtitle.Font = new Font("Microsoft YaHei UI", 9F);
            subtitle.ForeColor = TextSecondary;
            subtitle.AutoSize = true;
            subtitle.SetBounds(84, 52, 500, 20);
            header.Controls.Add(subtitle);

            _statusPill = new StatusPill();
            _statusPill.Text = "读取中";
            _statusPill.Size = new Size(96, 30);
            _statusPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _statusPill.SetBounds(1000, 28, 96, 30);
            header.Controls.Add(_statusPill);

            _refreshButton = MakeButton("刷新", false, 84);
            _refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _refreshButton.SetBounds(904, 26, 84, 34);
            _refreshButton.Click += OnRefreshClick;
            header.Controls.Add(_refreshButton);

            header.Resize += delegate
            {
                _statusPill.Left = header.ClientSize.Width - 24 - _statusPill.Width;
                _refreshButton.Left = _statusPill.Left - 12 - _refreshButton.Width;
            };

            return header;
        }

        private Panel BuildSidebar()
        {
            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 190;
            sidebar.BackColor = Color.White;
            sidebar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
                }
            };

            Label caption = new Label();
            caption.Text = "控制台";
            caption.Font = new Font("Microsoft YaHei UI", 8.5F);
            caption.ForeColor = Color.FromArgb(150, 157, 166);
            caption.AutoSize = true;
            caption.SetBounds(22, 22, 100, 18);
            sidebar.Controls.Add(caption);

            string[] texts = new string[] { "总览", "WiFi 优先级", "时间与行为", "定时检测", "运行日志" };
            _navButtons = new Button[texts.Length];
            for (int index = 0; index < texts.Length; index++)
            {
                Button button = new Button();
                button.Text = texts[index];
                button.SetBounds(13, 48 + index * 46, 164, 40);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Padding = new Padding(16, 0, 0, 0);
                button.Font = new Font("Microsoft YaHei UI", 10F);
                button.Cursor = Cursors.Hand;
                int pageIndex = index;
                button.Click += delegate { SwitchPage(pageIndex); };
                sidebar.Controls.Add(button);
                _navButtons[index] = button;
            }

            _taskStatusLabel = new Label();
            _taskStatusLabel.Text = "计划任务：检查中";
            _taskStatusLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
            _taskStatusLabel.ForeColor = TextSecondary;
            _taskStatusLabel.AutoSize = false;
            _taskStatusLabel.SetBounds(22, 600, 150, 18);
            _taskStatusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            sidebar.Controls.Add(_taskStatusLabel);

            Label version = new Label();
            version.Text = "v1.0  ·  PowerShell 引擎";
            version.Font = new Font("Microsoft YaHei UI", 8F);
            version.ForeColor = Color.FromArgb(165, 171, 179);
            version.AutoSize = false;
            version.SetBounds(22, 624, 160, 18);
            version.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            sidebar.Controls.Add(version);

            sidebar.Resize += delegate
            {
                _taskStatusLabel.Top = sidebar.ClientSize.Height - 62;
                version.Top = sidebar.ClientSize.Height - 38;
            };

            return sidebar;
        }

        private Panel BuildBottomBar()
        {
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 56;
            bottom.BackColor = Color.White;
            bottom.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
                }
            };

            _statusLabel = new Label();
            _statusLabel.Text = "就绪";
            _statusLabel.Font = new Font("Microsoft YaHei UI", 9F);
            _statusLabel.ForeColor = TextSecondary;
            _statusLabel.AutoSize = false;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.SetBounds(24, 18, 660, 22);
            bottom.Controls.Add(_statusLabel);

            _dirtyLabel = new Label();
            _dirtyLabel.Text = "有未保存的更改";
            _dirtyLabel.Font = new Font("Microsoft YaHei UI", 9F);
            _dirtyLabel.ForeColor = Warning;
            _dirtyLabel.TextAlign = ContentAlignment.MiddleRight;
            _dirtyLabel.AutoSize = false;
            _dirtyLabel.Visible = false;
            _dirtyLabel.SetBounds(700, 18, 150, 22);
            _dirtyLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bottom.Controls.Add(_dirtyLabel);

            _saveButton = MakeButton("保存配置", true, 110);
            _saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _saveButton.SetBounds(986, 11, 110, 34);
            _saveButton.Click += delegate { SaveAndRefresh(); };
            bottom.Controls.Add(_saveButton);

            _reloadButton = MakeButton("重新加载", false, 96);
            _reloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _reloadButton.SetBounds(882, 11, 96, 34);
            _reloadButton.Click += OnReloadClick;
            bottom.Controls.Add(_reloadButton);

            bottom.Resize += delegate
            {
                _saveButton.Left = bottom.ClientSize.Width - 24 - _saveButton.Width;
                _reloadButton.Left = _saveButton.Left - 8 - _reloadButton.Width;
                _dirtyLabel.Left = _reloadButton.Left - 10 - _dirtyLabel.Width;
                _statusLabel.Width = Math.Max(200, _dirtyLabel.Left - 40);
            };

            return bottom;
        }

        private Panel BuildOverviewPage()
        {
            Panel page = new Panel();
            page.BackColor = PageBg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.BackColor = PageBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 162F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(root);

            root.Controls.Add(BuildCurrentCard(), 0, 0);
            root.Controls.Add(BuildDecisionCard(), 0, 1);
            root.Controls.Add(BuildQuickActions(), 0, 2);
            root.Controls.Add(BuildRecentLogCard(), 0, 3);

            return page;
        }

        private Control BuildCurrentCard()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 12);

            Panel right = new Panel();
            right.Dock = DockStyle.Right;
            right.Width = 250;
            right.BackColor = Color.White;

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Color.White;
            card.Controls.Add(left);
            card.Controls.Add(right);

            Label caption = new Label();
            caption.Text = "当前连接";
            caption.Font = new Font("Microsoft YaHei UI", 9F);
            caption.ForeColor = TextSecondary;
            caption.AutoSize = true;
            caption.SetBounds(0, 0, 100, 18);
            left.Controls.Add(caption);

            _currentSsid = new Label();
            _currentSsid.Text = "—";
            _currentSsid.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold);
            _currentSsid.ForeColor = TextPrimary;
            _currentSsid.AutoSize = false;
            _currentSsid.AutoEllipsis = true;
            _currentSsid.SetBounds(0, 22, 560, 42);
            left.Controls.Add(_currentSsid);

            _currentMeta = new Label();
            _currentMeta.Text = "正在读取...";
            _currentMeta.Font = new Font("Microsoft YaHei UI", 9F);
            _currentMeta.ForeColor = TextSecondary;
            _currentMeta.AutoSize = false;
            _currentMeta.AutoEllipsis = true;
            _currentMeta.SetBounds(2, 72, 560, 22);
            left.Controls.Add(_currentMeta);

            left.Resize += delegate
            {
                _currentSsid.Width = Math.Max(100, left.ClientSize.Width - 10);
                _currentMeta.Width = Math.Max(100, left.ClientSize.Width - 10);
            };

            Label signalCaption = new Label();
            signalCaption.Text = "信号强度";
            signalCaption.Font = new Font("Microsoft YaHei UI", 9F);
            signalCaption.ForeColor = TextSecondary;
            signalCaption.AutoSize = true;
            signalCaption.SetBounds(0, 2, 100, 18);
            right.Controls.Add(signalCaption);

            _signalBars = new SignalBars();
            _signalBars.SetBounds(0, 26, 50, 34);
            right.Controls.Add(_signalBars);

            _signalPercent = new Label();
            _signalPercent.Text = "--";
            _signalPercent.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            _signalPercent.ForeColor = TextPrimary;
            _signalPercent.AutoSize = true;
            _signalPercent.SetBounds(58, 24, 120, 32);
            right.Controls.Add(_signalPercent);

            _currentMode = new Label();
            _currentMode.Text = "";
            _currentMode.Font = new Font("Microsoft YaHei UI", 9F);
            _currentMode.ForeColor = TextSecondary;
            _currentMode.AutoSize = true;
            _currentMode.SetBounds(0, 66, 180, 20);
            right.Controls.Add(_currentMode);

            return card;
        }

        private Control BuildDecisionCard()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 12);

            Label caption = new Label();
            caption.Text = "当前决策";
            caption.Font = new Font("Microsoft YaHei UI", 9F);
            caption.ForeColor = TextSecondary;
            caption.AutoSize = true;
            caption.SetBounds(20, 14, 100, 18);
            card.Controls.Add(caption);

            _decisionTitle = new Label();
            _decisionTitle.Text = "等待状态";
            _decisionTitle.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            _decisionTitle.ForeColor = TextPrimary;
            _decisionTitle.AutoSize = true;
            _decisionTitle.SetBounds(20, 34, 500, 28);
            card.Controls.Add(_decisionTitle);

            _decisionMessage = new Label();
            _decisionMessage.Text = "正在读取 WiFi 状态...";
            _decisionMessage.Font = new Font("Microsoft YaHei UI", 9F);
            _decisionMessage.ForeColor = TextSecondary;
            _decisionMessage.AutoSize = false;
            _decisionMessage.AutoEllipsis = true;
            _decisionMessage.SetBounds(20, 64, 1030, 34);
            card.Controls.Add(_decisionMessage);

            _decisionSchedule = new Label();
            _decisionSchedule.Text = "";
            _decisionSchedule.Font = new Font("Microsoft YaHei UI", 9F);
            _decisionSchedule.ForeColor = Color.FromArgb(80, 129, 175);
            _decisionSchedule.AutoSize = false;
            _decisionSchedule.AutoEllipsis = true;
            _decisionSchedule.SetBounds(20, 100, 1030, 20);
            card.Controls.Add(_decisionSchedule);

            card.Resize += delegate
            {
                _decisionMessage.Width = Math.Max(200, card.ClientSize.Width - 40);
                _decisionSchedule.Width = Math.Max(200, card.ClientSize.Width - 40);
            };

            return card;
        }

        private Control BuildQuickActions()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0);
            panel.BackColor = PageBg;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.BackColor = PageBg;
            flow.Padding = new Padding(0, 9, 0, 0);
            panel.Controls.Add(flow);

            _checkButton = MakeButton("立即检查并连接", true, 150);
            _checkButton.Click += OnCheckNowClick;
            flow.Controls.Add(_checkButton);

            _pauseButton = MakeButton("暂停 1 小时", false, 110);
            _pauseButton.Click += OnPauseClick;
            flow.Controls.Add(_pauseButton);

            _resumeButton = MakeButton("恢复", false, 80);
            _resumeButton.Click += OnResumeClick;
            flow.Controls.Add(_resumeButton);

            _taskButton = MakeButton("安装计划任务", false, 130);
            _taskButton.Click += OnTaskButtonClick;
            flow.Controls.Add(_taskButton);

            Button viewLog = MakeButton("查看日志", false, 100);
            viewLog.Click += OnViewLogClick;
            flow.Controls.Add(viewLog);

            return panel;
        }

        private Control BuildRecentLogCard()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.Controls.Add(layout);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Color.White;
            layout.Controls.Add(header, 0, 0);

            Label caption = new Label();
            caption.Text = "最近日志";
            caption.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            caption.ForeColor = TextPrimary;
            caption.AutoSize = true;
            caption.SetBounds(0, 2, 100, 20);
            header.Controls.Add(caption);

            Button openButton = MakeLinkButton("打开日志目录", 110);
            openButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openButton.SetBounds(910, 0, 110, 24);
            openButton.Click += OnOpenLogFolderClick;
            header.Controls.Add(openButton);
            header.Resize += delegate
            {
                openButton.Left = header.ClientSize.Width - openButton.Width;
            };

            _recentLog = new TextBox();
            _recentLog.Dock = DockStyle.Fill;
            _recentLog.Multiline = true;
            _recentLog.ReadOnly = true;
            _recentLog.BorderStyle = BorderStyle.None;
            _recentLog.BackColor = Color.White;
            _recentLog.ForeColor = Color.FromArgb(64, 72, 82);
            _recentLog.Font = new Font("Consolas", 8.5F);
            _recentLog.ScrollBars = ScrollBars.Vertical;
            _recentLog.WordWrap = false;
            _recentLog.TabStop = false;
            layout.Controls.Add(_recentLog, 0, 1);

            return card;
        }

        private Panel BuildNetworksPage()
        {
            Panel page = new Panel();
            page.BackColor = PageBg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.BackColor = PageBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            page.Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = PageBg;
            Label title = new Label();
            title.Text = "WiFi 优先级";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.AutoSize = true;
            title.SetBounds(0, 0, 200, 26);
            header.Controls.Add(title);
            Label subtitle = new Label();
            subtitle.Text = "数字越小越优先。勾选 = 参与自动连接；双击可编辑。";
            subtitle.Font = new Font("Microsoft YaHei UI", 9F);
            subtitle.ForeColor = TextSecondary;
            subtitle.AutoSize = true;
            subtitle.SetBounds(0, 28, 600, 20);
            header.Controls.Add(subtitle);
            root.Controls.Add(header, 0, 0);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.WrapContents = false;
            toolbar.BackColor = PageBg;

            _addNetworkButton = MakeButton("添加 WiFi", true, 104);
            _addNetworkButton.Click += OnAddNetworkClick;
            toolbar.Controls.Add(_addNetworkButton);

            _editNetworkButton = MakeButton("编辑", false, 70);
            _editNetworkButton.Click += OnEditNetworkClick;
            toolbar.Controls.Add(_editNetworkButton);

            _deleteNetworkButton = MakeButton("删除", false, 70);
            _deleteNetworkButton.Click += OnDeleteNetworkClick;
            toolbar.Controls.Add(_deleteNetworkButton);

            _upNetworkButton = MakeButton("上移", false, 70);
            _upNetworkButton.Click += OnMoveNetworkUp;
            toolbar.Controls.Add(_upNetworkButton);

            _downNetworkButton = MakeButton("下移", false, 70);
            _downNetworkButton.Click += OnMoveNetworkDown;
            toolbar.Controls.Add(_downNetworkButton);

            Button refreshVisible = MakeButton("刷新可见网络", false, 120);
            refreshVisible.Click += delegate { BeginRefresh(); };
            toolbar.Controls.Add(refreshVisible);
            root.Controls.Add(toolbar, 0, 1);

            _networkList = new ListView();
            _networkList.Dock = DockStyle.Fill;
            _networkList.View = View.Details;
            _networkList.FullRowSelect = true;
            _networkList.CheckBoxes = true;
            _networkList.GridLines = false;
            _networkList.HideSelection = false;
            _networkList.MultiSelect = false;
            _networkList.BorderStyle = BorderStyle.FixedSingle;
            _networkList.BackColor = Color.White;
            _networkList.Font = new Font("Microsoft YaHei UI", 9F);
            _networkList.Columns.Add("优先级", 64);
            _networkList.Columns.Add("SSID", 260);
            _networkList.Columns.Add("配置文件", 210);
            _networkList.Columns.Add("最低信号", 90);
            _networkList.Columns.Add("状态", 200);
            _networkList.ItemCheck += OnNetworkItemCheck;
            _networkList.SelectedIndexChanged += OnNetworkSelectionChanged;
            _networkList.DoubleClick += OnNetworkDoubleClick;
            _networkList.KeyDown += OnNetworkKeyDown;
            _networkList.Resize += delegate
            {
                if (_networkList.Columns.Count >= 5)
                {
                    int fixedWidth = 64 + 260 + 210 + 90;
                    _networkList.Columns[4].Width = Math.Max(150, _networkList.ClientSize.Width - fixedWidth - 4);
                }
            };
            root.Controls.Add(_networkList, 0, 2);

            Label footer = new Label();
            footer.Text = "提示：Windows 只能同时连接一个 WiFi；本工具会在时间窗口内按顺序尝试，失败自动降级。";
            footer.Font = new Font("Microsoft YaHei UI", 8.5F);
            footer.ForeColor = TextSecondary;
            footer.AutoSize = false;
            footer.SetBounds(0, 4, 900, 20);
            root.Controls.Add(footer, 0, 3);

            return page;
        }

        private Panel BuildSchedulePage()
        {
            Panel page = new Panel();
            page.BackColor = PageBg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.BackColor = PageBg;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(root);

            Control scheduleCard = BuildScheduleCard();
            scheduleCard.Margin = new Padding(0, 0, 8, 0);
            root.Controls.Add(scheduleCard, 0, 0);

            Control behaviorCard = BuildBehaviorCard();
            behaviorCard.Margin = new Padding(8, 0, 0, 0);
            root.Controls.Add(behaviorCard, 1, 0);

            return page;
        }

        private Control BuildScheduleCard()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            card.Controls.Add(layout);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = "触发时间";
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.AutoSize = true;
            title.SetBounds(0, 0, 160, 24);
            header.Controls.Add(title);
            Label subtitle = new Label();
            subtitle.Text = "只在窗口内自动连接/切换";
            subtitle.Font = new Font("Microsoft YaHei UI", 8.5F);
            subtitle.ForeColor = TextSecondary;
            subtitle.AutoSize = true;
            subtitle.SetBounds(0, 24, 240, 18);
            header.Controls.Add(subtitle);
            layout.Controls.Add(header, 0, 0);

            Panel enablePanel = new Panel();
            enablePanel.Dock = DockStyle.Fill;
            _scheduleEnabled = new CheckBox();
            _scheduleEnabled.Text = "启用时间窗口";
            _scheduleEnabled.Font = new Font("Microsoft YaHei UI", 9F);
            _scheduleEnabled.AutoSize = true;
            _scheduleEnabled.SetBounds(0, 4, 120, 24);
            _scheduleEnabled.CheckedChanged += OnScheduleEnabledChanged;
            enablePanel.Controls.Add(_scheduleEnabled);

            _scheduleWarning = new Label();
            _scheduleWarning.Text = "关闭后全天生效，容易和手动操作冲突";
            _scheduleWarning.Font = new Font("Microsoft YaHei UI", 8.5F);
            _scheduleWarning.ForeColor = Danger;
            _scheduleWarning.AutoSize = true;
            _scheduleWarning.SetBounds(130, 7, 260, 18);
            _scheduleWarning.Visible = false;
            enablePanel.Controls.Add(_scheduleWarning);
            layout.Controls.Add(enablePanel, 0, 1);

            _windowList = new ListView();
            _windowList.Dock = DockStyle.Fill;
            _windowList.View = View.Details;
            _windowList.FullRowSelect = true;
            _windowList.GridLines = false;
            _windowList.HideSelection = false;
            _windowList.MultiSelect = false;
            _windowList.BorderStyle = BorderStyle.FixedSingle;
            _windowList.BackColor = Color.White;
            _windowList.Font = new Font("Microsoft YaHei UI", 9F);
            _windowList.Columns.Add("星期", 200);
            _windowList.Columns.Add("开始", 70);
            _windowList.Columns.Add("结束", 70);
            _windowList.SelectedIndexChanged += OnWindowSelectionChanged;
            _windowList.DoubleClick += OnEditWindowClick;
            layout.Controls.Add(_windowList, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            _addWindowButton = MakeButton("添加", true, 78);
            _addWindowButton.Click += OnAddWindowClick;
            buttons.Controls.Add(_addWindowButton);
            _editWindowButton = MakeButton("编辑", false, 70);
            _editWindowButton.Click += OnEditWindowClick;
            buttons.Controls.Add(_editWindowButton);
            _deleteWindowButton = MakeButton("删除", false, 70);
            _deleteWindowButton.Click += OnDeleteWindowClick;
            buttons.Controls.Add(_deleteWindowButton);
            layout.Controls.Add(buttons, 0, 3);

            Label hint = new Label();
            hint.Text = "跨午夜：23:00–07:00；开始日决定星期归属。";
            hint.Font = new Font("Microsoft YaHei UI", 8.5F);
            hint.ForeColor = TextSecondary;
            hint.AutoSize = true;
            hint.SetBounds(0, 6, 380, 18);
            layout.Controls.Add(hint, 0, 4);

            return card;
        }

        private Control BuildBehaviorCard()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 48;
            Label title = new Label();
            title.Text = "行为选项";
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.AutoSize = true;
            title.SetBounds(0, 0, 160, 24);
            header.Controls.Add(title);
            Label subtitle = new Label();
            subtitle.Text = "控制抢占、防抖和失败重试";
            subtitle.Font = new Font("Microsoft YaHei UI", 8.5F);
            subtitle.ForeColor = TextSecondary;
            subtitle.AutoSize = true;
            subtitle.SetBounds(0, 24, 260, 18);
            header.Controls.Add(subtitle);

            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.BackColor = Color.White;
            card.Controls.Add(scroll);
            card.Controls.Add(header);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.AutoSize = true;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            grid.Padding = new Padding(0, 2, 6, 6);
            scroll.Controls.Add(grid);

            int row = 0;
            _chkPreempt = MakeCheckBox("主动切换到更高优先级");
            _chkPreempt.CheckedChanged += OnBehaviorChanged;
            grid.Controls.Add(_chkPreempt, 0, row);
            grid.SetColumnSpan(_chkPreempt, 2);
            _toolTip.SetToolTip(_chkPreempt, "已连接低优先级网络时，发现更高优先级可用就切换。关闭后只在断线时兜底。");
            row++;

            _chkPreemptUnknown = MakeCheckBox("接管非列表网络");
            _chkPreemptUnknown.CheckedChanged += OnBehaviorChanged;
            grid.Controls.Add(_chkPreemptUnknown, 0, row);
            grid.SetColumnSpan(_chkPreemptUnknown, 2);
            _toolTip.SetToolTip(_chkPreemptUnknown, "当前连接的不是列表内 WiFi 时，也按优先级接管。关闭后只会从断线状态开始兜底。");
            row++;

            _chkManual = MakeCheckBox("尊重系统报告的手动连接");
            _chkManual.CheckedChanged += OnBehaviorChanged;
            grid.Controls.Add(_chkManual, 0, row);
            grid.SetColumnSpan(_chkManual, 2);
            _toolTip.SetToolTip(_chkManual, "系统把连接标记为 Manual 时，暂不自动切换。仍建议用“暂停”来确保手动操作不被干扰。");
            row++;

            _numDelay = AddNumericRow(grid, "防抖确认次数", 1, 10, row++);
            _toolTip.SetToolTip(_numDelay, "连续多少次检查都发现更高优先级才切换。1 分钟检查一次时，2 次约等于 2 分钟。");
            _numGrace = AddNumericRow(grid, "手动保护（分钟）", 0, 240, row++);
            _numTimeout = AddNumericRow(grid, "连接超时（秒）", 5, 60, row++);
            _numCooldown = AddNumericRow(grid, "失败冷却（分钟）", 1, 60, row++);
            _numAttempts = AddNumericRow(grid, "单次最多尝试", 1, 10, row++);
            _numInterval = AddNumericRow(grid, "检查间隔（分钟）", 1, 15, row++);
            _toolTip.SetToolTip(_numInterval, "计划任务的检查频率。修改后需要重新安装计划任务。");
            _numPostDelay = AddNumericRow(grid, "连接后等待（秒）", 0, 60, row++);
            _numLogDays = AddNumericRow(grid, "日志保留（天）", 1, 365, row++);

            _chkVerify = MakeCheckBox("连接后验证外网（失败则继续降级）");
            _chkVerify.CheckedChanged += OnBehaviorChanged;
            grid.Controls.Add(_chkVerify, 0, row);
            grid.SetColumnSpan(_chkVerify, 2);
            row++;

            Panel verifyUrlPanel = new Panel();
            verifyUrlPanel.Height = 50;
            verifyUrlPanel.Dock = DockStyle.Fill;
            Label urlLabel = new Label();
            urlLabel.Text = "检测 URL";
            urlLabel.AutoSize = true;
            urlLabel.SetBounds(0, 2, 100, 18);
            verifyUrlPanel.Controls.Add(urlLabel);

            _txtVerifyUrl = new TextBox();
            _txtVerifyUrl.SetBounds(0, 22, 390, 24);
            _txtVerifyUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtVerifyUrl.TextChanged += OnBehaviorChanged;
            verifyUrlPanel.Controls.Add(_txtVerifyUrl);
            _toolTip.SetToolTip(_txtVerifyUrl, "默认使用 Microsoft 的联网检测地址。校园网 Portal 会返回登录页而不是期望内容。");

            grid.Controls.Add(verifyUrlPanel, 0, row);
            grid.SetColumnSpan(verifyUrlPanel, 2);
            row++;

            Panel verifyExpectPanel = new Panel();
            verifyExpectPanel.Height = 48;
            verifyExpectPanel.Dock = DockStyle.Fill;
            Label expectLabel = new Label();
            expectLabel.Text = "期望内容";
            expectLabel.AutoSize = true;
            expectLabel.SetBounds(0, 2, 100, 18);
            verifyExpectPanel.Controls.Add(expectLabel);

            _txtVerifyExpect = new TextBox();
            _txtVerifyExpect.SetBounds(0, 22, 240, 24);
            _txtVerifyExpect.TextChanged += OnBehaviorChanged;
            verifyExpectPanel.Controls.Add(_txtVerifyExpect);

            Label expectHint = new Label();
            expectHint.Text = "HTTP 200 且内容匹配";
            expectHint.Font = new Font("Microsoft YaHei UI", 8F);
            expectHint.ForeColor = TextSecondary;
            expectHint.AutoSize = true;
            expectHint.SetBounds(250, 25, 160, 18);
            verifyExpectPanel.Controls.Add(expectHint);
            _toolTip.SetToolTip(_txtVerifyExpect, "默认 Microsoft Connect Test；如果内容不匹配，视为 Portal/未联网。");

            grid.Controls.Add(verifyExpectPanel, 0, row);
            grid.SetColumnSpan(verifyExpectPanel, 2);
            row++;

            Label commandLabel = new Label();
            commandLabel.Text = "连接成功后执行（可选 PowerShell 命令）";
            commandLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
            commandLabel.ForeColor = TextSecondary;
            commandLabel.AutoSize = true;
            commandLabel.Margin = new Padding(0, 8, 0, 2);
            grid.Controls.Add(commandLabel, 0, row);
            grid.SetColumnSpan(commandLabel, 2);
            row++;

            _txtOnConnect = new TextBox();
            _txtOnConnect.Multiline = true;
            _txtOnConnect.Height = 50;
            _txtOnConnect.ScrollBars = ScrollBars.Vertical;
            _txtOnConnect.Dock = DockStyle.Fill;
            _txtOnConnect.TextChanged += OnBehaviorChanged;
            grid.Controls.Add(_txtOnConnect, 0, row);
            grid.SetColumnSpan(_txtOnConnect, 2);
            _toolTip.SetToolTip(_txtOnConnect, "可使用 $env:WIFI_SSID 和 $env:WIFI_PROFILE。用于校园网 Portal 认证。");
            row++;

            grid.RowCount = row;

            return card;
        }

        private NumericUpDown AddNumericRow(TableLayoutPanel grid, string text, int min, int max, int row)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", 9F);
            label.ForeColor = TextPrimary;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 4, 8, 4);
            grid.Controls.Add(label, 0, row);

            NumericUpDown box = new NumericUpDown();
            box.Minimum = min;
            box.Maximum = max;
            box.Width = 90;
            box.Height = 26;
            box.TextAlign = HorizontalAlignment.Right;
            box.Anchor = AnchorStyles.Left;
            box.Margin = new Padding(0, 2, 0, 2);
            box.ValueChanged += OnBehaviorChanged;
            grid.Controls.Add(box, 1, row);

            return box;
        }

        private Panel BuildTimedPage()
        {
            Panel page = new Panel();
            page.BackColor = PageBg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.BackColor = PageBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = PageBg;
            Label title = new Label();
            title.Text = "定时检测";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.AutoSize = true;
            title.SetBounds(0, 0, 160, 26);
            header.Controls.Add(title);
            Label subtitle = new Label();
            subtitle.Text = "在指定时间自动执行一次；只在桌面版运行时生效，关闭窗口后托盘继续。";
            subtitle.Font = new Font("Microsoft YaHei UI", 9F);
            subtitle.ForeColor = TextSecondary;
            subtitle.AutoSize = true;
            subtitle.SetBounds(0, 28, 700, 20);
            header.Controls.Add(subtitle);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.Controls.Add(content, 0, 1);

            CardPanel left = new CardPanel();
            left.Dock = DockStyle.Fill;
            left.Margin = new Padding(0, 0, 8, 0);
            content.Controls.Add(left, 0, 0);

            TableLayoutPanel leftLayout = new TableLayoutPanel();
            leftLayout.Dock = DockStyle.Fill;
            leftLayout.ColumnCount = 1;
            leftLayout.RowCount = 4;
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            left.Controls.Add(leftLayout);

            Panel enablePanel = new Panel();
            enablePanel.Dock = DockStyle.Fill;
            _chkTimedEnabled = new CheckBox();
            _chkTimedEnabled.Text = "启用定时检测";
            _chkTimedEnabled.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _chkTimedEnabled.AutoSize = true;
            _chkTimedEnabled.SetBounds(0, 4, 150, 24);
            _chkTimedEnabled.CheckedChanged += OnTimedEnabledChanged;
            enablePanel.Controls.Add(_chkTimedEnabled);

            Label enableHint = new Label();
            enableHint.Text = "到点后执行一次，不影响引擎的时间窗口";
            enableHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            enableHint.ForeColor = TextSecondary;
            enableHint.AutoSize = true;
            enableHint.SetBounds(160, 7, 330, 18);
            enablePanel.Controls.Add(enableHint);
            leftLayout.Controls.Add(enablePanel, 0, 0);

            _timedList = new ListView();
            _timedList.Dock = DockStyle.Fill;
            _timedList.View = View.Details;
            _timedList.FullRowSelect = true;
            _timedList.GridLines = false;
            _timedList.HideSelection = false;
            _timedList.MultiSelect = false;
            _timedList.BorderStyle = BorderStyle.FixedSingle;
            _timedList.BackColor = Color.White;
            _timedList.Font = new Font("Microsoft YaHei UI", 9F);
            _timedList.Columns.Add("时间", 80);
            _timedList.Columns.Add("星期", 200);
            _timedList.Columns.Add("动作", 130);
            _timedList.SelectedIndexChanged += OnTimedSelectionChanged;
            _timedList.DoubleClick += OnTimedDoubleClick;
            leftLayout.Controls.Add(_timedList, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            _addTimedButton = MakeButton("添加", true, 78);
            _addTimedButton.Click += OnAddTimedClick;
            buttons.Controls.Add(_addTimedButton);
            _editTimedButton = MakeButton("编辑", false, 70);
            _editTimedButton.Click += OnEditTimedClick;
            buttons.Controls.Add(_editTimedButton);
            _deleteTimedButton = MakeButton("删除", false, 70);
            _deleteTimedButton.Click += OnDeleteTimedClick;
            buttons.Controls.Add(_deleteTimedButton);
            leftLayout.Controls.Add(buttons, 0, 2);

            Label hint = new Label();
            hint.Text = "提示：这只是桌面版的附加定时；系统级兜底仍由“安装计划任务”负责。";
            hint.Font = new Font("Microsoft YaHei UI", 8.5F);
            hint.ForeColor = TextSecondary;
            hint.AutoSize = true;
            hint.SetBounds(0, 12, 520, 18);
            leftLayout.Controls.Add(hint, 0, 3);

            CardPanel right = new CardPanel();
            right.Dock = DockStyle.Fill;
            right.Margin = new Padding(8, 0, 0, 0);
            content.Controls.Add(right, 1, 0);

            Label statusTitle = new Label();
            statusTitle.Text = "运行状态";
            statusTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            statusTitle.ForeColor = TextPrimary;
            statusTitle.AutoSize = true;
            statusTitle.SetBounds(20, 16, 160, 24);
            right.Controls.Add(statusTitle);

            _trayStateLabel = new Label();
            _trayStateLabel.Text = "主窗口运行中";
            _trayStateLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _trayStateLabel.ForeColor = Success;
            _trayStateLabel.AutoSize = false;
            _trayStateLabel.SetBounds(20, 48, 380, 20);
            right.Controls.Add(_trayStateLabel);

            _timedNextLabel = new Label();
            _timedNextLabel.Text = "下次执行：—";
            _timedNextLabel.Font = new Font("Microsoft YaHei UI", 9F);
            _timedNextLabel.ForeColor = TextPrimary;
            _timedNextLabel.AutoSize = false;
            _timedNextLabel.AutoEllipsis = true;
            _timedNextLabel.SetBounds(20, 78, 380, 20);
            right.Controls.Add(_timedNextLabel);

            _timedLastLabel = new Label();
            _timedLastLabel.Text = "上次执行：—";
            _timedLastLabel.Font = new Font("Microsoft YaHei UI", 9F);
            _timedLastLabel.ForeColor = TextSecondary;
            _timedLastLabel.AutoSize = false;
            _timedLastLabel.AutoEllipsis = true;
            _timedLastLabel.SetBounds(20, 102, 380, 20);
            right.Controls.Add(_timedLastLabel);

            _chkCloseToTray = new CheckBox();
            _chkCloseToTray.Text = "关闭窗口时最小化到系统托盘";
            _chkCloseToTray.Font = new Font("Microsoft YaHei UI", 9F);
            _chkCloseToTray.AutoSize = true;
            _chkCloseToTray.SetBounds(20, 142, 300, 24);
            _chkCloseToTray.CheckedChanged += OnCloseToTrayChanged;
            right.Controls.Add(_chkCloseToTray);

            Label trayHint = new Label();
            trayHint.Text = "关闭窗口后程序仍在后台，定时检测和托盘菜单继续可用；\r\n真正退出请使用托盘菜单的“退出”。";
            trayHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            trayHint.ForeColor = TextSecondary;
            trayHint.AutoSize = false;
            trayHint.SetBounds(20, 172, 370, 44);
            right.Controls.Add(trayHint);

            Label systemHint = new Label();
            systemHint.Text = "需要退出桌面版后仍然定时执行？\r\n请使用引擎的 Windows 计划任务（“安装计划任务”）。";
            systemHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            systemHint.ForeColor = Color.FromArgb(120, 128, 138);
            systemHint.AutoSize = false;
            systemHint.SetBounds(20, 232, 370, 44);
            right.Controls.Add(systemHint);

            Button openTimedLog = MakeButton("查看定时日志", false, 110);
            openTimedLog.SetBounds(20, 292, 110, 32);
            openTimedLog.Click += delegate
            {
                string path = Path.Combine(_engine.LogDirectory, "ui-timed.log");
                if (File.Exists(path))
                {
                    EngineClient.OpenFile(path);
                }
                else
                {
                    OnOpenLogFolderClick(this, EventArgs.Empty);
                }
            };
            right.Controls.Add(openTimedLog);

            right.Resize += delegate
            {
                _trayStateLabel.Width = Math.Max(160, right.ClientSize.Width - 40);
                _timedNextLabel.Width = Math.Max(160, right.ClientSize.Width - 40);
                _timedLastLabel.Width = Math.Max(160, right.ClientSize.Width - 40);
                trayHint.Width = Math.Max(160, right.ClientSize.Width - 40);
                systemHint.Width = Math.Max(160, right.ClientSize.Width - 40);
            };

            return page;
        }
        private Panel BuildLogPage()
        {
            Panel page = new Panel();
            page.BackColor = PageBg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.BackColor = PageBg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = PageBg;
            Label title = new Label();
            title.Text = "运行日志";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.AutoSize = true;
            title.SetBounds(0, 0, 160, 26);
            header.Controls.Add(title);

            _logPathLabel = new Label();
            _logPathLabel.Text = "暂无日志文件";
            _logPathLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
            _logPathLabel.ForeColor = TextSecondary;
            _logPathLabel.AutoSize = false;
            _logPathLabel.AutoEllipsis = true;
            _logPathLabel.SetBounds(0, 28, 620, 18);
            header.Controls.Add(_logPathLabel);

            Button refresh = MakeButton("刷新", false, 70);
            refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refresh.SetBounds(870, 4, 70, 32);
            refresh.Click += delegate { RefreshLog(); };
            header.Controls.Add(refresh);

            Button openFolder = MakeButton("打开日志目录", false, 116);
            openFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openFolder.SetBounds(744, 4, 116, 32);
            openFolder.Click += OnOpenLogFolderClick;
            header.Controls.Add(openFolder);

            Button configFolder = MakeButton("打开配置目录", false, 116);
            configFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            configFolder.SetBounds(618, 4, 116, 32);
            configFolder.Click += OnOpenConfigFolderClick;
            header.Controls.Add(configFolder);

            root.Controls.Add(header, 0, 0);

            _logBox = new TextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.BackColor = Color.White;
            _logBox.ForeColor = Color.FromArgb(52, 60, 70);
            _logBox.Font = new Font("Consolas", 9F);
            _logBox.ScrollBars = ScrollBars.Both;
            _logBox.WordWrap = false;
            root.Controls.Add(_logBox, 0, 1);

            return page;
        }

        private void SwitchPage(int index)
        {
            if (_pages == null || index < 0 || index >= _pages.Length)
            {
                return;
            }
            for (int pageIndex = 0; pageIndex < _pages.Length; pageIndex++)
            {
                _pages[pageIndex].Visible = pageIndex == index;
            }
            for (int buttonIndex = 0; buttonIndex < _navButtons.Length; buttonIndex++)
            {
                Button button = _navButtons[buttonIndex];
                bool selected = buttonIndex == index;
                button.BackColor = selected ? Color.FromArgb(232, 241, 251) : Color.White;
                button.ForeColor = selected ? Accent : TextPrimary;
                button.Font = new Font("Microsoft YaHei UI", 10F, selected ? FontStyle.Bold : FontStyle.Regular);
                button.FlatAppearance.MouseOverBackColor = selected
                    ? Color.FromArgb(232, 241, 251)
                    : Color.FromArgb(244, 246, 249);
            }
            if (index == 3)
            {
                UpdateTimedStatus();
            }
            if (index == 4)
            {
                RefreshLog();
            }
            if (IsHandleCreated)
            {
                ActiveControl = null;
            }
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", size, style);
            label.ForeColor = color;
            label.AutoSize = true;
            return label;
        }

        private static CheckBox MakeCheckBox(string text)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.Font = new Font("Microsoft YaHei UI", 9F);
            checkBox.AutoSize = true;
            checkBox.Margin = new Padding(0, 3, 0, 3);
            checkBox.Anchor = AnchorStyles.Left;
            return checkBox;
        }

        private static Button MakeButton(string text, bool primary, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 34;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(203, 211, 220);
            button.FlatAppearance.MouseOverBackColor = primary ? AccentDark : Color.FromArgb(243, 247, 251);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 74, 139) : Color.FromArgb(232, 238, 245);
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : TextPrimary;
            button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 0, 8, 0);
            return button;
        }

        private static Button MakeLinkButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 24;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 247, 251);
            button.BackColor = Color.White;
            button.ForeColor = Accent;
            button.Font = new Font("Microsoft YaHei UI", 8.5F);
            button.Cursor = Cursors.Hand;
            return button;
        }
    }
}
