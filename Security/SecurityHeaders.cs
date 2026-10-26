namespace PsySite.Security;

/// <summary>
/// Заголовки безопасности для каждого ответа сайта. Они говорят браузеру:
/// - Content-Security-Policy — откуда можно загружать скрипты, стили, шрифты и картинки.
///   Если на страницу всё-таки попадёт чужой скрипт (например, через текст), браузер его не выполнит.
/// - X-Frame-Options / frame-ancestors — сайт нельзя встроить в чужую страницу
///   (защита от «кликджекинга»: невидимая админка поверх чужой кнопки).
/// - X-Content-Type-Options — не угадывать тип файла: загруженная «картинка» не исполнится как страница.
/// - Referrer-Policy — не передавать чужим сайтам полный адрес страницы (в нём бывает номер заказа).
/// - Permissions-Policy — сайту не нужны камера, микрофон и геолокация.
///
/// Если добавите на сайт внешний сервис (виджет, счётчик, видео), допишите его адрес в нужный пункт Csp ниже.
/// </summary>
public static class SecurityHeaders
{
    private static readonly string Csp = string.Join("; ",
        "default-src 'self'",
        "script-src 'self' https://cdn.jsdelivr.net",                                  // редактор Quill в админке
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net",
        "font-src 'self' https://fonts.gstatic.com data:",
        "img-src 'self' data: https:",
        "frame-src https:",                                                             // видео YouTube / VK в текстах
        "connect-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "frame-ancestors 'none'");

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["Content-Security-Policy"] = Csp;
            h["X-Frame-Options"] = "DENY";
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            await next();
        });
}
