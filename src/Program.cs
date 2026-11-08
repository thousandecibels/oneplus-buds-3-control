using System;
using System.Threading;
using System.Windows.Forms;

namespace Buds3Control
{
    internal static class Program
    {
        internal const string AppName = "Buds 3 Control";

        [STAThread]
        private static int Main(string[] args)
        {
            using (var instance = new Mutex(false, @"Local\Buds3Control"))
            {
                bool acquired;
                try { acquired = instance.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }

                if (!acquired)
                {
                    const string message = "Buds 3 Control is already running in the taskbar notification area.";
                    if (args.Length == 0) MessageBox.Show(message, AppName);
                    else Console.Error.WriteLine(message);
                    return 1;
                }

                try
                {
                    if (args.Length > 0) return RunCommand(args);

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var tray = new TrayApp()) Application.Run(tray);
                    return 0;
                }
                finally
                {
                    instance.ReleaseMutex();
                }
            }
        }

        private static int RunCommand(string[] args)
        {
            NoiseMode? target = null;
            if (args.Length == 2 && args[0] == "--set")
            {
                switch (args[1].ToLowerInvariant())
                {
                    case "off": target = NoiseMode.Off; break;
                    case "anc": target = NoiseMode.Cancellation; break;
                    case "transparency": target = NoiseMode.Transparency; break;
                }
            }
            if (!target.HasValue && (args.Length != 1 || args[0] != "--status"))
            {
                Console.Error.WriteLine("Usage: Buds3Control.exe [--status | --set off|anc|transparency]");
                return 2;
            }

            try
            {
                using (BudsSession session = BudsSession.Open())
                {
                    NoiseMode? current;
                    string error = null;
                    if (target.HasValue) error = session.SetMode(target.Value, out current);
                    else current = session.ReadMode();
                    Console.WriteLine(ModeLabel(current));
                    if (error == null) return 0;
                    Console.Error.WriteLine(error);
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetBaseException().Message);
                return 1;
            }
        }

        internal static string ModeLabel(NoiseMode? mode)
        {
            switch (BudsSession.ModeGroup(mode))
            {
                case NoiseMode.Cancellation: return "Noise Cancellation";
                case NoiseMode.Transparency: return "Transparency";
                case NoiseMode.Off: return "Off";
                default: return "Unknown mode";
            }
        }
    }
}
