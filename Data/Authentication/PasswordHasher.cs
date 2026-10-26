using System.Security.Cryptography;
using System.Text;

namespace _20strike_ui.Data;

/// <summary>
/// SHA-256 password hashing used by the simulated authentication. Produces the uppercase hex
/// digest (no separators) that the simulated user store expects.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string plainText)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(plainText));
        return Convert.ToHexString(bytes);
    }
}