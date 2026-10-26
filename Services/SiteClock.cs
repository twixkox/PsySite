using Microsoft.Extensions.Options;

namespace PsySite.Services;

/// <summary>
/// Текущее время в часовом поясе сайта. Сервер (VPS) часто работает в UTC,
/// а расписание консультаций ведётся по Москве — поэтому время берём отсюда, а не из DateTime.Now.
/// </summary>
public class SiteClock(IOptions<SiteOptions> options)
{
    private readonly TimeZoneInfo _tz = FindZone(options.Value.TimeZone);

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

    public string ZoneName => _tz.Id;

    private static TimeZoneInfo FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
