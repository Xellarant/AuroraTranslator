#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Aurora.Content.Preparation;

internal static class ContentInputFingerprint
{
    // Explicitly records that no bytes were captured; this is not a SHA-256 digest.
    internal const string Unreadable = "unreadable";

    internal static bool Matches(string path, string expected)
    {
        try { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == expected; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return expected == Unreadable; }
    }
}
