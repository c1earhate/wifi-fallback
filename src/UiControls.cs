using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WifiFallback
{
    public class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Padding = new Padding(20, 16, 20, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(227, 232, 238)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    public class SignalBars : Control
    {
        private int _signal;

        public int Signal
        {
            get { return _signal; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (clamped != _signal)
                {
                    _signal = clamped;
                    Invalidate();
                }
            }
        }

        public SignalBars()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Size = new Size(50, 34);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int bars = 5;
            int gap = 4;
            int barWidth = 6;
            int totalWidth = bars * barWidth + (bars - 1) * gap;
            int startX = (Width - totalWidth) / 2;
            int bottom = Height - 2;

            for (int index = 0; index < bars; index++)
            {
                int height = (int)Math.Round((Height - 6) * ((index + 1) / (double)bars));
                int x = startX + index * (barWidth + gap);
                int y = bottom - height;
                bool active = _signal >= (index + 1) * 20;
                using (SolidBrush brush = new SolidBrush(active
                    ? Color.FromArgb(0, 103, 192)
                    : Color.FromArgb(214, 221, 229)))
                {
                    e.Graphics.FillRectangle(brush, x, y, barWidth, height);
                }
            }
        }
    }

    public class StatusPill : Control
    {
        private Color _fillColor = Color.FromArgb(232, 241, 251);
        private Color _textColor = Color.FromArgb(0, 103, 192);

        public Color FillColor
        {
            get { return _fillColor; }
            set { _fillColor = value; Invalidate(); }
        }

        public Color TextColor
        {
            get { return _textColor; }
            set { _textColor = value; Invalidate(); }
        }

        public StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Size = new Size(96, 30);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            BackColor = Color.White;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Height;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rectangle.X, rectangle.Y, radius, radius, 180, 90);
                path.AddArc(rectangle.Right - radius, rectangle.Y, radius, radius, 270, 90);
                path.AddArc(rectangle.Right - radius, rectangle.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rectangle.X, rectangle.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();
                using (SolidBrush brush = new SolidBrush(_fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, _textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
