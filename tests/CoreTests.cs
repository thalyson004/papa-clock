using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PapaClock;

internal static class CoreTests
{
    private static int passed;
    private static readonly DateTime Monday = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Local);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        passed++;
        Console.WriteLine("PASS: " + description);
    }
    private static Alarm Daily(int h, int m)
    {
        return new Alarm { Hour = h, Minute = m, Days = Enumerable.Range(0, 7).ToList() };
    }
    public static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PapaClock-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppState state = new AppState();
            Alarm daily = Daily(8, 0);
            state.Alarms.Add(daily);
            Check(Scheduler.Tick(state, Monday.AddSeconds(-1), Monday.AddSeconds(-1).ToUniversalTime()).Count == 0, "Alarm does not ring early");
            Check(Scheduler.Tick(state, Monday, Monday.ToUniversalTime()).Count == 1, "Daily alarm rings at its time");
            Check(Scheduler.Tick(state, Monday.AddSeconds(1), Monday.AddSeconds(1).ToUniversalTime()).Count == 0, "Repeated ticks do not duplicate alarms");
            Check(state.Ringing.Count == 1, "Active alert survives until explicitly dismissed");
            Check(state.Ringing[0].Kind == "alarm", "Scheduled alerts retain their source for popup preferences");
            Check(daily.Next(Monday).Value == Monday.AddDays(1), "Next occurrence skips an alarm already delivered today");
            Check(Scheduler.Tick(state, Monday.AddDays(1), Monday.AddDays(1).ToUniversalTime()).Count == 1, "Recurring alarm rings again the next day");

            Alarm weekday = new Alarm { Hour = 8, Days = new List<int> { 0, 1, 2, 3, 4 } };
            DateTime saturday = Monday.AddDays(5);
            state = new AppState(); state.Alarms.Add(weekday);
            Check(Scheduler.Tick(state, saturday, saturday.ToUniversalTime()).Count == 0, "Weekday alarm skips Saturday");
            Check(weekday.Next(saturday).Value == Monday.AddDays(7), "Weekday alarm advances to Monday");
            Check(Display.Days(weekday) == "Segunda-feira · Terça-feira · Quarta-feira · Quinta-feira · Sexta-feira", "Each configured weekday is listed explicitly");
            Check(Display.Days(new Alarm { Days = new List<int> { 5, 6 } }) == "Sábado · Domingo", "Weekend days are listed individually");
            Check(Display.Days(Daily(8, 0)) == String.Join(" · ", Display.FullDayNames), "Seven-day alarms list all seven days");
            Check(Display.Days(new Alarm { Days = new List<int> { 6, 0, 0, 3 } }) == "Segunda-feira · Quinta-feira · Domingo", "Day list is ordered and removes duplicates");

            AppState schedule = new AppState();
            Alarm todayLater = Daily(15, 0);
            Alarm todayEarlier = Daily(9, 0);
            Alarm otherDay = new Alarm { Hour = 10, Days = new List<int> { 1 } };
            Alarm off = Daily(11, 0); off.Enabled = false;
            Alarm past = Daily(7, 0);
            Alarm delivered = Daily(12, 0); delivered.LastOccurrence = Monday.ToString("yyyy-MM-dd");
            Alarm onceToday = new Alarm { Hour = 10, Minute = 30 }; onceToday.ArmOneShot(Monday);
            Alarm onceTomorrow = new Alarm { Hour = 6, Minute = 30 }; onceTomorrow.ArmOneShot(Monday);
            schedule.Alarms.AddRange(new[] { todayLater, otherDay, off, past, delivered, onceTomorrow, onceToday, todayEarlier });
            var appointments = Scheduler.Today(schedule, Monday);
            Check(appointments.Select(a => a.Alarm.Id).SequenceEqual(new[] { todayEarlier.Id, onceToday.Id, todayLater.Id }),
                "Daily schedule includes only enabled upcoming alarms today in time order");
            Check(appointments.All(a => a.At.Date == Monday.Date && a.At >= Monday), "Daily schedule never shows a future-day or past occurrence");
            Check(Scheduler.Today(schedule, Monday.Date.AddHours(23)).Count == 0, "Schedule is empty when today's alarms have passed");
            Check(Scheduler.Today(new AppState(), Monday).Count == 0, "Empty alarm configuration produces an empty schedule");
            AppState sundaySchedule = new AppState();
            Alarm sundayOnly = new Alarm { Hour = 9, Days = new List<int> { 6 } };
            sundaySchedule.Alarms.Add(sundayOnly);
            Check(Scheduler.Today(sundaySchedule, Monday.AddDays(6)).Single().Alarm.Id == sundayOnly.Id,
                "Daily schedule uses Sunday correctly in the Monday-first weekday configuration");
            Check(Scheduler.Today(sundaySchedule, Monday.AddDays(7)).Count == 0, "Schedule changes weekdays correctly after midnight");
            AppState midnightSchedule = new AppState();
            Alarm midnightAlarm = Daily(0, 0);
            midnightSchedule.Alarms.Add(midnightAlarm);
            Check(Scheduler.Today(midnightSchedule, Monday.Date.AddSeconds(-1)).Count == 0
                && Scheduler.Today(midnightSchedule, Monday.Date).Single().At == Monday.Date,
                "Midnight occurrence belongs to its own calendar day");

            state = new AppState(); state.Alarms.Add(Daily(8, 0));
            Check(Scheduler.Tick(state, Monday.AddMinutes(9), Monday.AddMinutes(9).ToUniversalTime()).Count == 1, "Brief sleep recovers an alarm within ten minutes");
            state = new AppState(); state.Alarms.Add(Daily(8, 0));
            Check(Scheduler.Tick(state, Monday.AddMinutes(11), Monday.AddMinutes(11).ToUniversalTime()).Count == 0, "Very old recurring alarm is not replayed");
            state = new AppState(); state.Alarms.Add(Daily(23, 59));
            DateTime midnight = Monday.AddDays(1).Date;
            Check(Scheduler.Tick(state, midnight, midnight.ToUniversalTime()).Count == 1, "Recovery crosses midnight correctly");

            Alarm once = new Alarm { Hour = 7, Minute = 30 };
            once.ArmOneShot(Monday);
            Check(once.Next(Monday).Value == Monday.AddDays(1).Date.AddHours(7).AddMinutes(30), "One-shot in the past is scheduled for tomorrow");
            DateTime oneTime = once.Next(Monday).Value;
            state = new AppState(); state.Alarms.Add(once);
            Check(Scheduler.Tick(state, oneTime, oneTime.ToUniversalTime()).Count == 1 && !once.Enabled, "One-shot rings once and disables itself");
            once.Enabled = true; once.ArmOneShot(Monday.AddDays(-3));
            Check(Scheduler.Tick(state, Monday, Monday.ToUniversalTime()).Count == 0 && !once.Enabled, "Expired one-shot disables without ringing days late");
            Alarm disabled = Daily(8, 0); disabled.Enabled = false;
            state = new AppState(); state.Alarms.Add(disabled);
            Check(Scheduler.Tick(state, Monday, Monday.ToUniversalTime()).Count == 0 && !disabled.Next(Monday).HasValue, "Disabled alarm is silent and absent from upcoming alarms");

            DateTime utc = Monday.ToUniversalTime();
            state = new AppState(); state.Timer.Start(90, utc);
            Check(Math.Abs(state.Timer.Remaining(utc.AddSeconds(30)) - 60) < .01, "Countdown uses actual elapsed time");
            state.Timer.Pause(utc.AddSeconds(30));
            Check(state.Timer.Remaining(utc.AddHours(3)) == 60, "Paused countdown stays paused as time passes");
            state.Timer.Resume(utc.AddHours(3));
            Check(state.Timer.Remaining(utc.AddHours(3).AddSeconds(10)) == 50, "Resuming preserves remaining duration");
            Check(Scheduler.Tick(state, Monday.AddHours(3).AddMinutes(2), utc.AddHours(3).AddMinutes(2)).Count == 1, "Countdown completes even if ticks were delayed");
            Check(state.Ringing.Last().Kind == "timer", "Countdown alerts are distinct from scheduled alarms");
            Check(Scheduler.Tick(state, Monday.AddHours(3).AddMinutes(3), utc.AddHours(3).AddMinutes(3)).Count == 0, "Finished countdown does not fire twice");
            state.Timer.Start(10, utc); state.Timer.Cancel();
            Check(Scheduler.Tick(state, Monday.AddMinutes(1), utc.AddMinutes(1)).Count == 0, "Canceled countdown never rings");
            bool rejected = false;
            try { state.Timer.Start(0, utc); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Zero-duration countdown is rejected");
            Check(Display.Duration(359999) == "99:59:59" && Display.Duration(.1) == "00:00:01", "Countdown display supports long durations and rounds up");

            state = new AppState();
            state.Snoozed.Add(new Reminder { Name = "Pausa", DueUtcTicks = utc.AddMinutes(5).Ticks });
            Check(Scheduler.Tick(state, Monday, utc).Count == 0, "Snoozed alert waits for its deadline");
            Check(Scheduler.Tick(state, Monday.AddMinutes(5), utc.AddMinutes(5)).Count == 1 && state.Snoozed.Count == 0, "Snoozed alert fires and is removed from queue");

            StateStore store = new StateStore(Path.Combine(directory, "settings.json"));
            Check(store.Load().Alarms.Count == 0, "First launch starts without artificial alarms");
            Check(Enum.GetValues(typeof(PopupKind)).Cast<PopupKind>().All(k => !store.Load().Popups.IsEnabled(k)), "All tray popups are disabled on first launch");
            state = new AppState();
            Alarm persisted = Daily(8, 0); persisted.Name = "Café e medicação";
            state.Alarms.Add(persisted);
            state.Timer.Start(120, utc);
            Scheduler.Tick(state, Monday, utc);
            state.Snoozed.Add(new Reminder { Name = "Água", DueUtcTicks = utc.AddMinutes(5).Ticks });
            store.Save(state);
            state = store.Load();
            Check(state.Alarms[0].Name == "Café e medicação" && state.Alarms[0].Days.Count == 7, "JSON round-trip preserves accents and recurrence");
            Check(state.Timer.Status == "running" && state.Timer.Remaining(utc.AddSeconds(30)) == 90, "Running countdown continues across application restart");
            Check(state.Ringing.Count == 1 && state.Snoozed.Count == 1, "Active and snoozed alerts survive restart");
            Check(Scheduler.Tick(state, Monday.AddSeconds(1), utc.AddSeconds(1)).Count == 0, "Persisted occurrence prevents duplicate after restart");
            state.Timer.Pause(utc.AddSeconds(30)); store.Save(state);
            Check(store.Load().Timer.Remaining(utc.AddDays(1)) == 90, "Paused countdown survives restart");
            Check(File.Exists(store.PathName + ".bak"), "Atomic save retains a backup");

            state.Popups.Timer = true;
            state.Popups.SaveFailure = true;
            store.Save(state);
            AppState withPopups = store.Load();
            Check(withPopups.Popups.Timer && withPopups.Popups.SaveFailure && !withPopups.Popups.Alarm && !withPopups.Popups.Background,
                "Independent popup choices survive saving and reopening");

            string legacyPath = Path.Combine(directory, "legacy.json");
            string currentJson = File.ReadAllText(store.PathName);
            string legacyJson = Regex.Replace(currentJson, "\"Popups\":\\{[^}]*\\},", "");
            Check(legacyJson != currentJson, "Migration fixture actually omits popup preferences");
            File.WriteAllText(legacyPath, legacyJson);
            StateStore legacyStore = new StateStore(legacyPath);
            AppState legacy = legacyStore.Load();
            Check(legacyStore.LoadWarning == null && legacy.Alarms[0].Id == state.Alarms[0].Id && legacy.Timer.Status == "paused"
                && legacy.Timer.PausedSeconds == 90 && legacy.Snoozed.Count == state.Snoozed.Count && legacy.Ringing.Count == state.Ringing.Count,
                "Older settings upgrade without losing alarms, countdowns or reminders");
            Check(Enum.GetValues(typeof(PopupKind)).Cast<PopupKind>().All(k => !legacy.Popups.IsEnabled(k)), "Upgrade starts with every tray popup disabled");
            legacyStore.Save(legacy);
            Check(legacyStore.Load().Popups != null, "Migrated settings remain valid after saving again");

            File.WriteAllText(store.PathName, "invalid json");
            StateStore recovery = new StateStore(store.PathName);
            Check(recovery.Load().Alarms.Count == 1 && recovery.LoadWarning != null, "Corrupt settings recover from backup with a warning");
            Check(Directory.GetFiles(directory, "*.corrupt-*").Length > 0, "Corrupt original is preserved for recovery");
            File.WriteAllText(store.PathName + ".bak", "also invalid");
            StateStore broken = new StateStore(store.PathName);
            Check(broken.Load().Alarms.Count == 0 && broken.LoadWarning != null, "Unrecoverable data yields a visible warning and usable defaults");

            Console.WriteLine("\n" + passed + " checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // The test owns this exact generated directory; never touch application user data.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
