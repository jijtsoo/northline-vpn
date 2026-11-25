using System;
using System.Security.Cryptography;
using System.Text;

namespace OpenVpnClient.Services
{
    public static class SecretBox
    {
        public static string Protect(string plain)
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string blob)
        {
            var protectedBytes = Convert.FromBase64String(blob);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
