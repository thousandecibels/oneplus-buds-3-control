using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Buds3Control
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon icon;
        private readonly Icon artwork;
        private readonly ControlWindow controls;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripLabel status;
        private readonly ToolStripLabel batteryStatus;
        private readonly Dictionary<NoiseMode, ToolStripMenuItem> choices;
        private readonly ToolStripMenuItem ancLevelMenu;
        private readonly ToolStripMenuItem refresh;
        private readonly ToolStripMenuItem details;
        private readonly Timer timer;
        private BudsSession session;
        private NoiseMode? current;
        private NoiseMode? pendingMode;
        private BatteryLevels battery;
        private bool busy;
        private bool exiting;
        private bool connected;
        private string problem;

        internal TrayApp()
        {
            menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Open controls", null, (sender, args) => controls.Show()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripLabel(Bluetooth.DeviceName)
            {
                Font = new Font(SystemFonts.MenuFont, FontStyle.Bold)
            });
            status = new ToolStripLabel();
            menu.Items.Add(status);
            batteryStatus = new ToolStripLabel();
            menu.Items.Add(batteryStatus);
            menu.Items.Add(new ToolStripSeparator());
            choices = new Dictionary<NoiseMode, ToolStripMenuItem>();
            AddChoice(menu.Items, NoiseMode.Cancellation);
            ancLevelMenu = new ToolStripMenuItem("ANC level");
            AddChoice(ancLevelMenu.DropDownItems, NoiseMode.AncMild);
            AddChoice(ancLevelMenu.DropDownItems, NoiseMode.AncModerate);
            AddChoice(ancLevelMenu.DropDownItems, NoiseMode.AncMax);
            AddChoice(ancLevelMenu.DropDownItems, NoiseMode.AncSmart);
            menu.Items.Add(ancLevelMenu);
            AddChoice(menu.Items, NoiseMode.Transparency);
            AddChoice(menu.Items, NoiseMode.Off);
            menu.Items.Add(new ToolStripSeparator());
            refresh = new ToolStripMenuItem("Refresh connection", null, (sender, args) => UpdateAsync(null));
            menu.Items.Add(refresh);
            details = new ToolStripMenuItem("Connection details…", null, (sender, args) =>
                MessageBox.Show(problem, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.Items.Add(details);
            menu.Items.Add(new ToolStripMenuItem("Bluetooth settings", null,
                (sender, args) => OpenBluetoothSettings()));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (sender, args) => ExitThread()));

            using (Stream iconFile = typeof(TrayApp).Assembly.GetManifestResourceStream("Buds3Control.ico"))
            {
                artwork = new Icon(iconFile, SystemInformation.SmallIconSize);
                iconFile.Position = 0;
                controls = new ControlWindow(iconFile, mode => UpdateAsync(mode), () => UpdateAsync(null), OpenBluetoothSettings);
            }
            icon = new NotifyIcon { Icon = artwork, ContextMenuStrip = menu, Text = Program.AppName, Visible = true };
            icon.MouseUp += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left) controls.Show();
            };
            icon.BalloonTipClicked += (sender, args) => controls.Show();
            Render();

            timer = new Timer { Interval = 1 };
            timer.Tick += (sender, args) =>
            {
                timer.Interval = 10000;
                UpdateAsync(null);
            };
            timer.Start();
            controls.Show();
        }

        private static void OpenBluetoothSettings()
        {
            System.Diagnostics.Process.Start("ms-settings:bluetooth");
        }

        private void AddChoice(ToolStripItemCollection items, NoiseMode mode)
        {
            string label = AncLevelLabel(mode) ?? Program.ModeLabel(mode);
            var item = new ToolStripMenuItem(label, null, (sender, args) => UpdateAsync(mode));
            choices.Add(mode, item);
            items.Add(item);
        }

        private async void UpdateAsync(NoiseMode? target)
        {
            if (exiting) return;
            if (busy)
            {
                if (target.HasValue) pendingMode = target;
                return;
            }
            busy = true;
            Render();

            Result result = await Task.Run(() => UpdateDevice(target));
            if (exiting) return;

            busy = false;
            connected = result.Connected;
            current = result.Mode;
            if (!connected || !target.HasValue) battery = result.Battery;
            problem = result.Error;
            Render();
            if (problem != null && target.HasValue)
            {
                icon.ShowBalloonTip(4500, Program.AppName, problem, ToolTipIcon.Warning);
            }

            // Keep the panel interactive while the single Bluetooth request completes.
            NoiseMode? next = pendingMode;
            pendingMode = null;
            if (connected && next.HasValue) UpdateAsync(next);
        }

        private Result UpdateDevice(NoiseMode? target)
        {
            try
            {
                if (session == null) session = BudsSession.Open();
                if (!target.HasValue)
                {
                    var result = new Result { Connected = true, Mode = session.ReadMode() };
                    try { result.Battery = session.ReadBattery(); }
                    catch (IOException) { /* A failed battery read leaves the noise controls available. */ }
                    return result;
                }

                NoiseMode? mode;
                string error = session.SetMode(target.Value, out mode);
                return new Result { Connected = true, Mode = mode, Error = error };
            }
            catch (Exception ex)
            {
                if (session != null) session.Dispose();
                session = null;
                var socketError = ex.GetBaseException() as SocketException;
                string error = ex.Message;
                if (socketError != null && !(ex is InvalidOperationException))
                {
                    error = socketError.SocketErrorCode == SocketError.AccessDenied
                        ? "Windows denied Bluetooth access. Launch the app from File Explorer."
                        : "Bluetooth link interrupted. Wake the earbuds and try again.";
                }
                return new Result { Error = error };
            }
        }

        private void Render()
        {
            string mode = Program.ModeLabel(current);
            string level = AncLevelLabel(current);
            if (level != null) mode += " · " + level;
            status.Text = connected ? ("Connected  ·  " + mode) : (busy ? "Connecting…" : "Disconnected");
            status.ToolTipText = problem;
            status.AutoToolTip = problem != null;
            batteryStatus.Visible = connected;
            batteryStatus.Text = String.Format("Left {0}   Right {1}   Case {2}",
                Program.FormatBattery(battery.Left), Program.FormatBattery(battery.Right), Program.FormatBattery(battery.Case));
            details.Visible = problem != null;
            refresh.Text = busy && !connected ? "Connecting…" : "Refresh connection";
            refresh.Enabled = !busy;
            ancLevelMenu.Enabled = connected;
            icon.Text = connected ? (Bluetooth.DeviceName + " · " + mode) : (Program.AppName + " · " + status.Text);
            foreach (var choice in choices)
            {
                choice.Value.Enabled = connected && !busy;
                choice.Value.Checked = BudsSession.MatchesMode(current, choice.Key);
            }
            controls.Render(connected, busy, current, battery, problem);
        }

        private static string AncLevelLabel(NoiseMode? mode)
        {
            switch (mode)
            {
                case NoiseMode.AncMild: return "Low";
                case NoiseMode.AncModerate: return "Moderate";
                case NoiseMode.AncMax: return "High";
                case NoiseMode.AncSmart: return "Auto";
                default: return null;
            }
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            timer.Stop();
            icon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                controls.Dispose();
                icon.Dispose();
                menu.Dispose();
                artwork.Dispose();
                if (!busy && session != null) session.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class Result
        {
            internal bool Connected;
            internal NoiseMode? Mode;
            internal BatteryLevels Battery;
            internal string Error;
        }
    }
}
