using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace COMTool.Core.Config;

/// <summary>
/// Windows DPAPI counterpart of COMTool/secure_store.py.
/// Tokens are base64 and are only decryptable by the same Windows user.
/// On other platforms both methods return an empty string, matching Python.
/// </summary>
public static class SecretProtector
{
    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static string Protect(string? password)
    {
        if (string.IsNullOrEmpty(password) || !IsSupported)
        {
            return "";
        }

        try
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (CryptographicException)
        {
            return "";
        }
        catch (PlatformNotSupportedException)
        {
            return "";
        }
    }

    public static string Unprotect(string? token)
    {
        if (string.IsNullOrEmpty(token) || !IsSupported)
        {
            return "";
        }

        try
        {
            var encrypted = Convert.FromBase64String(token);
            var decrypted = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (
            ex is CryptographicException
                or FormatException
                or PlatformNotSupportedException
                or DecoderFallbackException)
        {
            return "";
        }
    }
}
