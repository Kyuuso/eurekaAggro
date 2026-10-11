using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace EurekaSuite.Configuration;

/// <summary>
/// Encrypts small secrets (tracker edit passwords) for storage in the plugin configuration file using
/// Windows DPAPI, scoped to the current Windows user. Calls crypt32.dll directly so the plugin does not
/// need to ship the System.Security.Cryptography.ProtectedData package.
/// </summary>
internal static class SecretProtector
{
    private const string Prefix = "dpapi:";
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EurekaSuite.TrackerPassword");

    // Config saves happen often; re-encrypting unchanged passwords every time is unnecessary
    private static readonly ConcurrentDictionary<string, string> ProtectedCache = new(StringComparer.Ordinal);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>
    /// Returns the encrypted, prefixed form of a secret. If DPAPI is unavailable the plain value is
    /// returned so the password is not lost.
    /// </summary>
    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        if (ProtectedCache.TryGetValue(plain, out var cached)) return cached;

        var encrypted = Transform(Encoding.UTF8.GetBytes(plain), protect: true);
        if (encrypted == null) return plain;

        var result = Prefix + Convert.ToBase64String(encrypted);
        ProtectedCache[plain] = result;
        return result;
    }

    /// <summary>
    /// Decrypts a value written by <see cref="Protect"/>. Values without the prefix are treated as
    /// legacy plain text. Returns an empty string if the value was encrypted by another Windows user or machine.
    /// </summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;

        try
        {
            var decrypted = Transform(Convert.FromBase64String(stored.Substring(Prefix.Length)), protect: false);
            if (decrypted == null) return string.Empty;

            var plain = Encoding.UTF8.GetString(decrypted);
            ProtectedCache[plain] = stored;
            return plain;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static byte[]? Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var inputBlob = new DataBlob { Length = input.Length, Data = inputHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { Length = Entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };

            DataBlob outputBlob;
            bool ok = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);

            if (!ok || outputBlob.Data == IntPtr.Zero) return null;

            try
            {
                var output = new byte[outputBlob.Length];
                Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Length);
                return output;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            inputHandle.Free();
            entropyHandle.Free();
        }
    }
}
