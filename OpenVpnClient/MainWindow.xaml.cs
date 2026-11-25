using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpenVpnClient.Services;
using OpenVpnClient.ViewModels;

namespace OpenVpnClient
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _dwell;
        private readonly DispatcherTimer _hide;
        private bool _open;
        private bool _pointerInside;

        public MainWindow()
        {
            InitializeComponent();
            _dwell = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _dwell.Tick += (_, _) =>
            {
                _dwell.Stop();
                if (_pointerInside) OpenStrip();
            };
            _hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _hide.Tick += (_, _) =>
            {
                _hide.Stop();
                if (!_pointerInside) CloseStrip();
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Place();
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName is nameof(MainViewModel.IsConnected)
                        or nameof(MainViewModel.IsConnecting)
                        or nameof(MainViewModel.ConnectionState)
                        or nameof(MainViewModel.StatusMessage)
                        or nameof(MainViewModel.ShowAuthDialog))
                    {
                        Paint(vm);
                        if (vm.ShowAuthDialog) OpenStrip();
                    }
                };
                Paint(vm);
            }
        }

        private void Place()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 8;
        }

        private void Paint(MainViewModel vm)
        {
            Color color = vm.IsConnected
                ? Color.FromRgb(61, 220, 151)
                : vm.ConnectionState == ConnectionState.Error
                    ? Color.FromRgb(255, 107, 107)
                    : Color.FromRgb(108, 155, 255);

            Dot.Fill = new SolidColorBrush(color);
            StripDot.Fill = new SolidColorBrush(color);
            PillText.Text = vm.IsConnected ? "On" : vm.IsConnecting ? "…" : "VPN";

            AuthCard.Visibility = _open && vm.ShowAuthDialog ? Visibility.Visible : Visibility.Collapsed;
            if (_open && vm.ShowAuthDialog && Height < 250)
                Animate(Width, 230);
        }

        private void Shell_MouseEnter(object sender, MouseEventArgs e)
        {
            _pointerInside = true;
            _hide.Stop();
            if (!_open) _dwell.Start();
        }

        private void Shell_MouseLeave(object sender, MouseEventArgs e)
        {
            _pointerInside = false;
            _dwell.Stop();
            if (_open && DataContext is MainViewModel vm && !vm.ShowAuthDialog)
                _hide.Start();
        }

        private void OpenStrip()
        {
            _open = true;
            Pill.Visibility = Visibility.Collapsed;
            Strip.Visibility = Visibility.Visible;
            var auth = DataContext is MainViewModel vm && vm.ShowAuthDialog;
            Animate(640, auth ? 230 : 56);
            AuthCard.Visibility = auth ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CloseStrip()
        {
            if (DataContext is MainViewModel { ShowAuthDialog: true }) return;
            _open = false;
            Strip.Visibility = Visibility.Collapsed;
            AuthCard.Visibility = Visibility.Collapsed;
            Pill.Visibility = Visibility.Visible;
            Animate(108, 28);
        }

        private void Animate(double width, double height)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            BeginAnimation(HeightProperty, new DoubleAnimation(height, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
            SizeChanged -= OnSize;
            SizeChanged += OnSize;
        }

        private void OnSize(object sender, SizeChangedEventArgs e) => Place();

        private void ConfirmAuth_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            vm.AuthPassword = PasswordBox.Password;
            PasswordBox.Password = string.Empty;
            vm.ConfirmAuthCommand.Execute(null);
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
