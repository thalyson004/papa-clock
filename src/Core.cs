using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PapaClock
{
    [DataContract]
    public sealed class Alarm
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "Meu alarme";
        [DataMember] public int Hour = 8;
        [DataMember] public int Minute;
        // Monday = 0, Sunday = 6. An empty selection means the next occurrence only.
        [DataMember] public List<int> Days = new List<int>();
        [DataMember] public bool Enabled = true;
        [DataMember] public string Sound = "Alarm01.wav";
        [DataMember] public long OneShotUtcTicks;
        [DataMember] public string LastOccurrence;

        public Alarm Copy()
        {
            return new Alarm { Id = Id, Name = Name, Hour = Hour, Minute = Minute,
                Days = new List<int>(Days), Enabled = Enabled, Sound = Sound,
                OneShotUtcTicks = OneShotUtcTicks, LastOccurrence = LastOccurrence };
        }

        public void ArmOneShot(DateTime now)
        {
            DateTime at = now.Date.AddHours(Hour).AddMinutes(Minute);
            if (at <= now) at = at.AddDays(1);
            OneShotUtcTicks = at.ToUniversalTime().Ticks;
        }

        public DateTime? Next(DateTime now)
        {
            if (!Enabled) return null;
            if (Days.Count == 0)
            {
                if (OneShotUtcTicks <= 0) return null;
                DateTime once = new DateTime(OneShotUtcTicks, DateTimeKind.Utc).ToLocalTime();
                return once >= now ? (DateTime?)once : null;
            }
            for (int i = 0; i <= 7; i++)
            {
                DateTime at = now.Date.AddDays(i).AddHours(Hour).AddMinutes(Minute);
                if (at >= now && Days.Contains(((int)at.DayOfWeek + 6) % 7)
                    && LastOccurrence != at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) return at;
            }
            return null;
        }
    }

    [DataContract]
    public sealed class Countdown
    {
        [DataMember] public string Name = "Hora de fazer uma pausa";
        [DataMember] public string Sound = "Alarm02.wav";
        [DataMember] public string Status = "idle";
        [DataMember] public long DeadlineUtcTicks;
        [DataMember] public double PausedSeconds;
        [DataMember] public int DurationSeconds = 1500;

        public double Remaining(DateTime utcNow)
        {
            return Status == "running" ? Math.Max(0, (DeadlineUtcTicks - utcNow.Ticks) / (double)TimeSpan.TicksPerSecond)
                : Status == "paused" ? Math.Max(0, PausedSeconds) : DurationSeconds;
        }
        public void Start(int seconds, DateTime utcNow)
        {
            if (seconds < 1 || seconds > 359999) throw new ArgumentOutOfRangeException("seconds");
            DurationSeconds = seconds;
            DeadlineUtcTicks = utcNow.AddSeconds(seconds).Ticks;
            PausedSeconds = 0;
            Status = "running";
        }
        public void Pause(DateTime utcNow)
        {
            if (Status != "running") return;
            PausedSeconds = Remaining(utcNow);
            Status = "paused";
        }
        public void Resume(DateTime utcNow)
        {
            if (Status != "paused") return;
            DeadlineUtcTicks = utcNow.AddSeconds(PausedSeconds).Ticks;
            Status = "running";
        }
        public void Cancel() { Status = "idle"; DeadlineUtcTicks = 0; PausedSeconds = 0; }
    }

    [DataContract]
    public sealed class Reminder
    {
        [DataMember] public string Name;
        [DataMember] public string Sound;
        [DataMember] public long DueUtcTicks;
        [DataMember] public string Kind;
    }

    public enum PopupKind { Alarm, Timer, Background, SaveFailure }

    [DataContract]
    public sealed class PopupSettings
    {
        [DataMember] public bool Alarm;
        [DataMember] public bool Timer;
        [DataMember] public bool Background;
        [DataMember] public bool SaveFailure;

        public bool IsEnabled(PopupKind kind)
        {
            switch (kind)
            {
                case PopupKind.Alarm: return Alarm;
                case PopupKind.Timer: return Timer;
                case PopupKind.Background: return Background;
                case PopupKind.SaveFailure: return SaveFailure;
                default: return false;
            }
        }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<Alarm> Alarms = new List<Alarm>();
        [DataMember] public Countdown Timer = new Countdown();
        [DataMember] public List<Reminder> Snoozed = new List<Reminder>();
        // Active alerts are durable: a restart must not silently discard a ringing alert.
        [DataMember] public List<Reminder> Ringing = new List<Reminder>();
        [DataMember] public PopupSettings Popups = new PopupSettings();
    }

    public sealed class StateStore
    {
        public readonly string PathName;
        public string LoadWarning;
        public StateStore(string path) { PathName = path; }

        public AppState Load()
        {
            if (!File.Exists(PathName)) return new AppState();
            try { return Read(PathName); }
            catch (Exception ex)
            {
                string recovered = PathName + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                try { File.Copy(PathName, recovered, false); } catch { }
                try
                {
                    AppState backup = Read(PathName + ".bak");
                    LoadWarning = "As configurações foram recuperadas da cópia de segurança. O arquivo anterior foi preservado.";
                    return backup;
                }
                catch
                {
                    LoadWarning = "Não foi possível ler as configurações. Uma cópia do arquivo foi preservada. Detalhe: " + ex.Message;
                    return new AppState();
                }
            }
        }

        private static AppState Read(string path)
        {
            AppState state;
            using (FileStream stream = File.OpenRead(path))
                state = (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(stream);
            if (state == null || state.Version != 1 || state.Alarms == null || state.Timer == null
                || state.Snoozed == null || state.Ringing == null) throw new InvalidDataException("Estrutura de dados inválida.");
            // Older settings have no Popups member. Upgrade without changing alarms or countdowns.
            if (state.Popups == null) state.Popups = new PopupSettings();
            HashSet<string> ids = new HashSet<string>();
            foreach (Alarm alarm in state.Alarms)
            {
                if (alarm == null || String.IsNullOrWhiteSpace(alarm.Id) || !ids.Add(alarm.Id)
                    || alarm.Hour < 0 || alarm.Hour > 23 || alarm.Minute < 0 || alarm.Minute > 59
                    || alarm.Days == null || alarm.Days.Any(d => d < 0 || d > 6)
                    || alarm.OneShotUtcTicks < 0 || alarm.OneShotUtcTicks > DateTime.MaxValue.Ticks)
                    throw new InvalidDataException("Alarme inválido.");
            }
            Countdown timer = state.Timer;
            if ((timer.Status != "idle" && timer.Status != "running" && timer.Status != "paused")
                || timer.DurationSeconds < 1 || timer.DurationSeconds > 359999
                || timer.DeadlineUtcTicks < 0 || timer.DeadlineUtcTicks > DateTime.MaxValue.Ticks
                || Double.IsNaN(timer.PausedSeconds) || Double.IsInfinity(timer.PausedSeconds)
                || timer.PausedSeconds < 0 || timer.PausedSeconds > 359999
                || (timer.Status == "running" && timer.DeadlineUtcTicks == 0))
                throw new InvalidDataException("Temporizador inválido.");
            foreach (Reminder reminder in state.Snoozed.Concat(state.Ringing))
                if (reminder == null || reminder.DueUtcTicks < 0 || reminder.DueUtcTicks > DateTime.MaxValue.Ticks)
                    throw new InvalidDataException("Lembrete inválido.");
            return state;
        }

        public void Save(AppState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            string temp = PathName + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, state);
                stream.Flush(true);
            }
            if (File.Exists(PathName)) File.Replace(temp, PathName, PathName + ".bak");
            else File.Move(temp, PathName);
        }
    }

    public sealed class ScheduledAlarm
    {
        public Alarm Alarm;
        public DateTime At;
    }

    public static class Scheduler
    {
        public static List<ScheduledAlarm> Today(AppState state, DateTime localNow)
        {
            return state.Alarms.Select(a => new { Alarm = a, At = a.Next(localNow) })
                .Where(a => a.At.HasValue && a.At.Value.Date == localNow.Date)
                .Select(a => new ScheduledAlarm { Alarm = a.Alarm, At = a.At.Value })
                .OrderBy(a => a.At).ThenBy(a => a.Alarm.Name).ToList();
        }

        public static List<Reminder> Tick(AppState state, DateTime localNow, DateTime utcNow)
        {
            List<Reminder> due = new List<Reminder>();
            foreach (Alarm alarm in state.Alarms.Where(a => a.Enabled))
            {
                if (alarm.Days.Count == 0)
                {
                    if (alarm.OneShotUtcTicks > 0 && alarm.OneShotUtcTicks <= utcNow.Ticks)
                    {
                        alarm.Enabled = false;
                        if (utcNow.Ticks - alarm.OneShotUtcTicks <= TimeSpan.FromMinutes(10).Ticks)
                            due.Add(new Reminder { Name = alarm.Name, Sound = alarm.Sound, Kind = "alarm" });
                    }
                    continue;
                }
                // Also examine yesterday so an alarm just before midnight survives a brief sleep.
                for (int offset = -1; offset <= 0; offset++)
                {
                    DateTime at = localNow.Date.AddDays(offset).AddHours(alarm.Hour).AddMinutes(alarm.Minute);
                    string key = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (alarm.Days.Contains(((int)at.DayOfWeek + 6) % 7) && localNow >= at
                        && localNow - at <= TimeSpan.FromMinutes(10) && alarm.LastOccurrence != key)
                    {
                        alarm.LastOccurrence = key;
                        due.Add(new Reminder { Name = alarm.Name, Sound = alarm.Sound, Kind = "alarm" });
                    }
                }
            }
            if (state.Timer.Status == "running" && state.Timer.DeadlineUtcTicks <= utcNow.Ticks)
            {
                due.Add(new Reminder { Name = state.Timer.Name, Sound = state.Timer.Sound, Kind = "timer" });
                state.Timer.Cancel();
            }
            foreach (Reminder reminder in state.Snoozed.Where(r => r.DueUtcTicks <= utcNow.Ticks).ToArray())
            {
                due.Add(reminder);
                state.Snoozed.Remove(reminder);
            }
            state.Ringing.AddRange(due);
            return due;
        }
    }

    public static class Display
    {
        public static readonly string[] DayNames = { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
        public static readonly string[] FullDayNames = { "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado", "Domingo" };
        public static string Days(Alarm alarm)
        {
            if (alarm.Days.Count == 0) return "Uma vez";
            return String.Join(" · ", alarm.Days.Distinct().OrderBy(d => d).Select(d => FullDayNames[d]));
        }
        public static string Duration(double seconds)
        {
            int s = (int)Math.Ceiling(Math.Max(0, seconds));
            return String.Format("{0:00}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
        }
    }
}
