using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PsySite.Logging;

/// <summary>Настройки логов (раздел "Logs" в appsettings.json).</summary>
public class LogOptions
{
    /// <summary>Сколько дней хранить общий журнал.</summary>
    public int RetainedDays { get; set; } = 30;

    /// <summary>Сколько дней хранить журнал ошибок (он маленький, поэтому дольше).</summary>
    public int ErrorRetainedDays { get; set; } = 90;

    /// <summary>Максимальный размер одного файла, МБ. Если файл больше — начинается новый.</summary>
    public int FileSizeLimitMb { get; set; } = 50;
}

/// <summary>
/// Где и как пишутся логи.
///
/// Файлы лежат в App_Data/logs:
///   site-20261026.log   — всё подряд: запросы, записи, оплаты, письма, действия в админке, ошибки;
///   errors-20261026.log — только предупреждения и ошибки, чтобы быстро найти проблему.
/// Каждый день начинается новый файл, старые удаляются автоматически.
/// Уровни подробности (что писать, а что нет) задаются в appsettings.json, раздел "Serilog".
/// </summary>
public static class LoggingSetup
{
    public const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Src}: {Message:lj}{NewLine}{Exception}";

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Src}: {Message:lj}{NewLine}{Exception}";

    public static string LogDirectory(string contentRoot) => Path.Combine(contentRoot, "App_Data", "logs");

    /// <summary>
    /// Временный логгер на время запуска: если сайт упадёт ещё до полной настройки
    /// (например, ошибка в appsettings.json), причина всё равно попадёт в файл startup-*.log.
    /// </summary>
    public static Serilog.ILogger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .Enrich.With<ShortSourceEnricher>()
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .WriteTo.File(
                Path.Combine(LogDirectory(Directory.GetCurrentDirectory()), "startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                outputTemplate: FileTemplate)
            .CreateBootstrapLogger();

    /// <summary>Основная настройка логов.</summary>
    public static void Configure(LoggerConfiguration lc, IConfiguration config, string contentRoot, IServiceProvider services)
    {
        var o = config.GetSection("Logs").Get<LogOptions>() ?? new LogOptions();
        var dir = LogDirectory(contentRoot);
        var sizeLimit = (long)Math.Max(1, o.FileSizeLimitMb) * 1024 * 1024;

        lc.ReadFrom.Configuration(config)   // уровни из раздела "Serilog"
          .ReadFrom.Services(services)
          .Enrich.FromLogContext()
          .Enrich.With<ShortSourceEnricher>()
          .WriteTo.Console(outputTemplate: ConsoleTemplate)
          .WriteTo.File(
              Path.Combine(dir, "site-.log"),
              rollingInterval: RollingInterval.Day,
              retainedFileCountLimit: o.RetainedDays,
              fileSizeLimitBytes: sizeLimit,
              rollOnFileSizeLimit: true,
              outputTemplate: FileTemplate)
          .WriteTo.File(
              Path.Combine(dir, "errors-.log"),
              restrictedToMinimumLevel: LogEventLevel.Warning,
              rollingInterval: RollingInterval.Day,
              retainedFileCountLimit: o.ErrorRetainedDays,
              fileSizeLimitBytes: sizeLimit,
              rollOnFileSizeLimit: true,
              outputTemplate: FileTemplate);
    }
}

/// <summary>
/// Делает подпись источника короткой: вместо «PsySite.Services.BookingService» — «BookingService»,
/// а журнал HTTP-запросов подписывается просто «HTTP».
/// </summary>
public class ShortSourceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent e, ILogEventPropertyFactory f)
    {
        var src = "Сайт";
        if (e.Properties.TryGetValue("SourceContext", out var v) && v is ScalarValue { Value: string s })
        {
            src = s == "Serilog.AspNetCore.RequestLoggingMiddleware" ? "HTTP" : s[(s.LastIndexOf('.') + 1)..];
            var generic = src.IndexOf('`');
            if (generic > 0) src = src[..generic];
        }
        e.AddOrUpdateProperty(f.CreateProperty("Src", src));
    }
}
