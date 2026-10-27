using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PsySite.Data;

/// <summary>Какая база данных используется (раздел "Database" в appsettings.json).</summary>
public enum DatabaseProvider { Sqlite, PostgreSql, SqlServer }

/// <summary>Сайт не смог подключиться к базе. Текст сообщения — подсказка, что проверить.</summary>
public class DatabaseStartupException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Подключение к базе данных.
///
/// В appsettings.json:
///   "Database": { "Provider": "Sqlite" }            — Sqlite | PostgreSql | SqlServer
///   "ConnectionStrings": { "Default": "..." }        — строка подключения к выбранной базе
///
/// Примеры строк подключения — в README, раздел «База данных».
/// </summary>
public static class DatabaseSetup
{
    public static DatabaseProvider ReadProvider(IConfiguration config)
    {
        var value = config["Database:Provider"];
        if (string.IsNullOrWhiteSpace(value)) return DatabaseProvider.Sqlite;
        return value.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgresql" or "postgres" or "npgsql" => DatabaseProvider.PostgreSql,
            "sqlserver" or "mssql" or "localdb" => DatabaseProvider.SqlServer,
            _ => throw new InvalidOperationException(
                $"Неизвестная база данных Database:Provider = «{value}». Допустимо: Sqlite, PostgreSql, SqlServer.")
        };
    }

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        var provider = ReadProvider(config);
        var cs = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Не задана строка подключения к базе: ConnectionStrings:Default в appsettings.json " +
                "(или переменная окружения ConnectionStrings__Default). " +
                $"Сайт искал настройки в папке: {contentRoot}");

        if (provider == DatabaseProvider.Sqlite) cs = PrepareSqlite(cs, contentRoot);

        services.AddDbContext<AppDbContext>(o =>
        {
            switch (provider)
            {
                case DatabaseProvider.Sqlite:
                    o.UseSqlite(cs);
                    break;
                case DatabaseProvider.PostgreSql:
                    o.UseNpgsql(cs, x => x.EnableRetryOnFailure(3));
                    break;
                case DatabaseProvider.SqlServer:
                    o.UseSqlServer(cs, x => x.EnableRetryOnFailure(3));
                    break;
            }
        });
        return services;
    }

    /// <summary>
    /// Путь к файлу SQLite в строке подключения считается от папки сайта, а не от текущей папки,
    /// и папка для файла создаётся заранее — иначе SQLite отвечает «unable to open database file».
    /// </summary>
    private static string PrepareSqlite(string cs, string contentRoot)
    {
        SqliteConnectionStringBuilder b;
        try { b = new SqliteConnectionStringBuilder(cs); }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Ошибка в строке подключения SQLite «{cs}»: {ex.Message} Правильный вид: Data Source=App_Data/site.db", ex);
        }
        if (string.IsNullOrWhiteSpace(b.DataSource) || b.DataSource == ":memory:" || b.Mode == SqliteOpenMode.Memory)
            return cs;
        if (!Path.IsPathRooted(b.DataSource))
            b.DataSource = Path.GetFullPath(Path.Combine(contentRoot, b.DataSource));
        var dir = Path.GetDirectoryName(b.DataSource);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return b.ToString();
    }

    /// <summary>Создать таблицы (если базы ещё нет) и заполнить примерами. Ошибку подключения объясняет по-русски.</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, Serilog.ILogger log)
    {
        var provider = ReadProvider(config);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        log.Information("База данных: {Provider}, {Target}", provider.ToString(), Describe(db));

        try
        {
            if (await db.Database.EnsureCreatedAsync())
                log.Information("Созданы таблицы в новой базе данных");
            await SeedData.EnsureSeededAsync(db);
        }
        catch (Exception ex)
        {
            throw new DatabaseStartupException($"Не удалось подключиться к базе данных ({provider}). {Hint(provider, ex)}", ex);
        }
    }

    /// <summary>Куда подключаемся — без пароля, чтобы его можно было спокойно писать в лог.</summary>
    private static string Describe(AppDbContext db)
    {
        var cs = db.Database.GetConnectionString() ?? "";
        try
        {
            var b = new DbConnectionStringBuilder { ConnectionString = cs };
            foreach (var key in new[] { "Password", "Pwd" })
                if (b.ContainsKey(key)) b[key] = "***";
            return b.ConnectionString;
        }
        catch { return "(строка подключения не разобрана)"; }
    }

    /// <summary>Подсказка по тексту ошибки: что именно проверить.</summary>
    private static string Hint(DatabaseProvider provider, Exception ex)
    {
        // Сначала — по коду ошибки: он не зависит от языка сообщений (SQL Server пишет их по-русски).
        foreach (var e in Unwrap(ex))
        {
            if (e is Npgsql.PostgresException pg)
                switch (pg.SqlState)
                {
                    case "28P01": case "28000": return "Неверный логин или пароль пользователя PostgreSQL (Username / Password в строке подключения).";
                    case "3D000": return "Такой базы на сервере PostgreSQL нет. Создайте её (см. инструкцию) или проверьте Database в строке подключения.";
                    case "42501": return "У пользователя PostgreSQL нет прав. Сделайте его владельцем базы: ALTER DATABASE psysite OWNER TO psysite;";
                }
            if (e is Microsoft.Data.SqlClient.SqlException ms)
                switch (ms.Number)
                {
                    case 18456: return "Неверный логин или пароль SQL Server (User Id / Password) или вход по паролю отключён на сервере.";
                    case 4060: return "Базы с таким именем нет или у пользователя нет к ней доступа (Database в строке подключения).";
                    case 262: case 1802: return "У пользователя нет права создавать базу. Создайте базу заранее вручную — таблицы сайт создаст сам.";
                    case -2: case 2: case 26: case 40: case 53: case 258: case -1:
                        return "Сервер SQL Server не найден или не запущен. Проверьте Server в строке подключения и что служба SQL Server (или LocalDB) запущена; " +
                               "для LocalDB: Server=(localdb)\\MSSQLLocalDB.";
                }
            if (e is System.Net.Sockets.SocketException)
                return "Сервер базы не отвечает. Проверьте, что он запущен, а адрес и порт в строке подключения верные " +
                       "(PostgreSQL: Host=localhost;Port=5432, SQL Server: Server=localhost,1433).";
        }

        var all = string.Join(" | ", Unwrap(ex).Select(e => e.Message)).ToLowerInvariant();
        bool Has(params string[] parts) => parts.Any(all.Contains);

        if (Has("password authentication failed", "login failed"))
            return "Неверный логин или пароль пользователя базы данных (Username / Password или User Id / Password в строке подключения).";
        if (Has("does not exist") && Has("database"))
            return "Такой базы на сервере нет. Создайте её (см. инструкцию) или проверьте имя Database в строке подключения.";
        if (Has("cannot open database"))
            return "База не найдена или у пользователя нет к ней доступа. Проверьте Database / Initial Catalog и права пользователя.";
        if (Has("permission denied to create database"))
            return "У пользователя нет права создавать базы. Создайте базу заранее вручную (см. инструкцию) — таблицы сайт создаст сам.";
        if (Has("permission denied"))
            return "У пользователя базы нет прав. Сделайте его владельцем базы: ALTER DATABASE psysite OWNER TO psysite;";
        if (Has("certificate chain", "certificate", "ssl", "сертификат"))
            return "Ошибка защищённого соединения. Для локального SQL Server добавьте в строку подключения TrustServerCertificate=True.";
        if (Has("connection refused", "failed to connect", "no such host", "network-related", "server was not found", "timeout", "could not connect"))
            return provider == DatabaseProvider.SqlServer
                ? "Сервер базы не найден или не запущен. Проверьте Server в строке подключения и что служба SQL Server (или LocalDB) запущена."
                : "Сервер базы не отвечает. Проверьте, что PostgreSQL запущен, а Host и Port в строке подключения верные (обычно localhost и 5432).";
        if (Has("unable to open database file", "readonly database", "read-only"))
            return "Нет доступа к файлу базы. Проверьте, что у пользователя, от которого запущен сайт, есть право записи в папку App_Data.";
        if (Has("no such table", "no such column", "invalid column name", "invalid object name"))
            return "Структура базы устарела или создана не до конца. Для SQLite: остановите сайт и удалите App_Data/site.db — он создастся заново. " +
                   "Для PostgreSQL / SQL Server: удалите и создайте базу заново (данные пропадут!) или добавьте недостающее вручную.";
        if (Has("format of the initialization string", "keyword not supported", "connection string"))
            return "Ошибка в строке подключения ConnectionStrings:Default — проверьте написание параметров и точки с запятой между ними.";
        return "Подробности — в тексте ошибки ниже.";
    }

    private static IEnumerable<Exception> Unwrap(Exception? e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }
}
