using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsySite.Data;
using PsySite.Logging;
using PsySite.Models;

namespace PsySite.Services;

/// <summary>Данные, которые клиент вводит в форме записи.</summary>
public record BookingRequest(
    int ConsultationTypeId, int SlotId, string Name, string Email,
    string? Phone, string? ContactMethod, string? Comment);

/// <summary>Логика записи на консультацию: свободные окна, бронирование, отмена, письма.</summary>
public class BookingService(
    AppDbContext db, SiteClock clock, IEmailSender email,
    IOptions<SiteOptions> siteOptions, ILogger<BookingService> log)
{
    private readonly SiteOptions _site = siteOptions.Value;

    /// <summary>Свободные окна, на которые ещё можно записаться.</summary>
    public async Task<List<Slot>> GetAvailableSlotsAsync()
    {
        await ReleaseExpiredAsync();
        var from = clock.Now.AddHours(_site.BookingMinHoursAhead);
        return await db.Slots
            .Where(s => !s.IsBooked && s.StartsAt >= from)
            .OrderBy(s => s.StartsAt)
            .ToListAsync();
    }

    /// <summary>
    /// Создать запись. Окно занимается атомарно: если два человека нажмут «Записаться» одновременно,
    /// окно получит только один, второй увидит сообщение об ошибке.
    /// </summary>
    public async Task<(Booking? Booking, string? Error)> CreateAsync(BookingRequest r)
    {
        var type = await db.ConsultationTypes.FirstOrDefaultAsync(t => t.Id == r.ConsultationTypeId && t.IsActive);
        if (type is null)
        {
            log.LogWarning("Запись отклонена: формат консультации {TypeId} не найден или скрыт", r.ConsultationTypeId);
            return (null, "Выбранный вид консультации недоступен.");
        }

        var minStart = clock.Now.AddHours(_site.BookingMinHoursAhead);
        var claimed = await db.Slots
            .Where(s => s.Id == r.SlotId && !s.IsBooked && s.StartsAt >= minStart)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, true));
        if (claimed == 0)
        {
            log.LogInformation("Запись отклонена: окно {SlotId} уже занято или слишком близко по времени", r.SlotId);
            return (null, "Это время только что заняли. Пожалуйста, выберите другое.");
        }

        var needsPayment = type.Price > 0 && type.RequirePrepayment;
        var booking = new Booking
        {
            ConsultationTypeId = type.Id,
            SlotId = r.SlotId,
            ClientName = r.Name.Trim(),
            ClientEmail = r.Email.Trim(),
            ClientPhone = r.Phone?.Trim(),
            ContactMethod = r.ContactMethod?.Trim(),
            Comment = r.Comment?.Trim(),
            Status = needsPayment ? BookingStatus.AwaitingPayment : BookingStatus.New,
            CreatedAt = clock.Now
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var slot = await db.Slots.AsNoTracking().FirstAsync(s => s.Id == r.SlotId);
        log.LogInformation("Новая запись №{BookingId}: {Type}, {StartsAt:dd.MM.yyyy HH:mm}, клиент {Email}, {Status}",
            booking.Id, type.Name, slot.StartsAt, LogMask.Email(booking.ClientEmail),
            needsPayment ? "ждёт оплаты" : "без предоплаты");

        if (!needsPayment) await NotifyCreatedAsync(booking.Id);
        return (booking, null);
    }

    /// <summary>Отменить запись и освободить окно.</summary>
    public async Task CancelAsync(Booking booking)
    {
        if (booking.Status == BookingStatus.Cancelled) return;
        booking.Status = BookingStatus.Cancelled;
        await db.Slots.Where(s => s.Id == booking.SlotId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBooked, false));
        await db.SaveChangesAsync();
        log.LogInformation("Запись №{BookingId} отменена, окно {SlotId} снова свободно", booking.Id, booking.SlotId);
    }

    /// <summary>Неоплаченные записи старше N минут отменяются, а их окна снова становятся свободными.</summary>
    public async Task ReleaseExpiredAsync()
    {
        var deadline = clock.Now.AddMinutes(-_site.UnpaidBookingHoldMinutes);
        var expired = await db.Bookings
            .Where(b => b.Status == BookingStatus.AwaitingPayment && b.CreatedAt < deadline)
            .ToListAsync();
        if (expired.Count == 0) return;

        foreach (var b in expired)
        {
            await CancelAsync(b);
            await db.Orders
                .Where(o => o.Kind == OrderKind.Booking && o.ItemId == b.Id && o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, OrderStatus.Cancelled));
        }
        log.LogInformation("Не оплачены вовремя и отменены записи: {BookingIds}", expired.Select(b => b.Id));
    }

    /// <summary>Письма клиенту и психологу о новой записи.</summary>
    public async Task NotifyCreatedAsync(int bookingId, bool paid = false)
    {
        var b = await db.Bookings
            .Include(x => x.Slot).Include(x => x.ConsultationType)
            .FirstAsync(x => x.Id == bookingId);

        var when = b.Slot!.StartsAt.ToString("dddd, d MMMM yyyy, HH:mm");
        var what = H(b.ConsultationType!.Name);
        var paidLine = paid ? "<p>Оплата получена, спасибо!</p>" : "";

        await email.SendAsync(b.ClientEmail, "Вы записаны на консультацию",
            $"""
            <p>Здравствуйте, {H(b.ClientName)}!</p>
            <p>Вы записаны: <b>{what}</b>, {when} (время московское).</p>
            {paidLine}
            <p>Мы свяжемся с вами накануне, чтобы прислать ссылку или адрес.
            Если планы изменятся, просто ответьте на это письмо.</p>
            <p>{H(_site.Name)}</p>
            """);

        await email.SendAsync(_site.ContactEmail, $"Новая запись №{b.Id}, {when}",
            $"""
            <p><b>{what}</b>, {when}{(paid ? ", <b>оплачено</b>" : "")}</p>
            <p>Клиент: {H(b.ClientName)}<br>
            Почта: {H(b.ClientEmail)}<br>
            Телефон: {H(b.ClientPhone)}<br>
            Связь: {H(b.ContactMethod)}</p>
            <p>Комментарий:<br>{H(b.Comment)}</p>
            <p><a href="{_site.BaseUrl}/admin/bookings/details/{b.Id}">Открыть в админке</a></p>
            """);
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "—");
}
