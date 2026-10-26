using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PsySite.Data;
using PsySite.Logging;
using PsySite.Models;

namespace PsySite.Services;

/// <summary>Создание заказов и обработка успешной оплаты.</summary>
public class OrderService(
    AppDbContext db, SiteClock clock, BookingService bookings, IEmailSender email,
    IOptions<SiteOptions> siteOptions, ILogger<OrderService> log)
{
    private readonly SiteOptions _site = siteOptions.Value;

    public async Task<Order> CreateForBookingAsync(Booking booking)
    {
        var type = booking.ConsultationType
            ?? await db.ConsultationTypes.FirstAsync(t => t.Id == booking.ConsultationTypeId);
        var slot = booking.Slot ?? await db.Slots.FirstAsync(s => s.Id == booking.SlotId);

        var order = new Order
        {
            Kind = OrderKind.Booking,
            ItemId = booking.Id,
            Amount = type.Price,
            Description = $"{type.Name}, {slot.StartsAt:dd.MM.yyyy HH:mm}",
            CustomerEmail = booking.ClientEmail,
            CustomerName = booking.ClientName,
            CreatedAt = clock.Now
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        log.LogInformation("Создан заказ №{OrderId} на {Amount:0.##} ₽ за запись №{BookingId}", order.Id, order.Amount, booking.Id);
        return order;
    }

    public async Task<Order> CreateForArticleAsync(Article article, string customerEmail, string? customerName)
    {
        var order = new Order
        {
            Kind = OrderKind.Article,
            ItemId = article.Id,
            Amount = article.Price ?? 0,
            Description = article.Title,
            CustomerEmail = customerEmail.Trim(),
            CustomerName = customerName?.Trim(),
            CreatedAt = clock.Now
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        log.LogInformation("Создан заказ №{OrderId} на {Amount:0.##} ₽: «{Title}», покупатель {Email}",
            order.Id, order.Amount, article.Title, LogMask.Email(order.CustomerEmail));
        return order;
    }

    /// <summary>
    /// Отметить заказ оплаченным и выполнить то, что за ним стоит.
    /// Безопасно вызывать повторно (платёжные системы иногда шлют уведомление несколько раз).
    /// </summary>
    public async Task MarkPaidAsync(Order order)
    {
        if (order.Status == OrderStatus.Paid)
        {
            log.LogDebug("Заказ №{OrderId} уже отмечен оплаченным — повторное подтверждение пропущено", order.Id);
            return;
        }

        order.Status = OrderStatus.Paid;
        order.PaidAt = clock.Now;
        await db.SaveChangesAsync();
        log.LogInformation("Заказ №{OrderId} оплачен: {Amount:0.##} ₽ через {Provider}", order.Id, order.Amount, order.Provider ?? "—");

        switch (order.Kind)
        {
            case OrderKind.Booking:
                var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == order.ItemId);

                if (booking is null)
                {
                    log.LogError("Оплачен заказ №{OrderId}, но запись №{BookingId} не найдена", order.Id, order.ItemId);
                    break;
                }
                if (booking.Status == BookingStatus.Cancelled)
                {
                    log.LogWarning("Оплата пришла по уже отменённой записи №{BookingId} (заказ №{OrderId}) — нужно связаться с клиентом",
                        booking.Id, order.Id);
                    // Оплатили уже после того, как бронь истекла. Окно могли занять — разбираемся вручную.
                    await email.SendAsync(_site.ContactEmail, "Оплата отменённой записи",
                        $"<p>Пришла оплата по заказу №{order.Id} для записи №{booking.Id}, но запись уже была отменена " +
                        "(клиент не успел оплатить вовремя). Свяжитесь с клиентом и подберите время или оформите возврат.</p>");
                    break;
                }
                if (booking.Status == BookingStatus.AwaitingPayment)
                {
                    booking.Status = BookingStatus.New;
                    await db.SaveChangesAsync();
                }
                await bookings.NotifyCreatedAsync(booking.Id, paid: true);
                break;

            case OrderKind.Article:
                var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == order.ItemId);
                var access = string.IsNullOrWhiteSpace(article?.PaidAccessUrl)
                    ? "<p>Мы пришлём подробности отдельным письмом.</p>"
                    : $"<p>Ваша ссылка: <a href=\"{WebUtility.HtmlEncode(article!.PaidAccessUrl)}\">{WebUtility.HtmlEncode(article.PaidAccessUrl)}</a></p>";

                await email.SendAsync(order.CustomerEmail, $"Оплата получена: {order.Description}",
                    $"<p>Спасибо за оплату «{WebUtility.HtmlEncode(order.Description)}».</p>{access}<p>{WebUtility.HtmlEncode(_site.Name)}</p>");
                await email.SendAsync(_site.ContactEmail, $"Оплата: {order.Description}",
                    $"<p>{WebUtility.HtmlEncode(order.CustomerName)} ({WebUtility.HtmlEncode(order.CustomerEmail)}) оплатил(а) «{WebUtility.HtmlEncode(order.Description)}» на {order.Amount:0.##} ₽.</p>");
                break;
        }
    }
}
