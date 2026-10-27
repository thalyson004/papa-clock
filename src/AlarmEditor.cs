using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PapaClock
{
    internal sealed class AlarmEditor : Form
    {
        public Alarm Result;
        private readonly TextBox title;
        private readonly NumericUpDown hour, minute;
        private readonly List<CheckBox> days = new List<CheckBox>();
        private readonly ComboBox sound;
        private readonly CheckBox enabled;
        private readonly SoundService audio;
        private readonly Func<bool> canPreview;

        public AlarmEditor(Alarm original, SoundService soundService, Func<bool> previewAllowed)
        {
            Result = original.Copy();
            audio = soundService;
            canPreview = previewAllowed;
            Text = "Configurar alarme";
            Icon = Theme.ClockIcon();
            Font = Theme.Font(10);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(520, 543);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Paper;
            Controls.Add(Theme.Label("Um horário para você", 22, Theme.Ink, 28, 20, 460, 42, FontStyle.Bold));
            Controls.Add(Theme.Label("Escolha quando e como quer ser lembrado.", 10, Theme.Muted, 28, 65, 460, 25));
            Controls.Add(Theme.Label("HORÁRIO", 9, Theme.Muted, 28, 108, 200, 24, FontStyle.Bold));
            hour = Number(0, 23, original.Hour, 28, 139);
            minute = Number(0, 59, original.Minute, 158, 139);
            Controls.Add(hour);
            Label separator = Theme.Label(":", 26, Theme.Ink, 132, 137, 23, 49);
            separator.AutoEllipsis = false;
            Controls.Add(separator);
            Controls.Add(minute);
            enabled = new CheckBox { Text = "Alarme ativo", Checked = original.Enabled, Location = new Point(302, 151), AutoSize = true, ForeColor = Theme.Green };
            Controls.Add(enabled);
            Controls.Add(Theme.Label("NOME", 9, Theme.Muted, 28, 205, 400, 23, FontStyle.Bold));
            title = new TextBox { Text = original.Name, Location = new Point(28, 234), Size = new Size(464, 29), MaxLength = 80, Font = Theme.Font(11), AccessibleName = "Nome do alarme" };
            Controls.Add(title);
            Controls.Add(Theme.Label("REPETIR", 9, Theme.Muted, 28, 280, 200, 23, FontStyle.Bold));
            for (int i = 0; i < 7; i++)
            {
                CheckBox day = new CheckBox { Text = Display.DayNames[i], Appearance = Appearance.Button,
                    TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, Location = new Point(28 + i * 66, 311),
                    Size = new Size(58, 39), Checked = original.Days.Contains(i), Cursor = Cursors.Hand };
                day.FlatAppearance.BorderColor = Theme.Border;
                day.FlatAppearance.CheckedBackColor = Theme.Mint;
                days.Add(day);
                Controls.Add(day);
            }
            Controls.Add(Theme.Label("Sem dias selecionados: toca uma vez, no próximo horário.", 9, Theme.Muted, 28, 356, 464, 26));
            Controls.Add(Theme.Label("SOM", 9, Theme.Muted, 28, 390, 200, 24, FontStyle.Bold));
            sound = SoundService.Picker(original.Sound, 28, 421, 242);
            Controls.Add(sound);
            ModernButton browse = Theme.Button("Arquivo…", 280, 416, 106, false);
            browse.Click += delegate { if (canPreview()) { audio.Stop(); SoundService.Browse(sound, this); } };
            Controls.Add(browse);
            ModernButton preview = Theme.Button("Ouvir", 396, 416, 96, false);
            preview.Click += delegate { if (canPreview()) audio.Play(((SoundOption)sound.SelectedItem).Value, true); };
            Controls.Add(preview);
            ModernButton cancel = Theme.Button("Cancelar", 248, 481, 112, false);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            ModernButton save = Theme.Button("Salvar alarme", 372, 481, 120, true);
            save.Click += Save;
            Controls.Add(save);
            AcceptButton = save;
            CancelButton = cancel;
            FormClosed += delegate { if (canPreview()) audio.Stop(); };
        }

        private static NumericUpDown Number(int min, int max, int value, int x, int y)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Location = new Point(x, y),
                Size = new Size(102, 52), Font = Theme.Font(27), TextAlign = HorizontalAlignment.Center,
                AccessibleName = max == 23 ? "Hora" : "Minuto" };
        }

        private void Save(object sender, EventArgs e)
        {
            var newDays = Enumerable.Range(0, 7).Where(i => days[i].Checked).ToList();
            if (Result.Hour != (int)hour.Value || Result.Minute != (int)minute.Value || !Result.Days.SequenceEqual(newDays))
                Result.LastOccurrence = null;
            Result.Name = String.IsNullOrWhiteSpace(title.Text) ? "Meu alarme" : title.Text.Trim();
            Result.Hour = (int)hour.Value;
            Result.Minute = (int)minute.Value;
            Result.Days = newDays;
            Result.Sound = ((SoundOption)sound.SelectedItem).Value;
            Result.Enabled = enabled.Checked;
            // Editing the label/sound must not re-trigger an occurrence already delivered today.
            if (Result.Days.Count == 0) Result.ArmOneShot(DateTime.Now);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class AlertWindow : Form
    {
        private readonly Label title;
        private readonly Label detail;
        public event EventHandler Dismissed;
        public event EventHandler Snoozed;
        private bool completing;
        public AlertWindow()
        {
            Text = "Papa Clock · Hora do lembrete";
            Icon = Theme.ClockIcon();
            ClientSize = new Size(480, 285);
            BackColor = Theme.Paper;
            Font = Theme.Font(10);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            Controls.Add(Theme.Label("CHEGOU A HORA", 10, Theme.Green, 28, 25, 424, 25, FontStyle.Bold));
            title = Theme.Label("Seu lembrete", 23, Theme.Ink, 28, 64, 424, 54, FontStyle.Bold);
            detail = Theme.Label("", 11, Theme.Muted, 28, 126, 424, 50);
            Controls.Add(title);
            Controls.Add(detail);
            ModernButton snooze = Theme.Button("Adiar por 5 min", 28, 211, 202, false);
            snooze.Name = "snoozeAlert";
            snooze.Click += delegate { Complete(true); };
            Controls.Add(snooze);
            ModernButton dismiss = Theme.Button("Dispensar", 244, 211, 208, true);
            dismiss.Name = "dismissAlert";
            dismiss.Click += delegate { Complete(false); };
            Controls.Add(dismiss);
            AcceptButton = dismiss;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!completing && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Complete(false);
                }
            };
        }
        public void UpdateAlerts(System.Collections.Generic.List<Reminder> alerts)
        {
            title.Text = alerts[0].Name;
            detail.Text = DateTime.Now.ToString("HH:mm") + (alerts.Count > 1 ? "  ·  " + alerts.Count + " lembretes tocando\n" + String.Join(" · ", alerts.Skip(1).Select(a => a.Name)) : "\nUma pausa para o que importa.");
        }
        private void Complete(bool snooze)
        {
            if (completing) return;
            completing = true;
            EventHandler handler = snooze ? Snoozed : Dismissed;
            if (handler != null) handler(this, EventArgs.Empty);
            Close();
        }
    }
}
