using Microsoft.Extensions.Options;

namespace PsySite.Services;

/// <summary>
/// Текущее время в часовом поясе сайта. Сервер (VPS) часто работает в UTC,
/// а расписание консультаций ведётся по Москве — поэтому время берём отсюда, а не из DateTime.Now.
/// </summary>
public class SiteClock(IOptions<SiteOptions> options, ILogger<SiteClock> log)
{
    private readonly TimeZoneInfo _tz = FindZone(options.Value.TimeZone, log);

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

    public string ZoneName => _tz.Id;

    /// <summary>
    /// Ищет часовой пояс и по имени Linux («Europe/Moscow»), и по имени Windows («Russian Standard Time»):
    /// старые Windows Server знают только второе. Если не нашлось ни одно — время сервера и предупреждение в журнале.
    /// </summary>
    private static TimeZoneInfo FindZone(string id, ILogger log)
    {
        var candidates = new List<string> { id };
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)) candidates.Add(windowsId);
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId)) candidates.Add(ianaId);
        if (id == "Europe/Moscow") candidates.Add("Russian Standard Time");

        foreach (var candidate in candidates.Distinct())
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        log.LogWarning("Часовой пояс «{TimeZone}» (Site:TimeZone) не найден — используется время сервера ({Local}). " +
                       "Проверьте расписание консультаций.", id, TimeZoneInfo.Local.Id);
        return TimeZoneInfo.Local;
    }
}
