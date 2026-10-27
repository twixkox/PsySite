using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.Extensions.WebEncoders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite;
using PsySite.Data;
using PsySite.Logging;
using PsySite.Security;
using PsySite.Services;
using PsySite.Services.Payments;
using Serilog;
using Serilog.Events;

// Русские названия дней и месяцев, запятая в дробных числах (5000,50).
var ru = new CultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentCulture = ru;
CultureInfo.DefaultThreadCurrentUICulture = ru;

// Логи пишутся с самого начала: если запуск упадёт, причина будет в App_Data/logs/startup-*.log.
Log.Logger = LoggingSetup.CreateBootstrapLogger();

try
{
    // Папка сайта ищется так, чтобы он находил appsettings.json при запуске из любой папки (см. AppPaths).
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppPaths.ResolveContentRoot()
    });

    // ---------- Логирование (подробности — в Logging/LoggingSetup.cs) ----------
    builder.Services.Configure<LogOptions>(builder.Configuration.GetSection("Logs"));
    builder.Services.AddSerilog((services, lc) =>
        LoggingSetup.Configure(lc, builder.Configuration, builder.Environment.ContentRootPath, services));

    // ---------- Настройки из appsettings.json ----------
    builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
    builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
    builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
    builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payments"));

    // ---------- База данных: SQLite, PostgreSQL или SQL Server (см. Data/DatabaseSetup.cs) ----------
    var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
    Directory.CreateDirectory(dataDir);
    builder.Services.AddDatabase(builder.Configuration, builder.Environment.ContentRootPath);

    // Ключи шифрования cookie храним на диске, чтобы вход в админку не сбрасывался при перезапуске.
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

    // Не превращать кириллицу в &#x...; при выводе в HTML (всё равно безопасно — спецсимволы < > " & экранируются).
    builder.Services.Configure<WebEncoderOptions>(o =>
        o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

    // ---------- MVC + защита форм от подделки (CSRF) ----------
    builder.Services.AddControllersWithViews(o =>
        o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

    // ---------- Вход в админку по cookie ----------
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.LoginPath = "/admin/login";
            o.LogoutPath = "/admin/logout";
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            o.Cookie.Name = "psysite.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.Cookie.SameSite = SameSiteMode.Lax;
            // При каждом запросе проверяем, что пароль с момента входа не менялся.
            // Сменили пароль — все старые входы (и чужие, если пароль утёк) сразу недействительны.
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var current = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminOptions>>().Value.Password;
                var stamp = ctx.Principal?.FindFirst(AdminPassword.StampClaim)?.Value;
                if (string.IsNullOrEmpty(current) || stamp != AdminPassword.Stamp(current))
                {
                    ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Security")
                        .LogWarning("Вход в админку сброшен: пароль изменился, IP {ClientIp}", ctx.HttpContext.Connection.RemoteIpAddress);
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
        });
    builder.Services.AddAuthorization();

    // ---------- Ограничение частоты отправки форм (защита от спама) ----------
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy("forms", ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10) }));
        // Вход в админку — отдельно и строже: 10 попыток за 15 минут с одного адреса.
        o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));
        o.OnRejected = (ctx, _) =>
        {
            var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiter");
            log.LogWarning("Слишком много попыток с IP {ClientIp} ({Path}) — запрос отклонён",
                ctx.HttpContext.Connection.RemoteIpAddress, ctx.HttpContext.Request.Path);
            return ValueTask.CompletedTask;
        };
    });

    // ---------- Наши сервисы ----------
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
    builder.Services.AddScoped<OrderService>();
    builder.Services.AddScoped<BookingService>();
    builder.Services.AddSingleton<ImageStorage>();
    builder.Services.AddSingleton<SiteClock>();
    builder.Services.AddSingleton(new LogReader(LoggingSetup.LogDirectory(builder.Environment.ContentRootPath)));

    // Платёжные системы. Какая используется — задаётся в Payments:Provider.
    builder.Services.AddScoped<FakePaymentProvider>();
    builder.Services.AddHttpClient<YooKassaPaymentProvider>();
    builder.Services.AddScoped<IPaymentProvider>(sp =>
    {
        var name = builder.Configuration["Payments:Provider"] ?? "Fake";
        return name.Equals("YooKassa", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<YooKassaPaymentProvider>()
            : sp.GetRequiredService<FakePaymentProvider>();
    });

    // Не сообщать в ответах, на чём работает сайт (заголовок Server: Kestrel).
    builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

    var app = builder.Build();

    // ---------- Создаём базу и заполняем примерами при первом запуске ----------
    await DatabaseSetup.InitializeAsync(app.Services, builder.Configuration, Log.Logger);

    // ---------- Конвейер обработки запросов ----------
    // Нужно, когда сайт стоит за nginx: чтобы правильно определялись https и IP клиента.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/error");
        app.UseHsts();
    }
    app.UseSecurityHeaders(); // защитные заголовки браузеру — см. Security/SecurityHeaders.cs
    // Журнал запросов: одна строка на каждую страницу — адрес, код ответа, время, IP.
    // Ошибки сервера (500) пишутся с полным описанием исключения.
    app.UseSerilogRequestLogging(o =>
    {
        o.MessageTemplate = "{RequestMethod} {RequestPath} → {StatusCode} за {Elapsed:0} мс, IP {ClientIp}{UserSuffix}";
        o.GetLevel = (ctx, elapsedMs, ex) =>
        {
            // Показ страницы «Что-то пошло не так» после сбоя — сама ошибка уже записана строкой выше.
            if (ctx.Features.Get<IExceptionHandlerFeature>() is not null) return LogEventLevel.Verbose;
            var status = ctx.Response.StatusCode;
            if (ex is not null || status >= 500) return LogEventLevel.Error;
            // Картинки, стили и скрипты — их слишком много, в журнал не пишем (кроме ненайденных).
            if (Path.HasExtension(ctx.Request.Path.Value) && status < 400) return LogEventLevel.Verbose;
            if (status is 400 or 401 or 403) return LogEventLevel.Warning; // испорченная форма, попытка доступа
            if (elapsedMs > 3000) return LogEventLevel.Warning;            // медленная страница — повод разобраться
            return LogEventLevel.Information;
        };
        o.EnrichDiagnosticContext = (diag, ctx) =>
        {
            diag.Set("ClientIp", ctx.Connection.RemoteIpAddress?.ToString() ?? "?");
            var user = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
            diag.Set("UserSuffix", user is null ? "" : $", админ {user}");
            diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
        };
    });

    app.UseStatusCodePagesWithReExecute("/error/{0}");

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAreaControllerRoute(
        name: "admin",
        areaName: "Admin",
        pattern: "admin/{controller=Dashboard}/{action=Index}/{id?}");

    app.MapControllers(); // публичная часть описана атрибутами [Route] в контроллерах

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information("Сайт запущен: режим {Environment}, оплата {Provider}, адреса {Urls}",
            app.Environment.EnvironmentName, builder.Configuration["Payments:Provider"] ?? "Fake", string.Join(", ", app.Urls));

        // Проверка опасных настроек: пишем в журнал ошибок, чтобы это было видно в админке.
        var pwdProblem = AdminPassword.Problem(builder.Configuration["Admin:Password"], app.Environment.IsDevelopment());
        if (pwdProblem is not null) Log.Error("Небезопасная настройка: {Problem} Вход в админку закрыт.", pwdProblem);

        using var scope = app.Services.CreateScope();
        var fake = scope.ServiceProvider.GetRequiredService<IPaymentProvider>() as FakePaymentProvider;
        if (fake is { Enabled: false })
            Log.Error("Онлайн-оплата не подключена (Payments:Provider = Fake). Платные записи и материалы оплатить нельзя — только вручную в админке.");
        else if (fake is not null && !app.Environment.IsDevelopment())
            Log.Warning("Включена ТЕСТОВАЯ оплата без денег (Payments:AllowFakeInProduction). Не оставляйте так на боевом сайте!");
    });
    app.Lifetime.ApplicationStopping.Register(() => Log.Information("Сайт останавливается"));

    app.Run();
}
catch (DatabaseStartupException ex)
{
    // Понятная подсказка первой строкой, технические подробности — ниже.
    Log.Fatal(ex.InnerException, "Сайт не смог запуститься. {Hint}", ex.Message);
    Environment.ExitCode = 1;
}
catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionStrings") || ex.Message.Contains("Database:Provider") || ex.Message.Contains("строке подключения"))
{
    // Ошибка в настройках базы — достаточно понятного сообщения, без технических подробностей.
    Log.Fatal("Сайт не смог запуститься: {Reason}", ex.Message);
    Environment.ExitCode = 1;
}
catch (Exception ex) when (ex is not HostAbortedException) // HostAbortedException — штатная остановка при работе инструментов EF
{
    Log.Fatal(ex, "Сайт не смог запуститься: {Reason}", ex.Message);
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
