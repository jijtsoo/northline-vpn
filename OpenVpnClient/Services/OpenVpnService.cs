using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenVpnClient.Services
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public class OpenVpnService
    {
        private Process? _process;
        private string? _authFilePath;
        private bool _userDisconnect;
        private readonly Queue<string> _recent = new();

        public event Action<ConnectionState, string>? StateChanged;
        public event Action<string>? LogReceived;

        public ConnectionState CurrentState { get; private set; } = ConnectionState.Disconnected;

        public async Task ConnectAsync(string ovpnPath, string? username = null, string? password = null)
        {
            if (CurrentState is ConnectionState.Connecting or ConnectionState.Connected)
                return;

            StopProcess(markUser: false);
            _recent.Clear();
            SetState(ConnectionState.Connecting, "Connecting...");

            try
            {
                var openVpnPath = FindOpenVpnExecutable();
                if (string.IsNullOrEmpty(openVpnPath))
                {
                    SetState(ConnectionState.Error, "OpenVPN Community is not installed.");
                    return;
                }

                var args = new StringBuilder();
                args.Append($"--config \"{ovpnPath}\" ");
                args.Append("--verb 3 ");

                if (!string.IsNullOrEmpty(username))
                {
                    _authFilePath = Path.Combine(Path.GetTempPath(), $"ovpn_auth_{Guid.NewGuid():N}.txt");
                    var auth = (username ?? "") + "\n" + (password ?? "") + "\n";
                    await File.WriteAllTextAsync(_authFilePath, auth, new UTF8Encoding(false));
                    args.Append($"--auth-user-pass \"{_authFilePath}\" ");
                }

                var workDir = Path.GetDirectoryName(ovpnPath) ?? "";
                _userDisconnect = false;

                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = openVpnPath,
                        Arguments = args.ToString(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true,
                        WorkingDirectory = workDir
                    },
                    EnableRaisingEvents = true
                };

                _process.OutputDataReceived += OnOutputDataReceived;
                _process.ErrorDataReceived += OnErrorDataReceived;
                _process.Exited += OnProcessExited;

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                CleanupAuthFile();
                SetState(ConnectionState.Error, ex.Message);
            }
        }

        public void Disconnect()
        {
            _userDisconnect = true;
            StopProcess(markUser: true);
            CleanupAuthFile();
            SetState(ConnectionState.Disconnected, "Disconnected");
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            PushLog(e.Data);

            var data = e.Data;
            if (data.Contains("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase))
            {
                SetState(ConnectionState.Connected, "Connected");
            }
            else if (data.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase))
            {
                SetState(ConnectionState.Error, "Wrong username or password");
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
                PushLog(e.Data);
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            int code = -1;
            try { if (_process != null) code = _process.ExitCode; } catch { }

            CleanupAuthFile();

            if (_userDisconnect || CurrentState == ConnectionState.Disconnected)
                return;

            var reason = LastMeaningfulLine();
            if (CurrentState == ConnectionState.Error && !string.IsNullOrWhiteSpace(reason))
            {
                SetState(ConnectionState.Error, reason);
                return;
            }

            if (code == 0)
            {
                SetState(ConnectionState.Disconnected, "Disconnected");
                return;
            }

            SetState(ConnectionState.Error, string.IsNullOrWhiteSpace(reason)
                ? $"OpenVPN exited (code {code})"
                : reason);
        }

        private void PushLog(string line)
        {
            lock (_recent)
            {
                _recent.Enqueue(line);
                while (_recent.Count > 40)
                    _recent.Dequeue();
            }
            LogReceived?.Invoke(line);
        }

        private string LastMeaningfulLine()
        {
            lock (_recent)
            {
                string last = "";
                foreach (var line in _recent)
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    if (t.StartsWith("Options error", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("AUTH_FAILED", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("Exiting", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("All tap-windows", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("There are no TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("Cannot load", StringComparison.OrdinalIgnoreCase))
                        last = t;
                    else
                        last = t;
                }
                return last.Length > 180 ? last[..180] : last;
            }
        }

        private void StopProcess(bool markUser)
        {
            if (markUser) _userDisconnect = true;
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    try { _process.StandardInput.Close(); } catch { }
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(1500);
                }
            }
            catch { }
            finally
            {
                try { _process?.Dispose(); } catch { }
                _process = null;
            }
        }

        private void SetState(ConnectionState state, string message)
        {
            CurrentState = state;
            StateChanged?.Invoke(state, message);
        }

        private void CleanupAuthFile()
        {
            if (!string.IsNullOrEmpty(_authFilePath) && File.Exists(_authFilePath))
            {
                try { File.Delete(_authFilePath); } catch { }
                _authFilePath = null;
            }
        }

        private static string? FindOpenVpnExecutable()
        {
            string[] possiblePaths =
            {
                @"C:\Program Files\OpenVPN\bin\openvpn.exe",
                @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "bin", "openvpn.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenVPN", "bin", "openvpn.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "openvpn.exe",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
                    return first;
            }
            catch { }

            return null;
        }
    }
}
