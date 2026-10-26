using System.Security.Cryptography;
using System.Text;

namespace PsySite.Security;

/// <summary>Правила для пароля администратора.</summary>
public static class AdminPassword
{
    public const int MinLength = 10;
    public const string StampClaim = "pwd-stamp";

    /// <summary>
    /// Что не так с паролем (или null, если всё хорошо). На боевом сайте короткий пароль
    /// не принимается: его подбирают за минуты. При разработке можно любой.
    /// </summary>
    public static string? Problem(string? password, bool isDevelopment)
    {
        if (string.IsNullOrEmpty(password))
            return "Пароль администратора не задан.";
        if (!isDevelopment && password.Length < MinLength)
            return $"Пароль администратора слишком короткий: нужно не меньше {MinLength} символов.";
        return null;
    }

    /// <summary>
    /// «Отпечаток» пароля, который хранится в cookie входа. Сам пароль из него не восстановить.
    /// Если пароль сменили, отпечаток перестаёт совпадать — и все старые входы (в том числе
    /// чужие, если пароль утёк) сразу становятся недействительными.
    /// </summary>
    public static string Stamp(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("psysite-admin:" + password)))[..32];
}
