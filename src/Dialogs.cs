using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace WifiFallback
{
    public class NetworkDialog : Form
    {
        private ComboBox _ssidBox;
        private TextBox _profileBox;
        private NumericUpDown _minSignalBox;
        private CheckBox _hiddenBox;
        private CheckBox _enabledBox;
        private string _lastSelected = "";
        private NetworkConfig _result;

        public NetworkConfig Result
        {
            get { return _result; }
        }

        public NetworkDialog(NetworkConfig existing, List<VisibleInfo> visible, List<string> profiles)
        {
            _result = new NetworkConfig();
            if (existing != null)
            {
                _result.ssid = existing.ssid;
                _result.profile = existing.profile;
                _result.priority = existing.priority;
                _result.enabled = existing.enabled;
                _result.minSignal = existing.minSignal;
                _result.allowHidden = existing.allowHidden;
            }

            Text = existing == null ? "添加 WiFi" : "编辑 WiFi";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 356);
            BackColor = Color.White;

            Label ssidLabel = new Label();
            ssidLabel.Text = "WiFi 名称（SSID）";
            ssidLabel.SetBounds(22, 18, 300, 20);
            Controls.Add(ssidLabel);

            _ssidBox = new ComboBox();
            _ssidBox.SetBounds(22, 40, 416, 28);
            _ssidBox.DropDownStyle = ComboBoxStyle.DropDown;
            _ssidBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _ssidBox.AutoCompleteSource = AutoCompleteSource.ListItems;
            Controls.Add(_ssidBox);

            Label hint = new Label();
            hint.Text = "扫描到的网络和系统已保存的配置都会出现在下拉列表里。";
            hint.ForeColor = Color.FromArgb(120, 128, 138);
            hint.SetBounds(22, 72, 416, 20);
            Controls.Add(hint);

            Label profileLabel = new Label();
            profileLabel.Text = "配置文件名称（一般与 SSID 相同）";
            profileLabel.SetBounds(22, 100, 400, 20);
            Controls.Add(profileLabel);

            _profileBox = new TextBox();
            _profileBox.SetBounds(22, 122, 416, 26);
            Controls.Add(_profileBox);

            Label signalLabel = new Label();
            signalLabel.Text = "最低信号（%）";
            signalLabel.SetBounds(22, 160, 160, 20);
            Controls.Add(signalLabel);

            _minSignalBox = new NumericUpDown();
            _minSignalBox.SetBounds(22, 182, 100, 26);
            _minSignalBox.Minimum = 0;
            _minSignalBox.Maximum = 100;
            _minSignalBox.Value = Math.Max(0, Math.Min(100, _result.minSignal));
            Controls.Add(_minSignalBox);

            _hiddenBox = new CheckBox();
            _hiddenBox.Text = "隐藏网络（扫描不到也尝试连接）";
            _hiddenBox.SetBounds(22, 220, 300, 24);
            _hiddenBox.Checked = _result.allowHidden;
            Controls.Add(_hiddenBox);

            _enabledBox = new CheckBox();
            _enabledBox.Text = "启用此网络（参与自动连接）";
            _enabledBox.SetBounds(22, 248, 300, 24);
            _enabledBox.Checked = existing == null ? true : _result.enabled;
            Controls.Add(_enabledBox);

            Button okButton = new Button();
            okButton.Text = "确定";
            okButton.SetBounds(266, 306, 82, 32);
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.FlatAppearance.BorderSize = 0;
            okButton.BackColor = Color.FromArgb(0, 103, 192);
            okButton.ForeColor = Color.White;
            okButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            okButton.Click += OnOk;
            Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.SetBounds(356, 306, 82, 32);
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 219);
            cancelButton.BackColor = Color.White;
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (visible != null)
            {
                List<VisibleInfo> sorted = new List<VisibleInfo>(visible);
                sorted.Sort(delegate(VisibleInfo left, VisibleInfo right)
                {
                    return right.signal.CompareTo(left.signal);
                });
                foreach (VisibleInfo item in sorted)
                {
                    if (item != null && !string.IsNullOrEmpty(item.ssid) && seen.Add(item.ssid))
                    {
                        _ssidBox.Items.Add(item.ssid);
                    }
                }
            }
            if (profiles != null)
            {
                foreach (string profile in profiles)
                {
                    if (!string.IsNullOrEmpty(profile) && seen.Add(profile))
                    {
                        _ssidBox.Items.Add(profile);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_result.ssid))
            {
                _ssidBox.Text = _result.ssid;
                _profileBox.Text = _result.profile;
            }
            else
            {
                _profileBox.Text = "";
            }
            _lastSelected = _ssidBox.Text;

            _ssidBox.SelectedIndexChanged += delegate
            {
                string selected = _ssidBox.Text;
                if (!string.IsNullOrEmpty(selected) &&
                    (string.IsNullOrEmpty(_profileBox.Text) || _profileBox.Text == _lastSelected))
                {
                    _profileBox.Text = selected;
                }
                _lastSelected = selected;
            };
            _ssidBox.TextChanged += delegate
            {
                if (string.IsNullOrEmpty(_profileBox.Text) || _profileBox.Text == _lastSelected)
                {
                    _profileBox.Text = _ssidBox.Text.Trim();
                }
            };
        }

        private void OnOk(object sender, EventArgs e)
        {
            string ssid = _ssidBox.Text.Trim();
            if (ssid.Length == 0)
            {
                MessageBox.Show("请填写 WiFi 名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _ssidBox.Focus();
                return;
            }
            string profile = _profileBox.Text.Trim();
            if (profile.Length == 0)
            {
                profile = ssid;
            }
            _result.ssid = ssid;
            _result.profile = profile;
            _result.minSignal = (int)_minSignalBox.Value;
            _result.allowHidden = _hiddenBox.Checked;
            _result.enabled = _enabledBox.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public class WindowDialog : Form
    {
        private CheckBox[] _dayChecks;
        private DateTimePicker _startPicker;
        private DateTimePicker _endPicker;
        private WindowConfig _result;

        public WindowConfig Result
        {
            get { return _result; }
        }

        public WindowDialog(WindowConfig existing)
        {
            _result = new WindowConfig();
            if (existing == null)
            {
                _result.days = new List<object>(new object[] { 1, 2, 3, 4, 5, 6, 7 });
            }
            if (existing != null)
            {
                _result.days = new List<object>();
                if (existing.days != null)
                {
                    foreach (object day in existing.days)
                    {
                        int number = DayUtil.ToNumber(day);
                        if (number >= 1 && number <= 7 && !_result.days.Contains(number))
                        {
                            _result.days.Add(number);
                        }
                    }
                }
                _result.start = existing.start;
                _result.end = existing.end;
            }

            Text = existing == null ? "添加时间窗口" : "编辑时间窗口";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 306);
            BackColor = Color.White;

            Label daysLabel = new Label();
            daysLabel.Text = "生效的星期（可多选；指窗口开始的那一天）";
            daysLabel.SetBounds(22, 16, 390, 20);
            Controls.Add(daysLabel);

            FlowLayoutPanel dayPanel = new FlowLayoutPanel();
            dayPanel.SetBounds(18, 40, 444, 70);
            dayPanel.WrapContents = true;
            Controls.Add(dayPanel);

            string[] names = new string[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            _dayChecks = new CheckBox[7];
            for (int index = 0; index < 7; index++)
            {
                CheckBox checkBox = new CheckBox();
                checkBox.Text = names[index];
                checkBox.Width = 58;
                checkBox.AutoSize = false;
                checkBox.TextAlign = ContentAlignment.MiddleLeft;
                checkBox.Height = 24;
                checkBox.Margin = new Padding(0, 0, 3, 0);
                checkBox.Checked = _result.days.Contains(index + 1);
                dayPanel.Controls.Add(checkBox);
                _dayChecks[index] = checkBox;
            }

            Label startLabel = new Label();
            startLabel.Text = "开始时间";
            startLabel.SetBounds(22, 124, 100, 20);
            Controls.Add(startLabel);

            _startPicker = new DateTimePicker();
            _startPicker.SetBounds(22, 146, 110, 26);
            _startPicker.Format = DateTimePickerFormat.Custom;
            _startPicker.CustomFormat = "HH:mm";
            _startPicker.ShowUpDown = true;
            _startPicker.Value = DateTime.Today.Add(ParseTimeOfDay(_result.start, new TimeSpan(23, 0, 0)));
            Controls.Add(_startPicker);

            Label endLabel = new Label();
            endLabel.Text = "结束时间";
            endLabel.SetBounds(180, 124, 100, 20);
            Controls.Add(endLabel);

            _endPicker = new DateTimePicker();
            _endPicker.SetBounds(180, 146, 110, 26);
            _endPicker.Format = DateTimePickerFormat.Custom;
            _endPicker.CustomFormat = "HH:mm";
            _endPicker.ShowUpDown = true;
            _endPicker.Value = DateTime.Today.Add(ParseTimeOfDay(_result.end, new TimeSpan(7, 0, 0)));
            Controls.Add(_endPicker);

            Label hint = new Label();
            hint.Text = "结束早于开始 = 跨到次日；开始等于结束 = 24 小时。";
            hint.ForeColor = Color.FromArgb(120, 128, 138);
            hint.SetBounds(22, 188, 440, 40);
            Controls.Add(hint);

            Button okButton = new Button();
            okButton.Text = "确定";
            okButton.SetBounds(286, 254, 82, 32);
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.FlatAppearance.BorderSize = 0;
            okButton.BackColor = Color.FromArgb(0, 103, 192);
            okButton.ForeColor = Color.White;
            okButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            okButton.Click += OnOk;
            Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.SetBounds(376, 254, 82, 32);
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 219);
            cancelButton.BackColor = Color.White;
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void OnOk(object sender, EventArgs e)
        {
            List<int> days = new List<int>();
            for (int index = 0; index < _dayChecks.Length; index++)
            {
                if (_dayChecks[index].Checked)
                {
                    days.Add(index + 1);
                }
            }
            if (days.Count == 0)
            {
                MessageBox.Show("请至少选择一个星期。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _result.days = new List<object>();
            foreach (int day in days)
            {
                _result.days.Add(day);
            }
            _result.start = _startPicker.Value.ToString("HH:mm");
            _result.end = _endPicker.Value.ToString("HH:mm");
            DialogResult = DialogResult.OK;
            Close();
        }

        private static TimeSpan ParseTimeOfDay(string text, TimeSpan fallback)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(text, new string[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.TimeOfDay;
            }
            return fallback;
        }
    }
}
