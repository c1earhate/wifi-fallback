using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace WifiFallback
{
    public class TimedCheckDialog : Form
    {
        private DateTimePicker _timePicker;
        private CheckBox[] _dayChecks;
        private ComboBox _actionBox;
        private CheckBox _enabledBox;
        private TimedCheckConfig _result;

        public TimedCheckConfig Result
        {
            get { return _result; }
        }

        public TimedCheckDialog(TimedCheckConfig existing)
        {
            _result = new TimedCheckConfig();
            if (existing != null)
            {
                _result.time = existing.time;
                _result.action = existing.action;
                _result.enabled = existing.enabled;
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
            }
            if (_result.days.Count == 0)
            {
                _result.days = new List<object>(new object[] { 1, 2, 3, 4, 5, 6, 7 });
            }

            Text = existing == null ? "添加定时检测" : "编辑定时检测";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 300);
            BackColor = Color.White;

            Label timeLabel = new Label();
            timeLabel.Text = "执行时间";
            timeLabel.SetBounds(22, 18, 100, 20);
            Controls.Add(timeLabel);

            _timePicker = new DateTimePicker();
            _timePicker.SetBounds(22, 40, 110, 26);
            _timePicker.Format = DateTimePickerFormat.Custom;
            _timePicker.CustomFormat = "HH:mm";
            _timePicker.ShowUpDown = true;
            _timePicker.Value = DateTime.Today.Add(ParseTimeOfDay(_result.time));
            Controls.Add(_timePicker);

            _enabledBox = new CheckBox();
            _enabledBox.Text = "启用这一条";
            _enabledBox.SetBounds(160, 42, 150, 24);
            _enabledBox.Checked = _result.enabled;
            Controls.Add(_enabledBox);

            Label daysLabel = new Label();
            daysLabel.Text = "生效的星期";
            daysLabel.SetBounds(22, 82, 160, 20);
            Controls.Add(daysLabel);

            FlowLayoutPanel dayPanel = new FlowLayoutPanel();
            dayPanel.SetBounds(18, 104, 424, 66);
            dayPanel.WrapContents = true;
            Controls.Add(dayPanel);

            string[] names = new string[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            _dayChecks = new CheckBox[7];
            for (int index = 0; index < 7; index++)
            {
                CheckBox checkBox = new CheckBox();
                checkBox.Text = names[index];
                checkBox.Width = 54;
                checkBox.Height = 24;
                checkBox.AutoSize = false;
                checkBox.TextAlign = ContentAlignment.MiddleLeft;
                checkBox.Margin = new Padding(0, 0, 3, 0);
                checkBox.Checked = _result.days.Contains(index + 1);
                dayPanel.Controls.Add(checkBox);
                _dayChecks[index] = checkBox;
            }

            Label actionLabel = new Label();
            actionLabel.Text = "执行动作";
            actionLabel.SetBounds(22, 182, 100, 20);
            Controls.Add(actionLabel);

            _actionBox = new ComboBox();
            _actionBox.SetBounds(22, 204, 230, 28);
            _actionBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _actionBox.Items.Add("检测并连接（强制一次）");
            _actionBox.Items.Add("仅检测（不切换）");
            _actionBox.SelectedIndex = string.Equals(_result.action, "check", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Controls.Add(_actionBox);

            Button okButton = new Button();
            okButton.Text = "确定";
            okButton.SetBounds(266, 248, 82, 32);
            okButton.FlatStyle = FlatStyle.Flat;
            okButton.FlatAppearance.BorderSize = 0;
            okButton.BackColor = Color.FromArgb(0, 103, 192);
            okButton.ForeColor = Color.White;
            okButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            okButton.Click += OnOk;
            Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.SetBounds(356, 248, 82, 32);
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
            _result.time = _timePicker.Value.ToString("HH:mm");
            _result.days = new List<object>();
            foreach (int day in days)
            {
                _result.days.Add(day);
            }
            _result.action = _actionBox.SelectedIndex == 1 ? "check" : "connect";
            _result.enabled = _enabledBox.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static TimeSpan ParseTimeOfDay(string text)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(text, new string[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.TimeOfDay;
            }
            return new TimeSpan(23, 0, 0);
        }
    }
}
