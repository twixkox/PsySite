namespace PsySite;

public static class AppPaths
{
    /// <summary>
    /// Папка сайта — где лежат appsettings.json, wwwroot и App_Data.
    ///
    /// По умолчанию .NET считает ею ТЕКУЩУЮ папку, из которой запущена программа. Если запустить сайт
    /// из другой папки (ярлык, терминал, служба без WorkingDirectory), он не находил настройки, оставался
    /// без строки подключения и падал с непонятной ошибкой «no such table». Теперь, если в текущей папке
    /// нет appsettings.json, берётся папка, где лежит сама программа (PsySite.dll).
    /// </summary>
    public static string ResolveContentRoot()
    {
        var current = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(current, "appsettings.json")) ? current : AppContext.BaseDirectory;
    }
}
