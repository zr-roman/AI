using System.Security.Cryptography;
using System.Text;

namespace Crm.Core.Services;

/// <summary>В БД хранится только SHA-256 от API-ключа.</summary>
public static class ApiKeys
{
    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    /// <summary>Новый случайный ключ для выдачи пользователю.</summary>
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
