using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Buds3Control
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon icon;
        private readonly Icon artwork;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripLabel status;
        private readonly Dictionary<NoiseMode, ToolStripMenuItem> choices;
        private readonly ToolStripMenuItem refresh;
        private readonly ToolStripMenuItem details;
        private readonly Timer timer;
        private BudsSession session;
        private NoiseMode? current;
        private bool busy;
        private bool exiting;
        private bool connected;
        private string problem;

        internal TrayApp()
        {
            menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripLabel(Bluetooth.DeviceName)
            {
                Font = new Font(SystemFonts.MenuFont, FontStyle.Bold)
            });
            status = new ToolStripLabel();
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            choices = new Dictionary<NoiseMode, ToolStripMenuItem>();
            AddChoice(NoiseMode.Cancellation);
            AddChoice(NoiseMode.Transparency);
            AddChoice(NoiseMode.Off);
            menu.Items.Add(new ToolStripSeparator());
            refresh = new ToolStripMenuItem("Refresh connection", null, (sender, args) => UpdateAsync(null));
            menu.Items.Add(refresh);
            details = new ToolStripMenuItem("Connection details…", null, (sender, args) =>
                MessageBox.Show(problem, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.Items.Add(details);
            menu.Items.Add(new ToolStripMenuItem("Bluetooth settings", null,
                (sender, args) => System.Diagnostics.Process.Start("ms-settings:bluetooth")));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (sender, args) => ExitThread()));

            artwork = CreateIcon();
            icon = new NotifyIcon { Icon = artwork, ContextMenuStrip = menu, Text = Program.AppName, Visible = true };
            icon.MouseUp += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left) return;
                // NotifyIcon already handles the right-click menu. Focus it here for left clicks too.
                SetForegroundWindow(menu.Handle);
                menu.Show(Cursor.Position);
            };
            icon.BalloonTipClicked += (sender, args) => UpdateAsync(null);
            Render();

            timer = new Timer { Interval = 1 };
            timer.Tick += (sender, args) =>
            {
                timer.Interval = 10000;
                UpdateAsync(null);
            };
            timer.Start();
        }

        private void AddChoice(NoiseMode mode)
        {
            var item = new ToolStripMenuItem(Program.ModeLabel(mode), null, (sender, args) => UpdateAsync(mode));
            choices.Add(mode, item);
            menu.Items.Add(item);
        }

        private async void UpdateAsync(NoiseMode? target)
        {
            if (busy || exiting) return;
            busy = true;
            Render();

            Result result = await Task.Run(() => UpdateDevice(target));
            if (exiting) return;

            busy = false;
            connected = result.Connected;
            current = result.Mode;
            problem = result.Error;
            Render();
            if (problem != null && target.HasValue)
            {
                icon.ShowBalloonTip(4500, Program.AppName, problem, ToolTipIcon.Warning);
            }
        }

        private Result UpdateDevice(NoiseMode? target)
        {
            try
            {
                if (session == null) session = BudsSession.Open();
                if (!target.HasValue) return new Result { Connected = true, Mode = session.ReadMode() };

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
            status.Text = connected ? ("Connected  ·  " + mode) : (busy ? "Connecting…" : "Disconnected");
            status.ToolTipText = problem;
            status.AutoToolTip = problem != null;
            details.Visible = problem != null;
            refresh.Text = busy && !connected ? "Connecting…" : "Refresh connection";
            refresh.Enabled = !busy;
            icon.Text = connected ? (Bluetooth.DeviceName + " · " + mode) : (Program.AppName + " · " + status.Text);
            foreach (var choice in choices)
            {
                choice.Value.Enabled = connected && !busy;
                choice.Value.Checked = choice.Key == current;
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
                icon.Dispose();
                menu.Dispose();
                artwork.Dispose();
                if (!busy && session != null) session.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(0, 101, 204)))
            using (var pen = new Pen(Color.White, 2.5f))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.FillEllipse(brush, 1, 1, 30, 30);
                graphics.DrawArc(pen, 7, 7, 18, 18, 180, 180);
                graphics.FillEllipse(Brushes.White, 6, 14, 6, 10);
                graphics.FillEllipse(Brushes.White, 20, 14, 6, 10);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        private sealed class Result
        {
            internal bool Connected;
            internal NoiseMode? Mode;
            internal string Error;
        }
    }
}
