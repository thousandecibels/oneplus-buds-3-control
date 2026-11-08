using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Buds3Control
{
    internal sealed class ControlWindow : IDisposable
    {
        private readonly Window window;
        private readonly TextBlock connection;
        private readonly Ellipse connectionDot;
        private readonly TextBlock batteryHint;
        private readonly TextBlock problemText;
        private readonly Dictionary<NoiseMode, Button> modes;
        private readonly BatteryCard left;
        private readonly BatteryCard right;
        private readonly BatteryCard chargingCase;
        private bool closing;

        internal ControlWindow(IntPtr icon, Action<NoiseMode> setMode, Action refresh, Action openBluetooth)
        {
            using (Stream layout = typeof(ControlWindow).Assembly.GetManifestResourceStream("ControlWindow.xaml"))
            {
                window = (Window)XamlReader.Load(layout);
            }
            window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            window.SourceInitialized += (sender, args) => ApplyCaptionColors(new WindowInteropHelper(window).Handle);
            window.Closing += (sender, args) =>
            {
                if (closing) return;
                args.Cancel = true;
                window.Hide();
            };
            ElementHost.EnableModelessKeyboardInterop(window);

            connection = Find<TextBlock>("ConnectionText");
            connectionDot = Find<Ellipse>("ConnectionDot");
            batteryHint = Find<TextBlock>("BatteryHint");
            problemText = Find<TextBlock>("Problem");
            Find<Button>("Refresh").Click += (sender, args) => refresh();
            Find<Button>("BluetoothSettings").Click += (sender, args) => openBluetooth();

            left = new BatteryCard(Find<TextBlock>("LeftBattery"), Find<ProgressBar>("LeftCharge"));
            right = new BatteryCard(Find<TextBlock>("RightBattery"), Find<ProgressBar>("RightCharge"));
            chargingCase = new BatteryCard(Find<TextBlock>("CaseBattery"), Find<ProgressBar>("CaseCharge"));
            modes = new Dictionary<NoiseMode, Button>
            {
                { NoiseMode.Cancellation, Find<Button>("Cancellation") },
                { NoiseMode.Transparency, Find<Button>("Transparency") },
                { NoiseMode.Off, Find<Button>("Off") },
                { NoiseMode.AncMild, Find<Button>("Low") },
                { NoiseMode.AncModerate, Find<Button>("Moderate") },
                { NoiseMode.AncMax, Find<Button>("High") },
                { NoiseMode.AncSmart, Find<Button>("Auto") }
            };
            foreach (var mode in modes)
            {
                NoiseMode target = mode.Key;
                mode.Value.Click += (sender, args) => setMode(target);
            }
        }

        internal void Show()
        {
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        internal void Render(bool connected, bool busy, NoiseMode? current, BatteryLevels battery, string problem)
        {
            connection.Text = connected ? "Connected" : (busy ? "Connecting…" : "Disconnected");
            connectionDot.Fill = (System.Windows.Media.Brush)window.Resources[connected ? "Mint" : "Muted"];

            left.Update(connected ? battery.Left : null);
            right.Update(connected ? battery.Right : null);
            chargingCase.Update(connected ? battery.Case : null);
            batteryHint.Text = connected && !battery.Case.HasValue ? "Open the case lid for a reading" : "";

            problemText.Text = problem ?? "";
            problemText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;

            foreach (var mode in modes)
            {
                bool selected = BudsSession.MatchesMode(current, mode.Key);
                mode.Value.IsEnabled = connected;
                mode.Value.Tag = selected ? "Selected" : null;
                AutomationProperties.SetItemStatus(mode.Value, selected ? "Selected" : "");
            }
        }

        public void Dispose()
        {
            closing = true;
            window.Close();
        }

        private T Find<T>(string name) where T : FrameworkElement
        {
            return (T)window.FindName(name);
        }

        private static void ApplyCaptionColors(IntPtr handle)
        {
            if (SystemParameters.HighContrast) return;

            const int captionColor = 35;
            const int textColor = 36;
            int background = 0x001C1410; // COLORREF for #10141C.
            int foreground = 0x00FAF5F1; // COLORREF for #F1F5FA.
            DwmSetWindowAttribute(handle, captionColor, ref background, sizeof(int));
            DwmSetWindowAttribute(handle, textColor, ref foreground, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        private sealed class BatteryCard
        {
            private readonly TextBlock label;
            private readonly ProgressBar charge;

            internal BatteryCard(TextBlock label, ProgressBar charge)
            {
                this.label = label;
                this.charge = charge;
            }

            internal void Update(byte? level)
            {
                label.Text = Program.FormatBattery(level);
                charge.Value = level.GetValueOrDefault();
                charge.Visibility = level.HasValue ? Visibility.Visible : Visibility.Hidden;
            }
        }
    }
}
