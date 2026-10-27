using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PapaClock
{
    internal static class GuiTests
    {
        private static int passed;
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception("FAIL: " + label);
            passed++;
            Console.WriteLine("PASS: " + label);
        }
        private static T Field<T>(object obj, string name)
        {
            return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
        }
        private static void ObservePopups(MainWindow window, List<string> received)
        {
            AppState state = Field<AppState>(window, "state");
            typeof(MainWindow).GetField("popups", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window,
                new TrayPopupService(state.Popups, delegate(string title, string text, ToolTipIcon icon) { received.Add(title); }));
        }
        private static CheckBox PopupOption(MainWindow window, string name)
        {
            return (CheckBox)window.Controls.Find(name, true).Single();
        }
        private static void Pump(int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(15); }
        }
        private static void Click(Control root, string name)
        {
            ((Button)root.Controls.Find(name, true).Single()).PerformClick();
            Application.DoEvents();
        }
        private static void Capture(Form window, string name)
        {
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts");
            Directory.CreateDirectory(output);
            using (Bitmap bmp = new Bitmap(window.Width, window.Height))
            {
                window.DrawToBitmap(bmp, new Rectangle(Point.Empty, window.Size));
                bmp.Save(Path.Combine(output, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void VerifyDaySchedule(MainWindow window)
        {
            Click(window, "navtoday");
            CultureInfo pt = CultureInfo.GetCultureInfo("pt-BR");
            DateTime now = DateTime.Now;
            string weekday = now.ToString("dddd", pt);
            Check(window.Controls.Find("todayWeekday", true).Single().Text == Char.ToUpper(weekday[0], pt) + weekday.Substring(1)
                && window.Controls.Find("todayDate", true).Single().Text == now.ToString("dd 'de' MMMM 'de' yyyy", pt),
                "Daily schedule shows the current weekday, day, month and year");
            Check(window.Controls.Find("todayEmpty", true).Length == 1, "Daily schedule explains when no alarms remain today");
            Capture(window, "today-empty");
            // The core tests cover midnight; keep GUI fixtures safely ahead of the real clock.
            if (now.AddMinutes(3).Date != now.Date) return;
            AppState state = Field<AppState>(window, "state");
            DateTime first = now.AddMinutes(2);
            DateTime second = now.AddMinutes(3);
            Alarm recurring = new Alarm { Name = "Uma pausa para respirar", Hour = first.Hour, Minute = first.Minute, Days = Enumerable.Range(0, 7).ToList() };
            Alarm oneTime = new Alarm { Name = "Preparar o café", Hour = second.Hour, Minute = second.Minute };
            oneTime.ArmOneShot(now);
            Alarm otherDay = new Alarm { Name = "Somente amanhã", Hour = first.Hour, Minute = first.Minute,
                Days = new List<int> { (((int)now.DayOfWeek + 6) % 7 + 1) % 7 } };
            Alarm disabled = recurring.Copy(); disabled.Id = Guid.NewGuid().ToString("N"); disabled.Enabled = false;
            state.Alarms.AddRange(new[] { oneTime, otherDay, disabled, recurring });
            Click(window, "navhome");
            Click(window, "navtoday");
            FlowLayoutPanel list = (FlowLayoutPanel)window.Controls.Find("todayList", true).Single();
            Check(list.Controls.Count == 2 && list.Controls[0].Name == "todayAlarm_" + recurring.Id && list.Controls[1].Name == "todayAlarm_" + oneTime.Id,
                "Daily schedule UI filters disabled and other-day alarms and sorts the remaining ones");
            Control days = window.Controls.Find("alarmDays_" + recurring.Id, true).Single();
            Check(Display.FullDayNames.All(day => days.Text.Contains(day)), "Daily schedule lists every configured day by name");
            Size previousSize = window.Size;
            window.Size = window.MinimumSize;
            Pump(80);
            Check(days.Controls.Cast<Control>().All(badge => days.ClientRectangle.Contains(badge.Bounds)), "All seven day badges fit at the minimum window width");
            Capture(window, "today");
            window.Size = previousSize;
            recurring.Enabled = false;
            typeof(MainWindow).GetField("scheduleMinute", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, DateTime.MinValue);
            Pump(350);
            list = (FlowLayoutPanel)window.Controls.Find("todayList", true).Single();
            Check(list.Controls.Count == 1 && list.Controls[0].Name == "todayAlarm_" + oneTime.Id, "Daily schedule refreshes automatically as its minute changes");
            state.Alarms.Clear();
        }

        [STAThread]
        public static int Main()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PapaClock-gui-" + Guid.NewGuid().ToString("N"));
            MainWindow window = null;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                window = new MainWindow(false, directory, true);
                List<string> received = new List<string>();
                ObservePopups(window, received);
                window.Show(); Pump(200);
                Check(window.Visible && Field<NotifyIcon>(window, "tray").Visible, "Main window and system tray icon open");
                Capture(window, "home");
                VerifyDaySchedule(window);
                Click(window, "navpopups");
                Check(new[] { "popupAlarm", "popupTimer", "popupBackground", "popupSaveFailure" }.All(n => !PopupOption(window, n).Checked),
                    "Popup settings page starts with every option unchecked");
                Capture(window, "popups");
                PopupOption(window, "popupTimer").Checked = true;
                AppState popupSaved = new StateStore(Path.Combine(directory, "settings.json")).Load();
                Check(popupSaved.Popups.Timer && !popupSaved.Popups.Alarm && !popupSaved.Popups.Background && !popupSaved.Popups.SaveFailure,
                    "Changing a popup option persists only that category");
                PopupOption(window, "popupTimer").Checked = false;
                Click(window, "navalarms");
                Check(window.Controls.Find("createAlarm", true).Length == 1, "Alarm page opens");
                Capture(window, "alarms-empty");

                Exception editorFailure = null;
                using (System.Windows.Forms.Timer automation = new System.Windows.Forms.Timer { Interval = 150 })
                {
                    automation.Tick += delegate
                    {
                        AlarmEditor editor = Application.OpenForms.OfType<AlarmEditor>().FirstOrDefault();
                        if (editor == null) return;
                        automation.Stop();
                        try
                        {
                            Field<TextBox>(editor, "title").Text = "Café da manhã";
                            Field<NumericUpDown>(editor, "hour").Value = 7;
                            Field<NumericUpDown>(editor, "minute").Value = 15;
                            Capture(editor, "alarm-editor");
                            ((Button)editor.AcceptButton).PerformClick();
                        }
                        catch (Exception ex) { editorFailure = ex; editor.Close(); }
                    };
                    automation.Start();
                    Click(window, "createAlarm");
                }
                if (editorFailure != null) throw editorFailure;
                AppState state = Field<AppState>(window, "state");
                Check(state.Alarms.Count == 1 && state.Alarms[0].Name == "Café da manhã" && state.Alarms[0].Hour == 7, "Alarm editor creates and saves a real alarm");
                Check(window.Controls.Find("alarmDays_" + state.Alarms[0].Id, true).Single().Text == "Segunda-feira · Terça-feira · Quarta-feira · Quinta-feira · Sexta-feira",
                    "Alarm management cards show individual days instead of weekday groups");
                Capture(window, "alarms");
                // Keep this test alarm out of the scheduler until the deliberate ringing test below.
                state.Alarms[0].Enabled = false;

                Click(window, "navtimer");
                ((NumericUpDown)window.Controls.Find("timerMinutes", true).Single()).Value = 0;
                ((NumericUpDown)window.Controls.Find("timerSeconds", true).Single()).Value = 2;
                Capture(window, "timer");
                Click(window, "startTimer");
                Check(state.Timer.Status == "running", "Timer starts from GUI");
                window.Close();
                Check(!window.Visible && !window.IsDisposed && Field<NotifyIcon>(window, "tray").Visible, "Close button hides to tray and keeps application alive");
                Check(received.Count == 0 && Field<NotifyIcon>(window, "tray").BalloonTipText == "", "Closing the window requests no tray popup by default");
                Pump(2500);
                AlertWindow alert = Field<AlertWindow>(window, "alert");
                Check(alert != null && alert.Visible && state.Ringing.Count == 1, "Countdown completes and opens alert while main window is closed");
                Check(received.Count == 0 && Field<NotifyIcon>(window, "tray").BalloonTipText == "", "Countdown central alert appears without a corner popup");
                Capture(alert, "alert");
                Click(alert, "snoozeAlert");
                Check(state.Ringing.Count == 0 && state.Snoozed.Count == 1, "Snooze button saves a five-minute reminder");
                Check(state.Snoozed[0].Kind == "timer", "Snoozing preserves the timer notification category");
                Check(new StateStore(Path.Combine(directory, "settings.json")).Load().Snoozed.Count == 1, "Snoozed reminder is stored on disk");
                window.Dispose();

                window = new MainWindow(true, directory, true);
                ObservePopups(window, received);
                window.Show(); Pump(200);
                Check(!window.Visible && Field<NotifyIcon>(window, "tray").Visible, "Background startup creates tray icon without showing main window");
                PostMessage(window.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(200);
                Check(window.Visible, "Existing-instance message restores a background window");
                state = Field<AppState>(window, "state");
                Check(state.Alarms.Count == 1 && state.Snoozed.Count == 1, "Alarm and snooze survive reopening application");
                Click(window, "navpopups");
                PopupOption(window, "popupTimer").Checked = true;
                Click(window, "navhome");
                Click(window, "navpopups");
                Check(PopupOption(window, "popupTimer").Checked && !PopupOption(window, "popupAlarm").Checked, "Popup choices remain selected when navigating between pages");

                state.Snoozed[0].DueUtcTicks = DateTime.UtcNow.Ticks;
                Pump(500);
                alert = Field<AlertWindow>(window, "alert");
                Check(alert != null && alert.Visible && received.Count == 1 && received[0] == "Tempo esgotado",
                    "Enabled timer popup accompanies the central alert for a restored snooze");
                PostMessage(window.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(100);
                Check(received.Count == 1, "Restoring an active alert does not duplicate corner notifications");
                Click(alert, "dismissAlert");
                PopupOption(window, "popupTimer").Checked = false;
                PopupOption(window, "popupBackground").Checked = true;
                window.Close();
                Check(!window.Visible && received.Count == 2 && received.Last() == "O relógio continua por aqui", "Tray notice appears only after explicitly enabling it");
                PostMessage(window.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(100);
                PopupOption(window, "popupBackground").Checked = false;

                Click(window, "navtimer");
                ((NumericUpDown)window.Controls.Find("timerMinutes", true).Single()).Value = 0;
                ((NumericUpDown)window.Controls.Find("timerSeconds", true).Single()).Value = 10;
                Click(window, "startTimer");
                Click(window, "pauseTimer");
                double remaining = state.Timer.PausedSeconds;
                Pump(400);
                Check(state.Timer.Status == "paused" && state.Timer.Remaining(DateTime.UtcNow) == remaining, "Pause button freezes remaining time");
                Click(window, "startTimer");
                Check(state.Timer.Status == "running", "Continue button resumes countdown");
                Click(window, "cancelTimer");
                Check(state.Timer.Status == "idle", "Cancel button resets countdown");
                Click(window, "navsettings");
                Check(window.Controls.Find("startup", true).Length == 1, "Preferences expose Windows startup setting");
                Capture(window, "settings");

                Alarm ringing = state.Alarms[0];
                ringing.Enabled = true;
                ringing.Hour = DateTime.Now.Hour;
                ringing.Minute = DateTime.Now.Minute;
                ringing.Days = new System.Collections.Generic.List<int> { ((int)DateTime.Now.DayOfWeek + 6) % 7 };
                window.Close(); Pump(500);
                alert = Field<AlertWindow>(window, "alert");
                Check(alert != null && alert.Visible && !window.Visible, "Scheduled alarm opens an alert from tray mode");
                Check(received.Count == 2, "Scheduled alarm keeps its central window with every corner popup disabled");
                Click(alert, "dismissAlert");
                Check(state.Ringing.Count == 0 && Field<AlertWindow>(window, "alert") == null, "Dismiss button clears active alarm");
                Check(new StateStore(Path.Combine(directory, "settings.json")).Load().Ringing.Count == 0, "Dismissal persists to disk");

                PostMessage(window.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(100);
                Click(window, "navpopups");
                PopupOption(window, "popupAlarm").Checked = true;
                ringing.LastOccurrence = null;
                Pump(500);
                alert = Field<AlertWindow>(window, "alert");
                Check(alert != null && alert.Visible && received.Count == 3 && received.Last() == "Chegou a hora", "Enabling alarm popups allows its own corner notification");
                Click(alert, "dismissAlert");
                PopupOption(window, "popupAlarm").Checked = false;

                string blockedTemp = Path.Combine(directory, "settings.json.tmp");
                MethodInfo persist = typeof(MainWindow).GetMethod("Persist", BindingFlags.Instance | BindingFlags.NonPublic);
                Directory.CreateDirectory(blockedTemp);
                bool saved = (bool)persist.Invoke(window, new object[] { false });
                Check(!saved && received.Count == 3, "Background save failure is silent when its popup option is disabled");
                Directory.Delete(blockedTemp);
                persist.Invoke(window, new object[] { false });
                PopupOption(window, "popupSaveFailure").Checked = true;
                Directory.CreateDirectory(blockedTemp);
                saved = (bool)persist.Invoke(window, new object[] { false });
                Check(!saved && received.Count == 4 && received.Last() == "Não foi possível salvar", "Save failure notification respects its separate popup option");
                Directory.Delete(blockedTemp);
                persist.Invoke(window, new object[] { false });
                PopupOption(window, "popupSaveFailure").Checked = false;
                Console.WriteLine("\n" + passed + " GUI checks passed; audio muted during automation.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                if (window != null) window.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
