using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenVpnClient.Models;

namespace OpenVpnClient.Services
{
    public class ConfigManager
    {
        private static readonly HashSet<string> FileDirectives = new(StringComparer.OrdinalIgnoreCase)
        {
            "ca", "cert", "key", "dh", "pkcs12", "secret",
            "tls-auth", "tls-crypt", "tls-crypt-v2",
            "crl-verify", "extra-certs", "askpass"
        };

        private readonly string _configFolder;
        private readonly string _indexFile;
        private List<VpnConfig> _configs = new();

        public ConfigManager()
        {
            _configFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenVpnClient", "Configs");

            Directory.CreateDirectory(_configFolder);
            _indexFile = Path.Combine(_configFolder, "index.json");
            Load();
        }

        public IReadOnlyList<VpnConfig> GetAll() => _configs.AsReadOnly();

        public void Add(string ovpnFilePath, string? displayName = null)
        {
            if (!File.Exists(ovpnFilePath))
                throw new FileNotFoundException("Config file not found.");

            var sourceDir = Path.GetDirectoryName(ovpnFilePath) ?? "";
            var id = Guid.NewGuid().ToString();
            var destDir = Path.Combine(_configFolder, id);
            Directory.CreateDirectory(destDir);

            var raw = File.ReadAllText(ovpnFilePath);
            var requiresAuth = raw.Contains("auth-user-pass", StringComparison.OrdinalIgnoreCase);
            var rewritten = RewriteAndCopy(raw, sourceDir, destDir, ref requiresAuth);

            var destPath = Path.Combine(destDir, "profile.ovpn");
            File.WriteAllText(destPath, rewritten, new UTF8Encoding(false));

            _configs.Add(new VpnConfig
            {
                Id = id,
                Name = displayName ?? Path.GetFileNameWithoutExtension(ovpnFilePath),
                FilePath = destPath,
                RequiresAuth = requiresAuth
            });
            Save();
        }

        public void Remove(string id)
        {
            var config = _configs.FirstOrDefault(c => c.Id == id);
            if (config == null) return;

            var dir = Path.GetDirectoryName(config.FilePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            _configs.Remove(config);
            Save();
        }

        public void SaveCredentials(VpnConfig config, string username, string password)
        {
            config.Username = username;
            config.PasswordBlob = SecretBox.Protect(password);
            config.RequiresAuth = true;
            Save();
        }

        public void ClearCredentials(VpnConfig config)
        {
            config.Username = null;
            config.PasswordBlob = null;
            Save();
        }

        public string? ReadPassword(VpnConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.PasswordBlob))
                return null;

            try { return SecretBox.Unprotect(config.PasswordBlob); }
            catch { return null; }
        }

        private void Load()
        {
            if (!File.Exists(_indexFile)) return;
            try
            {
                var json = File.ReadAllText(_indexFile);
                _configs = JsonSerializer.Deserialize<List<VpnConfig>>(json) ?? new();
            }
            catch { _configs = new(); }
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_indexFile, json);
        }

        private static string RewriteAndCopy(string raw, string sourceDir, string destDir, ref bool requiresAuth)
        {
            var sb = new StringBuilder();
            foreach (var line in raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
                {
                    sb.AppendLine(line);
                    continue;
                }

                var space = trimmed.IndexOf(' ');
                var dir = space < 0 ? trimmed : trimmed.Substring(0, space);
                var rest = space < 0 ? "" : trimmed.Substring(space + 1).Trim();

                if (dir.Equals("auth-user-pass", StringComparison.OrdinalIgnoreCase))
                {
                    requiresAuth = true;
                    sb.AppendLine("auth-user-pass");
                    continue;
                }

                if (!FileDirectives.Contains(dir) || rest.Length == 0)
                {
                    sb.AppendLine(line);
                    continue;
                }

                var parts = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var referenced = parts[0].Trim('"', '\'');
                if (referenced.Equals("[inline]", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(line);
                    continue;
                }

                var full = Path.IsPathRooted(referenced) ? referenced : Path.Combine(sourceDir, referenced);
                if (File.Exists(full))
                {
                    var fileName = Path.GetFileName(full);
                    var target = Path.Combine(destDir, fileName);
                    if (!File.Exists(target))
                        File.Copy(full, target, false);
                    parts[0] = fileName;
                    sb.AppendLine(dir + " " + string.Join(" ", parts));
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }
    }
}
