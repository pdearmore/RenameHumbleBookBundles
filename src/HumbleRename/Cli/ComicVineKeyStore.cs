using System.Security.Cryptography;
using System.Text;

namespace HumbleRename.Cli;

/// <summary>
/// Remembers the Comic Vine key the user pasted, between runs.
/// </summary>
/// <remarks>
/// It is a credential, so it is encrypted for the current Windows account with DPAPI —
/// the file is useless to another user or machine — and kept under %LOCALAPPDATA%
/// alongside the lookup cache, never in the app folder, so a portable exe stays clean and
/// the key does not travel with it.
/// </remarks>
public static class ComicVineKeyStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HumbleRenamer",
        "comicvine.key");

    /// <summary>Binds the blob to this app, so it is not interchangeable with other DPAPI data.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HumbleRenamer.ComicVineKey.v1");

    /// <summary>Returns the saved key, or null when none is stored or it cannot be read.</summary>
    public static string? Load()
    {
        // DPAPI is Windows-only; on anything else there is simply no stored key.
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain).Trim();
            return key.Length > 0 ? key : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // A missing, corrupt, or foreign-account key just means the user re-enters it.
            return null;
        }
    }

    /// <summary>Encrypts and stores the key, returning false if it could not be written.</summary>
    public static bool Save(string key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encrypted);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Removes any stored key. Best effort.</summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more we can do; the key simply stays until next time.
        }
    }
}
