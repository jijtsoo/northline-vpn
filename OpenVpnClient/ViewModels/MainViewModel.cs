using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenVpnClient.Models;
using OpenVpnClient.Services;

namespace OpenVpnClient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager = new();
        private readonly OpenVpnService _vpnService = new();
        private readonly DispatcherTimer _clock;
        private DateTime _connectedAt;

        [ObservableProperty] private ObservableCollection<VpnConfig> configs = new();
        [ObservableProperty] private ObservableCollection<string> logs = new();
        [ObservableProperty] private VpnConfig? selectedConfig;
        [ObservableProperty] private ConnectionState connectionState = ConnectionState.Disconnected;
        [ObservableProperty] private string statusMessage = "Disconnected";
        [ObservableProperty] private string statusDetail = "Choose a profile to connect";
        [ObservableProperty] private string durationText = "";
        [ObservableProperty] private bool isConnecting;
        [ObservableProperty] private bool isConnected;
        [ObservableProperty] private bool showAuthDialog;
        [ObservableProperty] private bool rememberLogin = true;
        [ObservableProperty] private bool showLog;
        [ObservableProperty] private string authUsername = string.Empty;
        [ObservableProperty] private string authPassword = string.Empty;

        public MainViewModel()
        {
            _vpnService.StateChanged += OnStateChanged;
            _vpnService.LogReceived += OnLog;
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
            {
                if (!IsConnected) return;
                var elapsed = DateTime.Now - _connectedAt;
                DurationText = elapsed.ToString(@"hh\:mm\:ss");
            };
            LoadConfigs();
        }

        public bool HasSelection => SelectedConfig != null;
        public bool SelectionHasSavedAuth => SelectedConfig?.HasSavedAuth == true;

        partial void OnSelectedConfigChanged(VpnConfig? value)
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionHasSavedAuth));
            if (!IsConnected && !IsConnecting && ConnectionState != ConnectionState.Error)
            {
                StatusDetail = value == null
                    ? "Choose a profile to connect"
                    : value.HasSavedAuth
                        ? $"Saved login \u00b7 {value.Username}"
                        : value.RequiresAuth ? "Login required" : "Ready to connect";
            }
        }

        private void LoadConfigs()
        {
            var keep = SelectedConfig?.Id;
            Configs.Clear();
            foreach (var c in _configManager.GetAll())
                Configs.Add(c);

            if (keep != null)
            {
                foreach (var c in Configs)
                {
                    if (c.Id == keep)
                    {
                        SelectedConfig = c;
                        return;
                    }
                }
            }

            SelectedConfig = Configs.Count > 0 ? Configs[0] : null;
        }

        private void OnLog(string line)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Logs.Add(line);
                while (Logs.Count > 200)
                    Logs.RemoveAt(0);
            });
        }

        private void OnStateChanged(ConnectionState state, string message)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                ConnectionState = state;
                StatusMessage = state switch
                {
                    ConnectionState.Connecting => "Connecting",
                    ConnectionState.Connected => "Connected",
                    ConnectionState.Error => "Connection failed",
                    _ => "Disconnected"
                };
                IsConnecting = state == ConnectionState.Connecting;
                IsConnected = state == ConnectionState.Connected;

                if (state == ConnectionState.Connected)
                {
                    _connectedAt = DateTime.Now;
                    DurationText = "00:00:00";
                    _clock.Start();
                    StatusDetail = SelectedConfig == null ? "Tunnel is up" : SelectedConfig.Name;
                }
                else
                {
                    _clock.Stop();
                    if (state != ConnectionState.Connecting)
                        DurationText = "";
                    StatusDetail = state switch
                    {
                        ConnectionState.Connecting => "Starting OpenVPN\u2026",
                        ConnectionState.Error => message,
                        _ => SelectedConfig == null ? "Choose a profile to connect" : "Tunnel is down"
                    };
                }
            });
        }

        [RelayCommand]
        private void AddConfig()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "OpenVPN Config (*.ovpn)|*.ovpn|All files (*.*)|*.*",
                Title = "Select OpenVPN configuration"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                _configManager.Add(dialog.FileName);
                LoadConfigs();
                if (Configs.Count > 0)
                    SelectedConfig = Configs[^1];
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Could not add profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void RemoveConfig(VpnConfig? config)
        {
            config ??= SelectedConfig;
            if (config == null) return;

            var answer = MessageBox.Show($"Remove \"{config.Name}\"?", "Remove profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            if (SelectedConfig?.Id == config.Id && (IsConnected || IsConnecting))
                _vpnService.Disconnect();

            _configManager.Remove(config.Id);
            LoadConfigs();
        }

        [RelayCommand]
        private void ForgetLogin()
        {
            if (SelectedConfig == null || !SelectedConfig.HasSavedAuth) return;
            _configManager.ClearCredentials(SelectedConfig);
            OnPropertyChanged(nameof(SelectionHasSavedAuth));
            StatusDetail = "Saved login removed";
        }

        [RelayCommand]
        private async Task Connect()
        {
            if (SelectedConfig == null)
            {
                MessageBox.Show("Select a profile first.", "No profile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsConnected || IsConnecting)
            {
                _vpnService.Disconnect();
                return;
            }

            if (SelectedConfig.RequiresAuth)
            {
                if (SelectedConfig.HasSavedAuth)
                {
                    var password = _configManager.ReadPassword(SelectedConfig);
                    if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(SelectedConfig.Username))
                    {
                        await _vpnService.ConnectAsync(SelectedConfig.FilePath, SelectedConfig.Username, password);
                        return;
                    }
                }

                AuthUsername = SelectedConfig.Username ?? string.Empty;
                AuthPassword = string.Empty;
                RememberLogin = true;
                ShowAuthDialog = true;
                return;
            }

            await _vpnService.ConnectAsync(SelectedConfig.FilePath);
        }

        [RelayCommand]
        private async Task ConfirmAuth()
        {
            if (SelectedConfig == null) return;
            if (string.IsNullOrWhiteSpace(AuthUsername) || string.IsNullOrWhiteSpace(AuthPassword))
            {
                MessageBox.Show("Username and password are required.", "Sign in", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (RememberLogin)
                _configManager.SaveCredentials(SelectedConfig, AuthUsername.Trim(), AuthPassword);

            ShowAuthDialog = false;
            OnPropertyChanged(nameof(SelectionHasSavedAuth));
            var user = AuthUsername.Trim();
            var pass = AuthPassword;
            AuthPassword = string.Empty;
            await _vpnService.ConnectAsync(SelectedConfig.FilePath, user, pass);
        }

        [RelayCommand]
        private void CancelAuth() => ShowAuthDialog = false;

        [RelayCommand]
        private void ToggleLog() => ShowLog = !ShowLog;
    }
}
