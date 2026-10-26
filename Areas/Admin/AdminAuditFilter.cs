using System.Reflection;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PsySite.Areas.Admin;

/// <summary>
/// Журнал действий в админке: каждое изменение (любая отправка формы) записывается в лог —
/// кто, что сделал, с каким объектом и чем закончилось. Пример строки:
///   [INF] Админка: admin — удалён материал №7 «Сказки о силе» → выполнено
/// Тексты (заметки о клиентах, содержимое страниц) в лог не пишутся — только номера и названия.
/// </summary>
public class AdminAuditFilter(ILogger<AdminAuditFilter> log) : IAsyncActionFilter
{
    private static readonly Dictionary<string, string> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Articles/Save"] = "сохранён материал",
        ["Articles/Delete"] = "удалён материал",
        ["Pages/Save"] = "сохранена страница",
        ["Pages/Delete"] = "удалена страница",
        ["ConsultationTypes/Save"] = "сохранён формат консультации",
        ["ConsultationTypes/Delete"] = "удалён (или скрыт) формат консультации",
        ["Slots/Generate"] = "созданы окна в расписании",
        ["Slots/Add"] = "добавлено окно в расписании",
        ["Slots/Delete"] = "удалено окно в расписании",
        ["Slots/ClearDay"] = "убраны свободные окна за день",
        ["Bookings/SetStatus"] = "изменён статус записи",
        ["Bookings/SaveNote"] = "сохранена заметка к записи",
        ["Orders/MarkPaid"] = "заказ отмечен оплаченным вручную",
        ["Orders/MarkRefunded"] = "заказ отмечен как возврат",
        ["Subscribers/Delete"] = "удалён подписчик",
    };

    /// <summary>Параметры, которые нельзя писать в лог: там может быть текст о клиенте.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { "adminNote", "Body", "Comment", "password" };

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsPost(ctx.HttpContext.Request.Method))
        {
            await next();
            return;
        }

        var controller = ctx.RouteData.Values["controller"]?.ToString();
        var action = ctx.RouteData.Values["action"]?.ToString();
        var key = $"{controller}/{action}";
        var what = Actions.TryGetValue(key, out var name) ? name : key;
        var user = ctx.HttpContext.User.Identity?.Name ?? "?";
        var formIsValid = ctx.ModelState.IsValid;

        var executed = await next();

        // Сведения об объекте берём после выполнения: у нового материала номер появляется только при сохранении.
        var target = Describe(ctx);

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            log.LogError(executed.Exception, "Админка: {User} — {What}{Target} → ошибка", user, what, target);
            return;
        }

        var tempData = (executed.Controller as Microsoft.AspNetCore.Mvc.Controller)?.TempData;
        var refused = tempData?.Peek("Error") as string;

        if (!formIsValid || !ctx.ModelState.IsValid)
            log.LogInformation("Админка: {User} — {What}{Target} → не сохранено, ошибки в форме", user, what, target);
        else if (refused is not null)
            log.LogInformation("Админка: {User} — {What}{Target} → отказ: {Reason}", user, what, target, refused);
        else
            log.LogInformation("Админка: {User} — {What}{Target} → выполнено", user, what, target);
    }

    /// <summary>Номер, название и простые параметры действия: « №7 «Сказки о силе», status=Cancelled».</summary>
    private static string Describe(ActionExecutingContext ctx)
    {
        var parts = new List<string>();
        int? id = null;
        string? title = null;

        if (ctx.RouteData.Values.TryGetValue("id", out var rid) && int.TryParse(rid?.ToString(), out var parsed)) id = parsed;

        foreach (var (argName, value) in ctx.ActionArguments)
        {
            if (value is null || value is IFormFile || Hidden.Contains(argName)) continue;
            var type = value.GetType();

            if (IsSimple(type))
            {
                if (argName.Equals("id", StringComparison.OrdinalIgnoreCase)) { id ??= value as int?; continue; }
                if (value is string) continue; // строки не пишем — там может быть что угодно
                parts.Add($"{argName}={Format(value)}");
                continue;
            }

            // Модель формы: берём из неё Id и название.
            id ??= Get(value, "Id") as int?;
            title ??= (Get(value, "Title") ?? Get(value, "Name")) as string;

            // Форма генерации окон: период, дни и время — полезно видеть, что именно создано.
            foreach (var p in new[] { "From", "To", "Times", "DurationMinutes" })
                if (Get(value, p) is { } v) parts.Add($"{p}={Format(v)}");
            if (Get(value, "Days") is System.Collections.IEnumerable days and not string)
                parts.Add("Days=" + string.Join(",", days.Cast<object>()));
        }

        var head = (id is > 0 ? $" №{id}" : "") + (string.IsNullOrWhiteSpace(title) ? "" : $" «{Trim(title, 80)}»");
        return head + (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : "");
    }

    private static object? Get(object o, string prop) =>
        o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(o);

    private static bool IsSimple(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(Guid);

    private static string Format(object v) => v switch
    {
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("dd.MM.yyyy"),
        DateTime d => d.ToString("dd.MM.yyyy HH:mm"),
        string s => Trim(s, 60),
        _ => v.ToString() ?? ""
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
