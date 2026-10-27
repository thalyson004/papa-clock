using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PapaClock
{
    internal sealed class TrayPopupService
    {
        private readonly PopupSettings settings;
        private readonly Action<string, string, ToolTipIcon> display;

        public TrayPopupService(PopupSettings settings, Action<string, string, ToolTipIcon> display)
        {
            this.settings = settings;
            this.display = display;
        }

        public void Show(PopupKind kind, string title, string message, ToolTipIcon icon)
        {
            if (settings.IsEnabled(kind)) display(title, message, icon);
        }
    }

    internal static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(31, 46, 40);
        public static readonly Color Muted = Color.FromArgb(101, 115, 106);
        public static readonly Color Paper = Color.FromArgb(246, 247, 242);
        public static readonly Color Green = Color.FromArgb(39, 92, 65);
        public static readonly Color Mint = Color.FromArgb(221, 237, 220);
        public static readonly Color Border = Color.FromArgb(224, 230, 221);
        public static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }
        public static Label Label(string text, float size, Color color, int x, int y, int w, int h, FontStyle style = FontStyle.Regular)
        {
            return new Label { Text = text, Font = Font(size, style), ForeColor = color,
                Location = new Point(x, y), Size = new Size(w, h), AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
        }
        public static ModernButton Button(string text, int x, int y, int width, bool primary)
        {
            return new ModernButton { Text = text, Location = new Point(x, y), Size = new Size(width, 42),
                BackColor = primary ? Green : Color.White, ForeColor = primary ? Color.White : Ink };
        }
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        public static Icon ClockIcon()
        {
            using (Bitmap bitmap = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush b = new SolidBrush(Green)) g.FillEllipse(b, 2, 2, 60, 60);
                using (Pen pen = new Pen(Color.FromArgb(235, 246, 228), 4))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawEllipse(pen, 13, 13, 38, 38);
                    g.DrawLine(pen, 32, 22, 32, 33);
                    g.DrawLine(pen, 32, 33, 42, 38);
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    internal sealed class Card : Panel
    {
        public Color Fill = Color.White;
        public Card()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 16))
            using (Brush b = new SolidBrush(Fill))
            using (Pen pen = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class DayBadge : Control
    {
        private static readonly Color[] Fills = {
            Color.FromArgb(226, 238, 253), Color.FromArgb(222, 242, 232),
            Color.FromArgb(236, 229, 250), Color.FromArgb(255, 236, 215),
            Color.FromArgb(251, 226, 235), Color.FromArgb(250, 244, 212),
            Color.FromArgb(220, 242, 244)
        };
        private static readonly Color[] Inks = {
            Color.FromArgb(39, 75, 125), Color.FromArgb(36, 91, 61),
            Color.FromArgb(92, 64, 137), Color.FromArgb(125, 78, 35),
            Color.FromArgb(132, 54, 82), Color.FromArgb(112, 94, 27),
            Color.FromArgb(29, 95, 103)
        };
        private readonly Color fill;

        public DayBadge(int day)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Text = day < 0 ? "Uma vez" : Display.FullDayNames[day];
            Name = "dayBadge_" + day;
            AccessibleName = Text;
            AccessibleRole = AccessibleRole.StaticText;
            TabStop = false;
            Font = Theme.Font(9, FontStyle.Bold);
            BackColor = Color.Transparent;
            fill = day < 0 ? Theme.Paper : Fills[day];
            ForeColor = day < 0 ? Theme.Muted : Inks[day];
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Size = new Size(text.Width + 24, text.Height + 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2))
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            base.OnPaint(e);
        }
    }

    internal sealed class DayBadges : Panel
    {
        private bool arranging;

        public DayBadges(Alarm alarm)
        {
            BackColor = Color.Transparent;
            Text = Display.Days(alarm);
            AccessibleName = Text;
            SuspendLayout();
            if (alarm.Days.Count == 0) Controls.Add(new DayBadge(-1));
            else foreach (int day in alarm.Days.Distinct().OrderBy(d => d)) Controls.Add(new DayBadge(day));
            ResumeLayout(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (arranging) return;
            arranging = true;
            try
            {
                int x = 0, y = 0, rowHeight = 0;
                foreach (Control badge in Controls)
                {
                    if (x > 0 && x + badge.Width > ClientSize.Width)
                    {
                        x = 0;
                        y += rowHeight + 8;
                        rowHeight = 0;
                    }
                    badge.Location = new Point(x, y);
                    x += badge.Width + 8;
                    rowHeight = Math.Max(rowHeight, badge.Height);
                }
                Height = y + rowHeight;
            }
            finally { arranging = false; }
        }
    }

    internal sealed class ModernButton : Button
    {
        private bool hover;
        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.Font(10, FontStyle.Bold);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Enabled ? (hover ? ControlPaint.Light(BackColor, .08f) : BackColor) : Color.FromArgb(230, 233, 227);
            using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(BackColor == Color.White ? Theme.Border : fill))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }

    internal sealed class SoundOption
    {
        public string Title;
        public string Value;
        public override string ToString() { return Title; }
    }

    internal sealed class SoundService : IDisposable
    {
        private SoundPlayer player;
        private System.Threading.Timer previewStop;
        private readonly object gate = new object();
        private int generation;
        public bool Muted;
        private static string Resolve(string sound)
        {
            if (String.IsNullOrWhiteSpace(sound)) sound = "Alarm01.wav";
            string path = Path.IsPathRooted(sound) ? sound : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", sound);
            if (File.Exists(path)) return path;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
        }
        public void Play(string sound, bool preview)
        {
            lock (gate)
            {
                Stop();
                if (Muted) return;
                try
                {
                    player = new SoundPlayer(Resolve(sound));
                    player.Load();
                    player.PlayLooping();
                }
                catch
                {
                    if (player != null) { player.Dispose(); player = null; }
                    SystemSounds.Exclamation.Play();
                }
                int playback = generation;
                if (preview) previewStop = new System.Threading.Timer(delegate
                {
                    lock (gate) { if (playback == generation) Stop(); }
                }, null, 3000, System.Threading.Timeout.Infinite);
            }
        }
        public void Stop()
        {
            lock (gate)
            {
                generation++;
                if (previewStop != null) { previewStop.Dispose(); previewStop = null; }
                if (player != null) { player.Stop(); player.Dispose(); player = null; }
            }
        }
        public void Dispose() { Stop(); }
        public static ComboBox Picker(string selected, int x, int y, int width)
        {
            ComboBox picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Font(10),
                Location = new Point(x, y), Size = new Size(width, 30), BackColor = Color.White, AccessibleName = "Som" };
            string[] titles = { "Amanhecer", "Pulsos suaves", "Despertador", "Sinos", "Toque clássico" };
            string[] files = { "Alarm01.wav", "Alarm02.wav", "Alarm03.wav", "chimes.wav", "Ring01.wav" };
            for (int i = 0; i < files.Length; i++) picker.Items.Add(new SoundOption { Title = titles[i], Value = files[i] });
            int index = Array.IndexOf(files, selected);
            if (index < 0 && !String.IsNullOrEmpty(selected))
            {
                picker.Items.Add(new SoundOption { Title = Path.GetFileName(selected), Value = selected });
                index = picker.Items.Count - 1;
            }
            picker.SelectedIndex = Math.Max(0, index);
            return picker;
        }
        public static void Browse(ComboBox picker, IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Escolher som do alarme", Filter = "Áudio WAV (*.wav)|*.wav", CheckFileExists = true })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                // Load synchronously to reject unsupported/corrupt WAV files before saving.
                try { using (SoundPlayer check = new SoundPlayer(dialog.FileName)) check.Load(); }
                catch (Exception ex) { MessageBox.Show(owner, "Não foi possível abrir esse áudio WAV.\n" + ex.Message, "Som inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                picker.Items.Add(new SoundOption { Title = Path.GetFileName(dialog.FileName), Value = dialog.FileName });
                picker.SelectedIndex = picker.Items.Count - 1;
            }
        }
    }
}
