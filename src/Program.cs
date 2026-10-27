using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PapaClock
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        public const int ShowMessage = 0x8000 + 73;

        [STAThread]
        private static void Main(string[] args)
        {
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\PapaClock.Desktop.v1", out first))
            {
                if (!first)
                {
                    IntPtr window = FindWindow(null, "Papa Clock");
                    if (window != IntPtr.Zero) PostMessage(window, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Report(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Report(e.ExceptionObject as Exception); };
                try { Application.Run(new MainWindow(Array.IndexOf(args, "--background") >= 0)); }
                catch (Exception ex) { Report(ex); }
                mutex.ReleaseMutex();
            }
        }

        private static void Report(Exception ex)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PapaClock");
            try { Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "errors.log"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
            MessageBox.Show("O Papa Clock encontrou um erro. Os detalhes estão em:\n" + Path.Combine(folder, "errors.log") + "\n\n" + (ex == null ? "Erro desconhecido." : ex.Message), "Papa Clock", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
