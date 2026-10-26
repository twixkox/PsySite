namespace PsySite;

/// <summary>Общие настройки сайта (раздел "Site" в appsettings.json).</summary>
public class SiteOptions
{
    public string Name { get; set; } = "Имя Фамилия";
    public string Tagline { get; set; } = "Психолог. Автор. Преподаватель.";
    public string ContactEmail { get; set; } = "hello@example.com";

    /// <summary>Адрес сайта без слэша в конце, нужен для ссылок в письмах и возврата после оплаты.</summary>
    public string BaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>Часовой пояс, в котором указано расписание консультаций.</summary>
    public string TimeZone { get; set; } = "Europe/Moscow";

    /// <summary>За сколько часов до начала окно перестаёт показываться для записи.</summary>
    public int BookingMinHoursAhead { get; set; } = 12;

    /// <summary>Сколько минут неоплаченная запись держит окно, прежде чем оно снова освободится.</summary>
    public int UnpaidBookingHoldMinutes { get; set; } = 30;
}

/// <summary>Вход в админку (раздел "Admin").</summary>
public class AdminOptions
{
    public string Login { get; set; } = "admin";

    /// <summary>
    /// Пароль. В рабочей версии не храните его в appsettings.json,
    /// задайте через переменную окружения Admin__Password или dotnet user-secrets.
    /// </summary>
    public string Password { get; set; } = "";
}

/// <summary>Отправка писем (раздел "Smtp"). Если Host пустой, письма просто пишутся в лог.</summary>
public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "";
}

/// <summary>Платежи (раздел "Payments").</summary>
public class PaymentOptions
{
    /// <summary>"Fake" — тестовая оплата без денег, "YooKassa" — ЮKassa.</summary>
    public string Provider { get; set; } = "Fake";

    /// <summary>
    /// Разрешить тестовую оплату не в режиме разработки (только для тестового сервера!).
    /// На боевом сайте должно быть false — иначе консультацию можно «оплатить» без денег.
    /// </summary>
    public bool AllowFakeInProduction { get; set; }

    public YooKassaOptions YooKassa { get; set; } = new();
}

public class YooKassaOptions
{
    public string ShopId { get; set; } = "";
    public string SecretKey { get; set; } = "";
}
