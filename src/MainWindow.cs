using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PapaClock
{
    public sealed class MainWindow : Form
    {
        private readonly AppState state;
        private readonly StateStore store;
        private readonly SoundService audio = new SoundService();
        private readonly NotifyIcon tray;
        private TrayPopupService popups;
        private readonly System.Windows.Forms.Timer ticker;
        private readonly Panel page;
        private readonly Label heading, subheading;
        private readonly ModernButton[] navigation;
        private readonly bool startHidden;
        private bool exiting, shutdownHandled, needsSave;
        private DateTime lastSaveAttempt;
        private DateTime scheduleMinute;
        private string view = "home";
        private Label clockLabel, dateLabel, nextLabel, nextDetail, timerLabel, timerStatus, homeTimer;
        private NumericUpDown timerHours, timerMinutes, timerSeconds;
        private TextBox timerName;
        private ComboBox timerSound;
        private ModernButton startTimer, pauseTimer, cancelTimer;
        private AlertWindow alert;
        private readonly CultureInfo portuguese = CultureInfo.GetCultureInfo("pt-BR");
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public MainWindow(bool background, string dataDirectory = null, bool silent = false)
        {
            startHidden = background;
            audio.Muted = silent;
            string folder = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PapaClock");
            store = new StateStore(Path.Combine(folder, "settings.json"));
            state = store.Load();
            Text = "Papa Clock";
            Icon = Theme.ClockIcon();
            Font = Theme.Font(10);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1040, 735);
            MinimumSize = new Size(1000, 780);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Paper;
            DoubleBuffered = true;

            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 204, BackColor = Theme.Ink };
            sidebar.Controls.Add(new PictureBox { Image = Icon.ToBitmap(), Location = new Point(27, 32), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom });
            sidebar.Controls.Add(Theme.Label("papa clock", 21, Color.White, 24, 94, 170, 40, FontStyle.Bold));
            sidebar.Controls.Add(Theme.Label("Seu tempo, com calma.", 9, Color.FromArgb(184, 205, 190), 27, 138, 168, 25));
            string[] titles = { "Visão geral", "Programação do dia", "Alarmes", "Temporizador", "Preferências", "Pop-ups" };
            string[] pages = { "home", "today", "alarms", "timer", "settings", "popups" };
            navigation = new ModernButton[titles.Length];
            for (int i = 0; i < titles.Length; i++)
            {
                string target = pages[i];
                ModernButton button = Theme.Button(titles[i], 16, 218 + i * 55, 172, false);
                button.Name = "nav" + target;
                button.Size = new Size(172, 45);
                button.Click += delegate { ShowPage(target); };
                navigation[i] = button;
                sidebar.Controls.Add(button);
            }
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 94 };
            footer.Controls.Add(Theme.Label("●  Ativo em segundo plano", 9, Theme.Mint, 24, 5, 175, 28));
            footer.Controls.Add(Theme.Label("PAPA CLOCK   /   1.0", 8, Color.FromArgb(158, 181, 166), 26, 40, 170, 26));
            sidebar.Controls.Add(footer);

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 25, 32, 24) };
            Panel header = new Panel { Dock = DockStyle.Top, Height = 96 };
            heading = Theme.Label("", 25, Theme.Ink, 0, 0, 700, 46, FontStyle.Bold);
            heading.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            subheading = Theme.Label("", 10, Theme.Muted, 1, 48, 700, 32);
            subheading.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            header.Controls.Add(heading);
            header.Controls.Add(subheading);
            page = new Panel { Dock = DockStyle.Fill, Name = "page" };
            body.Controls.Add(page);
            body.Controls.Add(header);
            Controls.Add(body);
            Controls.Add(sidebar);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Abrir Papa Clock", null, delegate { Restore(); });
            menu.Items.Add("Novo alarme", null, delegate { Restore(); EditAlarm(null); });
            menu.Items.Add("Temporizador", null, delegate { Restore(); ShowPage("timer"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Dispensar som", null, delegate { CompleteAlerts(false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair do Papa Clock", null, delegate { ExitFromTray(); });
            tray = new NotifyIcon { Icon = Icon, Text = "Papa Clock · Ativo", Visible = true, ContextMenuStrip = menu };
            popups = new TrayPopupService(state.Popups, delegate(string title, string message, ToolTipIcon icon)
            {
                tray.ShowBalloonTip(6000, title, message, icon);
            });
            tray.DoubleClick += delegate { Restore(); };
            tray.BalloonTipClicked += delegate { Restore(); };
            ticker = new System.Windows.Forms.Timer { Interval = 250 };
            ticker.Tick += Tick;
            ticker.Start();
            FormClosing += ClosingWindow;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };
            Shown += delegate
            {
                if (!String.IsNullOrEmpty(store.LoadWarning))
                    MessageBox.Show(this, store.LoadWarning, "Configurações do Papa Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Tick(this, EventArgs.Empty);
                if (state.Ringing.Count > 0) ShowAlerts();
            };
            ShowPage("home");
        }

        protected override void SetVisibleCore(bool value)
        {
            // Create the message window, but do not flash the main UI at Windows logon.
            if (startHidden && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowMessage) Restore();
            base.WndProc(ref m);
        }

        private void Restore()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            if (state.Ringing.Count > 0) ShowAlerts();
        }

        private void ShowPage(string target)
        {
            audio.Stop();
            // A navigation action must never stop an alarm that is currently ringing.
            if (state.Ringing.Count > 0) audio.Play(state.Ringing[0].Sound, false);
            view = target;
            page.SuspendLayout();
            while (page.Controls.Count > 0) page.Controls[0].Dispose();
            clockLabel = dateLabel = nextLabel = nextDetail = timerLabel = timerStatus = homeTimer = null;
            timerHours = timerMinutes = timerSeconds = null;
            timerName = null;
            timerSound = null;
            startTimer = pauseTimer = cancelTimer = null;
            string[] keys = { "home", "today", "alarms", "timer", "settings", "popups" };
            for (int i = 0; i < navigation.Length; i++)
            {
                bool selected = target == keys[i];
                navigation[i].BackColor = selected ? Theme.Mint : Theme.Ink;
                navigation[i].ForeColor = selected ? Theme.Ink : Color.FromArgb(205, 220, 208);
            }
            if (target == "home") BuildHome();
            else if (target == "today") BuildToday();
            else if (target == "alarms") BuildAlarms();
            else if (target == "timer") BuildTimer();
            else if (target == "popups") BuildPopups();
            else BuildSettings();
            page.ResumeLayout(true);
            UpdateLive();
        }

        private Card NewCard(int x, int y, int width, int height)
        {
            Card card = new Card { Location = new Point(x, y), Size = new Size(width, height) };
            page.Controls.Add(card);
            return card;
        }

        private void BuildHome()
        {
            heading.Text = "Tempo para o que importa.";
            subheading.Text = "Seu relógio, alarmes e pequenas pausas em um só lugar.";
            int w = page.ClientSize.Width;
            Card hero = NewCard(0, 0, w, 211);
            hero.Fill = Theme.Mint;
            hero.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            hero.Controls.Add(Theme.Label("AGORA, NO SEU TEMPO", 9, Theme.Green, 25, 18, 350, 26, FontStyle.Bold));
            clockLabel = Theme.Label("", 56, Theme.Ink, 18, 49, 408, 93, FontStyle.Bold);
            dateLabel = Theme.Label("", 10, Theme.Green, 27, 155, 410, 30);
            hero.Controls.Add(clockLabel);
            hero.Controls.Add(dateLabel);
            Label nextTitle = Theme.Label("PRÓXIMO ALARME", 9, Theme.Green, w - 253, 42, 230, 26, FontStyle.Bold);
            nextLabel = Theme.Label("", 28, Theme.Ink, w - 255, 78, 230, 55, FontStyle.Bold);
            nextDetail = Theme.Label("", 10, Theme.Green, w - 253, 143, 230, 43);
            nextTitle.Anchor = nextLabel.Anchor = nextDetail.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hero.Controls.Add(nextTitle);
            hero.Controls.Add(nextLabel);
            hero.Controls.Add(nextDetail);

            Card alarms = NewCard(0, 229, (w - 18) / 2, 271);
            alarms.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            alarms.Controls.Add(Theme.Label("Seus alarmes", 17, Theme.Ink, 22, 16, alarms.Width - 44, 36, FontStyle.Bold));
            int total = state.Alarms.Count(a => a.Enabled);
            alarms.Controls.Add(Theme.Label(total == 0 ? "Nenhum alarme ativo por aqui." : total + (total == 1 ? " alarme ativo para sua rotina." : " alarmes ativos para sua rotina."), 10, Theme.Muted, 24, 58, alarms.Width - 48, 31));
            string upcoming = String.Join("\n", state.Alarms.Where(a => a.Enabled).OrderBy(a => a.Next(DateTime.Now) ?? DateTime.MaxValue)
                .Take(2).Select(a => String.Format("{0:00}:{1:00}   {2}", a.Hour, a.Minute, a.Name)));
            alarms.Controls.Add(Theme.Label(total == 0 ? "Do café da manhã à hora de descansar,\ncada compromisso tem seu horário." : upcoming, 11, Theme.Ink, 24, 103, alarms.Width - 48, 76));
            ModernButton add = Theme.Button("+  Criar alarme", 24, 205, alarms.Width - 48, true);
            add.Name = "createAlarm";
            add.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            add.Click += delegate { EditAlarm(null); };
            alarms.Controls.Add(add);

            Card timer = NewCard(alarms.Width + 18, 229, w - alarms.Width - 18, 271);
            timer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            timer.Controls.Add(Theme.Label("Uma pausa no dia", 17, Theme.Ink, 22, 16, timer.Width - 44, 36, FontStyle.Bold));
            timer.Controls.Add(Theme.Label("Um tempo para focar. Ou para respirar.", 10, Theme.Muted, 24, 58, timer.Width - 48, 31));
            homeTimer = Theme.Label("", 37, Theme.Ink, 18, 103, timer.Width - 36, 76, FontStyle.Bold);
            timer.Controls.Add(homeTimer);
            ModernButton open = Theme.Button("Abrir temporizador", 24, 205, timer.Width - 48, false);
            open.Name = "openTimer";
            open.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            open.Click += delegate { ShowPage("timer"); };
            timer.Controls.Add(open);
            hero.Resize += delegate
            {
                int half = (hero.Width - 18) / 2;
                alarms.Width = half;
                timer.Left = half + 18;
                timer.Width = hero.Width - half - 18;
            };
            Label hint = Theme.Label("Pode fechar a janela. O Papa Clock continua na bandeja do Windows.", 9, Theme.Muted, 2, 518, w - 4, 43);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            page.Controls.Add(hint);
        }

        private static void AddAlarmDays(Card row, Alarm alarm, int top)
        {
            DayBadges days = new DayBadges(alarm) { Name = "alarmDays_" + alarm.Id,
                Location = new Point(23, top), Width = row.Width - 46,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            days.SizeChanged += delegate { row.Height = Math.Max(150, days.Bottom + 20); };
            row.Controls.Add(days);
            row.Height = Math.Max(150, days.Bottom + 20);
        }

        private void BuildToday()
        {
            DateTime now = DateTime.Now;
            scheduleMinute = now.Date.AddHours(now.Hour).AddMinutes(now.Minute);
            var scheduled = Scheduler.Today(state, now);
            heading.Text = "Programação do dia";
            subheading.Text = "Os alarmes que ainda vão tocar hoje, em ordem de horário.";
            Card calendar = NewCard(0, 0, page.Width, 125);
            calendar.Fill = Theme.Mint;
            calendar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            string weekdayName = now.ToString("dddd", portuguese);
            Label weekday = Theme.Label(Char.ToUpper(weekdayName[0], portuguese) + weekdayName.Substring(1), 25, Theme.Ink, 23, 16, 430, 49, FontStyle.Bold);
            weekday.Name = "todayWeekday";
            calendar.Controls.Add(weekday);
            Label date = Theme.Label(now.ToString("dd 'de' MMMM 'de' yyyy", portuguese), 13, Theme.Green, 27, 73, 430, 32);
            date.Name = "todayDate";
            calendar.Controls.Add(date);
            Label count = Theme.Label(scheduled.Count + (scheduled.Count == 1 ? " alarme hoje" : " alarmes hoje"), 14, Theme.Green, calendar.Width - 230, 41, 203, 43, FontStyle.Bold);
            count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            calendar.Controls.Add(count);

            FlowLayoutPanel list = new FlowLayoutPanel { Name = "todayList", Location = new Point(0, 145),
                Size = new Size(page.Width, page.Height - 145), AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            page.Controls.Add(list);
            if (scheduled.Count == 0)
            {
                Card empty = new Card { Name = "todayEmpty", Size = new Size(Math.Max(650, list.Width - 22), 178) };
                empty.Controls.Add(Theme.Label("Nenhum alarme restante para hoje.", 19, Theme.Ink, 26, 25, 600, 50, FontStyle.Bold));
                empty.Controls.Add(Theme.Label("Seus próximos alarmes continuam disponíveis na aba Alarmes.\nEsta programação é atualizada automaticamente ao longo do dia.", 11, Theme.Muted, 28, 88, 600, 65));
                list.Controls.Add(empty);
            }
            foreach (ScheduledAlarm item in scheduled)
            {
                Card row = new Card { Name = "todayAlarm_" + item.Alarm.Id, Width = Math.Max(650, list.Width - 22), Height = 150, Margin = new Padding(0, 0, 0, 12) };
                row.Controls.Add(Theme.Label(item.At.ToString("HH:mm"), 32, Theme.Ink, 19, 14, 145, 63, FontStyle.Bold));
                Label name = Theme.Label(item.Alarm.Name, 14, Theme.Ink, 178, 24, row.Width - 205, 38, FontStyle.Bold);
                name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                row.Controls.Add(name);
                AddAlarmDays(row, item.Alarm, 88);
                list.Controls.Add(row);
            }
            list.Resize += delegate { foreach (Control row in list.Controls) row.Width = Math.Max(650, list.ClientSize.Width - 22); };
        }

        private void BuildAlarms()
        {
            heading.Text = "Sua rotina, no horário.";
            subheading.Text = "Escolha os dias, dê um nome e deixe o resto com o relógio.";
            ModernButton add = Theme.Button("+  Novo alarme", 0, 0, 168, true);
            add.Name = "createAlarm";
            add.Click += delegate { EditAlarm(null); };
            page.Controls.Add(add);
            Label count = Theme.Label(state.Alarms.Count + (state.Alarms.Count == 1 ? " alarme configurado" : " alarmes configurados"), 10, Theme.Muted, 186, 1, 420, 42);
            page.Controls.Add(count);
            FlowLayoutPanel list = new FlowLayoutPanel { Location = new Point(0, 63), Size = new Size(page.Width, page.Height - 63),
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            page.Controls.Add(list);
            if (state.Alarms.Count == 0)
            {
                Card empty = new Card { Size = new Size(Math.Max(650, list.Width - 22), 260) };
                empty.Controls.Add(Theme.Label("Um lembrete faz a diferença.", 21, Theme.Ink, 26, 39, 580, 49, FontStyle.Bold));
                empty.Controls.Add(Theme.Label("Crie seu primeiro alarme para acordar, tomar um remédio\nou lembrar de um compromisso. Você escolhe os dias e o som.", 11, Theme.Muted, 28, 104, 570, 78));
                list.Controls.Add(empty);
            }
            foreach (Alarm alarm in state.Alarms.OrderBy(a => a.Hour).ThenBy(a => a.Minute))
            {
                Alarm current = alarm;
                Card row = new Card { Width = Math.Max(650, list.Width - 22), Height = 150, Margin = new Padding(0, 0, 0, 12) };
                Label time = Theme.Label(String.Format("{0:00}:{1:00}", alarm.Hour, alarm.Minute), 32, alarm.Enabled ? Theme.Ink : Theme.Muted, 19, 22, 145, 63, FontStyle.Bold);
                row.Controls.Add(time);
                row.Controls.Add(Theme.Label(alarm.Name, 13, Theme.Ink, 178, 23, row.Width - 390, 34, FontStyle.Bold));
                AddAlarmDays(row, alarm, 116);
                CheckBox enabled = new CheckBox { Text = "Ativo", Checked = alarm.Enabled, ForeColor = Theme.Green, AutoSize = true,
                    Location = new Point(row.Width - 177, 20), Anchor = AnchorStyles.Top | AnchorStyles.Right, AccessibleName = "Ativar " + alarm.Name };
                enabled.CheckedChanged += delegate
                {
                    current.Enabled = enabled.Checked;
                    if (current.Enabled && current.Days.Count == 0) current.ArmOneShot(DateTime.Now);
                    Persist(true);
                    time.ForeColor = current.Enabled ? Theme.Ink : Theme.Muted;
                    UpdateLive();
                };
                row.Controls.Add(enabled);
                ModernButton edit = Theme.Button("Editar", row.Width - 184, 64, 80, false);
                edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                edit.Click += delegate { EditAlarm(current); };
                row.Controls.Add(edit);
                ModernButton remove = Theme.Button("Excluir", row.Width - 95, 64, 78, false);
                remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                remove.ForeColor = Color.FromArgb(151, 59, 50);
                remove.Click += delegate
                {
                    if (MessageBox.Show(this, "Excluir o alarme “" + current.Name + "”?", "Excluir alarme", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    state.Alarms.Remove(current);
                    Persist(true);
                    ShowPage("alarms");
                };
                row.Controls.Add(remove);
                list.Controls.Add(row);
            }
            list.Resize += delegate { foreach (Control row in list.Controls) row.Width = Math.Max(650, list.ClientSize.Width - 22); };
        }

        private void EditAlarm(Alarm alarm)
        {
            DateTime proposed = DateTime.Now.AddMinutes(1);
            Alarm initial = alarm ?? new Alarm { Hour = proposed.Hour, Minute = proposed.Minute, Days = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4 } };
            using (AlarmEditor editor = new AlarmEditor(initial, audio, delegate { return state.Ringing.Count == 0; }))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                if (alarm == null) state.Alarms.Add(editor.Result);
                else state.Alarms[state.Alarms.IndexOf(alarm)] = editor.Result;
                Persist(true);
            }
            ShowPage("alarms");
        }

        private NumericUpDown TimerNumber(string name, string caption, int maximum, int value, int x, Card card)
        {
            card.Controls.Add(Theme.Label(caption, 9, Theme.Muted, x, 220, 99, 25));
            NumericUpDown number = new NumericUpDown { Name = name, Minimum = 0, Maximum = maximum, Value = value,
                Location = new Point(x, 251), Size = new Size(99, 38), Font = Theme.Font(18),
                TextAlign = HorizontalAlignment.Center, AccessibleName = caption };
            card.Controls.Add(number);
            return number;
        }

        private void BuildTimer()
        {
            heading.Text = "Cada pausa tem seu tempo.";
            subheading.Text = "Defina a duração. Um som avisa quando a contagem chegar ao fim.";
            Card card = NewCard(0, 0, page.Width, 562);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            timerStatus = Theme.Label("PRONTO PARA COMEÇAR", 10, Theme.Green, 28, 18, 620, 30, FontStyle.Bold);
            timerLabel = Theme.Label("", 64, Theme.Ink, 17, 57, 635, 116, FontStyle.Bold);
            card.Controls.Add(timerStatus);
            card.Controls.Add(timerLabel);
            int[] presets = { 5, 15, 25 };
            for (int i = 0; i < presets.Length; i++)
            {
                int minutes = presets[i];
                ModernButton preset = Theme.Button(minutes + " min", 28 + 112 * i, 174, 100, false);
                preset.Name = "preset" + minutes;
                preset.Click += delegate
                {
                    if (state.Timer.Status != "idle") return;
                    timerHours.Value = 0;
                    timerMinutes.Value = minutes;
                    timerSeconds.Value = 0;
                };
                card.Controls.Add(preset);
            }
            int total = state.Timer.DurationSeconds;
            timerHours = TimerNumber("timerHours", "Horas", 99, total / 3600, 28, card);
            timerMinutes = TimerNumber("timerMinutes", "Minutos", 59, (total / 60) % 60, 140, card);
            timerSeconds = TimerNumber("timerSeconds", "Segundos", 59, total % 60, 252, card);
            timerHours.ValueChanged += TimerInputChanged;
            timerMinutes.ValueChanged += TimerInputChanged;
            timerSeconds.ValueChanged += TimerInputChanged;
            card.Controls.Add(Theme.Label("NOME DO LEMBRETE", 9, Theme.Muted, 28, 307, 600, 25, FontStyle.Bold));
            timerName = new TextBox { Name = "timerName", Text = state.Timer.Name, Location = new Point(28, 339), Size = new Size(580, 29),
                MaxLength = 80, Font = Theme.Font(11), AccessibleName = "Nome do temporizador" };
            card.Controls.Add(timerName);
            card.Controls.Add(Theme.Label("SOM AO TERMINAR", 9, Theme.Muted, 28, 384, 600, 25, FontStyle.Bold));
            timerSound = SoundService.Picker(state.Timer.Sound, 28, 417, 294);
            card.Controls.Add(timerSound);
            ModernButton browse = Theme.Button("Arquivo…", 334, 412, 132, false);
            browse.Click += delegate { if (state.Ringing.Count == 0) SoundService.Browse(timerSound, this); };
            card.Controls.Add(browse);
            ModernButton preview = Theme.Button("Ouvir", 478, 412, 130, false);
            preview.Click += delegate { if (state.Ringing.Count == 0) audio.Play(((SoundOption)timerSound.SelectedItem).Value, true); };
            card.Controls.Add(preview);
            startTimer = Theme.Button("Iniciar", 28, 489, 182, true);
            startTimer.Name = "startTimer";
            startTimer.Click += delegate
            {
                audio.Stop();
                if (state.Ringing.Count > 0) audio.Play(state.Ringing[0].Sound, false);
                if (state.Timer.Status == "paused") state.Timer.Resume(DateTime.UtcNow);
                else
                {
                    int seconds = (int)(timerHours.Value * 3600 + timerMinutes.Value * 60 + timerSeconds.Value);
                    if (seconds == 0)
                    {
                        MessageBox.Show(this, "Escolha uma duração maior que zero.", "Definir duração", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    state.Timer.Name = String.IsNullOrWhiteSpace(timerName.Text) ? "Tempo esgotado" : timerName.Text.Trim();
                    state.Timer.Sound = ((SoundOption)timerSound.SelectedItem).Value;
                    state.Timer.Start(seconds, DateTime.UtcNow);
                }
                Persist(true);
                UpdateLive();
            };
            card.Controls.Add(startTimer);
            pauseTimer = Theme.Button("Pausar", 222, 489, 182, false);
            pauseTimer.Name = "pauseTimer";
            pauseTimer.Click += delegate { state.Timer.Pause(DateTime.UtcNow); Persist(true); UpdateLive(); };
            card.Controls.Add(pauseTimer);
            cancelTimer = Theme.Button("Cancelar", 416, 489, 192, false);
            cancelTimer.Name = "cancelTimer";
            cancelTimer.Click += delegate { state.Timer.Cancel(); Persist(true); UpdateLive(); };
            card.Controls.Add(cancelTimer);
        }

        private void TimerInputChanged(object sender, EventArgs e)
        {
            if (state.Timer.Status == "idle" && timerLabel != null && timerHours != null && timerMinutes != null && timerSeconds != null)
                timerLabel.Text = Display.Duration((double)(timerHours.Value * 3600 + timerMinutes.Value * 60 + timerSeconds.Value));
        }

        private void BuildSettings()
        {
            heading.Text = "Do seu jeito.";
            subheading.Text = "Pequenos ajustes para o relógio acompanhar sua rotina.";
            Card card = NewCard(0, 0, page.Width, 494);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(Theme.Label("Sempre por perto", 18, Theme.Ink, 26, 20, 620, 39, FontStyle.Bold));
            CheckBox startup = new CheckBox { Text = "Iniciar com o Windows, na bandeja do sistema", Location = new Point(29, 83), Size = new Size(620, 31), ForeColor = Theme.Ink, Name = "startup" };
            bool reading = true;
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey)) startup.Checked = key != null && key.GetValue("PapaClock") != null; }
            catch { startup.Enabled = false; }
            startup.CheckedChanged += delegate
            {
                if (reading) return;
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (startup.Checked) key.SetValue("PapaClock", "\"" + Application.ExecutablePath + "\" --background");
                        else key.DeleteValue("PapaClock", false);
                    }
                }
                catch (Exception ex)
                {
                    reading = true;
                    startup.Checked = !startup.Checked;
                    reading = false;
                    MessageBox.Show(this, "Não foi possível alterar a inicialização.\n" + ex.Message, "Iniciar com o Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            reading = false;
            card.Controls.Add(startup);
            card.Controls.Add(Theme.Label("Ao clicar em × ou minimizar, a janela vai para a bandeja.\nDê dois cliques no ícone do relógio para abrir novamente.\nPara encerrar de verdade, use “Sair do Papa Clock” no ícone.", 11, Theme.Muted, 29, 134, 620, 89));
            card.Controls.Add(Theme.Label("Tudo salvo neste computador", 17, Theme.Ink, 26, 245, 620, 38, FontStyle.Bold));
            card.Controls.Add(Theme.Label("Seus alarmes, sons e contagem ficam guardados no seu usuário.\nO relógio precisa do Windows ligado e acordado para tocar.\nConfira também o volume de saída do computador.", 10, Theme.Muted, 29, 292, 620, 79));
            ModernButton folder = Theme.Button("Abrir pasta de dados", 28, 396, 236, false);
            folder.Click += delegate
            {
                string path = Path.GetDirectoryName(store.PathName);
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            };
            card.Controls.Add(folder);
            card.Controls.Add(Theme.Label("v1.0  ·  Sem conta, sem conexão necessária.", 9, Theme.Muted, 29, 451, 620, 26));
            page.Controls.Add(Theme.Label("Alarmes recorrentes seguem o horário local do Windows.\nAo voltar de uma suspensão, alarmes atrasados em até 10 minutos são recuperados.", 9, Theme.Muted, 2, 509, page.Width - 4, 52));
        }

        private void BuildPopups()
        {
            heading.Text = "Avisos, do seu jeito.";
            subheading.Text = "Configure os pop-ups do Windows no canto inferior direito.";
            Card card = NewCard(0, 0, page.Width, 425);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(Theme.Label("Pop-ups da bandeja", 18, Theme.Ink, 26, 18, 620, 39, FontStyle.Bold));
            card.Controls.Add(Theme.Label("Todos começam desativados. As alterações são salvas automaticamente.", 10, Theme.Muted, 29, 62, 660, 28));
            PopupOption(card, "popupAlarm", "Alarmes e lembretes adiados", "Avisar também no canto da tela quando um alarme tocar.", 105,
                state.Popups.Alarm, delegate(bool enabled) { state.Popups.Alarm = enabled; });
            PopupOption(card, "popupTimer", "Fim do temporizador", "Avisar também no canto da tela quando a contagem terminar.", 182,
                state.Popups.Timer, delegate(bool enabled) { state.Popups.Timer = enabled; });
            PopupOption(card, "popupBackground", "Janela recolhida à bandeja", "Avisar ao fechar ou minimizar a janela do relógio.", 259,
                state.Popups.Background, delegate(bool enabled) { state.Popups.Background = enabled; });
            PopupOption(card, "popupSaveFailure", "Falhas ao salvar", "Avisar quando não for possível salvar os dados em segundo plano.", 336,
                state.Popups.SaveFailure, delegate(bool enabled) { state.Popups.SaveFailure = enabled; });

            Card note = NewCard(0, 444, page.Width, 108);
            note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            note.Fill = Theme.Mint;
            note.Controls.Add(Theme.Label("O alarme no centro da tela continua.", 16, Theme.Ink, 24, 14, 660, 35, FontStyle.Bold));
            note.Controls.Add(Theme.Label("A janela do alarme, o som e os botões de adiar e dispensar continuam\nfuncionando mesmo com todos os pop-ups acima desativados.", 10, Theme.Green, 26, 54, 660, 43));
        }

        private void PopupOption(Card card, string name, string title, string description, int y, bool enabled, Action<bool> update)
        {
            CheckBox option = new CheckBox { Name = name, Text = title, Checked = enabled,
                Location = new Point(29, y), Size = new Size(card.Width - 58, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink };
            option.CheckedChanged += delegate { update(option.Checked); Persist(true); };
            card.Controls.Add(option);
            Label detail = Theme.Label(description, 10, Theme.Muted, 55, y + 31, card.Width - 81, 27);
            detail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(detail);
        }

        private void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            DateTime utc = DateTime.UtcNow;
            int enabled = state.Alarms.Count(a => a.Enabled);
            var due = Scheduler.Tick(state, now, utc);
            if (due.Count > 0 || enabled != state.Alarms.Count(a => a.Enabled))
            {
                Persist(false);
                if (view == "alarms" || view == "home" || view == "today") ShowPage(view);
                if (due.Count > 0)
                {
                    ShowAlerts();
                    foreach (var group in due.GroupBy(r => r.Kind == "timer" ? PopupKind.Timer : PopupKind.Alarm))
                        popups.Show(group.Key, group.Key == PopupKind.Timer ? "Tempo esgotado" : "Chegou a hora",
                            String.Join(" · ", group.Select(r => r.Name ?? "Seu lembrete")), ToolTipIcon.Info);
                }
            }
            if (view == "today" && scheduleMinute != now.Date.AddHours(now.Hour).AddMinutes(now.Minute)) ShowPage("today");
            if (needsSave && utc - lastSaveAttempt > TimeSpan.FromSeconds(30)) Persist(false);
            if (state.Ringing.Count > 0 && (alert == null || alert.IsDisposed)) ShowAlerts();
            UpdateLive();
        }

        private void UpdateLive()
        {
            DateTime now = DateTime.Now;
            if (clockLabel != null) clockLabel.Text = now.ToString("HH:mm:ss");
            if (dateLabel != null) dateLabel.Text = now.ToString("dddd, dd 'de' MMMM", portuguese);
            var next = state.Alarms.Select(a => new { Alarm = a, At = a.Next(now) }).Where(a => a.At.HasValue).OrderBy(a => a.At.Value).FirstOrDefault();
            if (nextLabel != null)
            {
                nextLabel.Text = next == null ? "Tudo livre" : next.At.Value.ToString("HH:mm");
                nextDetail.Text = next == null ? "Crie seu primeiro lembrete." : next.Alarm.Name + "\n" + next.At.Value.ToString("ddd, dd/MM", portuguese);
            }
            double remaining = state.Timer.Remaining(DateTime.UtcNow);
            if (homeTimer != null) homeTimer.Text = Display.Duration(remaining);
            if (timerLabel != null)
            {
                bool running = state.Timer.Status == "running";
                bool paused = state.Timer.Status == "paused";
                if (running || paused) timerLabel.Text = Display.Duration(remaining);
                else TimerInputChanged(this, EventArgs.Empty);
                timerStatus.Text = running ? "CONTAGEM EM ANDAMENTO" : paused ? "PAUSADO · RESPIRE UM POUCO" : "PRONTO PARA COMEÇAR";
                startTimer.Enabled = !running;
                startTimer.Text = paused ? "Continuar" : "Iniciar";
                pauseTimer.Enabled = running;
                cancelTimer.Enabled = running || paused;
                timerHours.Enabled = timerMinutes.Enabled = timerSeconds.Enabled = timerName.Enabled = timerSound.Enabled = !running && !paused;
            }
            string tip = "Papa Clock · " + (state.Timer.Status == "running" ? Display.Duration(remaining) : next == null ? "Ativo" : "Próximo alarme " + next.At.Value.ToString("HH:mm"));
            if (tray.Text != tip) tray.Text = tip;
        }

        private bool Persist(bool notify)
        {
            lastSaveAttempt = DateTime.UtcNow;
            try { store.Save(state); needsSave = false; return true; }
            catch (Exception ex)
            {
                bool firstFailure = !needsSave;
                needsSave = true;
                if (notify) MessageBox.Show(this, "Não foi possível salvar. O relógio tentará novamente; mantenha o aplicativo aberto.\n" + ex.Message, "Falha ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (firstFailure) popups.Show(PopupKind.SaveFailure, "Não foi possível salvar", "Mantenha o Papa Clock aberto. Verifique espaço e permissão na pasta de dados.", ToolTipIcon.Warning);
                return false;
            }
        }

        private void ShowAlerts()
        {
            if (state.Ringing.Count == 0) return;
            audio.Play(state.Ringing[0].Sound, false);
            if (alert == null || alert.IsDisposed)
            {
                alert = new AlertWindow();
                alert.Dismissed += delegate { CompleteAlerts(false); };
                alert.Snoozed += delegate { CompleteAlerts(true); };
                alert.UpdateAlerts(state.Ringing);
                alert.Show();
            }
            else { alert.UpdateAlerts(state.Ringing); alert.Activate(); }
        }

        private void CompleteAlerts(bool snooze)
        {
            audio.Stop();
            if (snooze)
                foreach (Reminder reminder in state.Ringing)
                    state.Snoozed.Add(new Reminder { Name = reminder.Name, Sound = reminder.Sound, Kind = reminder.Kind, DueUtcTicks = DateTime.UtcNow.AddMinutes(5).Ticks });
            state.Ringing.Clear();
            Persist(false);
            if (alert != null && !alert.IsDisposed) alert.Dispose();
            alert = null;
            UpdateLive();
        }

        private void ExitFromTray()
        {
            bool pending = state.Alarms.Any(a => a.Enabled) || state.Timer.Status != "idle" || state.Snoozed.Count > 0 || state.Ringing.Count > 0;
            if (pending && MessageBox.Show(this, "Ao sair, os alarmes e sons param até você abrir o Papa Clock novamente.\n\nDeseja sair?", "Sair do Papa Clock", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (!Persist(true)) return;
            exiting = true;
            Close();
        }

        private void ClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                Persist(false);
                return;
            }
            Persist(false);
            Cleanup();
        }

        private void HideToTray()
        {
            Hide();
            popups.Show(PopupKind.Background, "O relógio continua por aqui", "O Papa Clock continua ativo na bandeja do Windows.", ToolTipIcon.Info);
        }

        private void Cleanup()
        {
            if (shutdownHandled) return;
            shutdownHandled = true;
            ticker.Stop();
            ticker.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            audio.Dispose();
            if (alert != null) alert.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tray != null) Cleanup();
            base.Dispose(disposing);
        }
    }
}
