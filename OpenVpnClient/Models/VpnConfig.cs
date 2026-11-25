using System;

namespace OpenVpnClient.Models
{
    public class VpnConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool RequiresAuth { get; set; }
        public string? Username { get; set; }
        public string? PasswordBlob { get; set; }
        public DateTime AddedDate { get; set; } = DateTime.Now;

        public bool HasSavedAuth =>
            RequiresAuth &&
            !string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(PasswordBlob);
    }
}
