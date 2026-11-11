using System.Security.Cryptography;
using System.Text;

namespace Helinho.Application.Identidade;

/// <summary>Token opaco de 256 bits (P2). O banco guarda só o hash SHA-256.</summary>
public static class SessionTokens
{
    private const int TokenBytes = 32;

    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
