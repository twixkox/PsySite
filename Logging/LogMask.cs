namespace PsySite.Logging;

/// <summary>
/// Скрывает персональные данные перед записью в лог.
/// Логи читают при разборе проблем, их пересылают и хранят в бэкапах — полные почты
/// и телефоны клиентов психолога там не нужны (152-ФЗ). По маске всё равно можно
/// сопоставить запись в логе с записью в админке.
/// </summary>
public static class LogMask
{
    /// <summary>anna.smirnova@example.com → an***@example.com</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "—";
        email = email.Trim();
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var visible = Math.Min(2, at);
        return email[..visible] + "***" + email[at..];
    }

    /// <summary>+7 912 345-67-89 → ***89</summary>
    public static string Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "—";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length <= 2 ? "***" : "***" + digits[^2..];
    }
}
