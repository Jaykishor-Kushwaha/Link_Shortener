using System.Security.Cryptography;

namespace UrlShortener.Api.Services;

/// <summary>
/// Generates cryptographically random short codes.
/// Uses RandomNumberGenerator for unguessable, URL-safe codes.
/// Codes are 8 characters from a 64-char alphabet = ~48 bits of entropy.
/// Collision resistance is enforced at the database level via unique index, not here.
/// </summary>
public static class ShortCodeGenerator
{
    // URL-safe alphabet: A-Z, a-z, 0-9, -, _  (64 characters = 6 bits per char)
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    private const int CodeLength = 8;

    public static string Generate()
    {
        Span<byte> randomBytes = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(randomBytes);

        Span<char> code = stackalloc char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
        {
            // Mask to 6 bits (0-63) to index into the 64-char alphabet with no bias
            code[i] = Alphabet[randomBytes[i] & 0x3F];
        }

        return new string(code);
    }
}
