using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;
using PsySite.Services.Payments;

namespace PsySite.Controllers;

public class PaymentResultViewModel
{
    public Order Order { get; set; } = null!;
    public Booking? Booking { get; set; }
    public Article? Article { get; set; }
}

[Route("pay")]
public class PaymentsController(
    AppDbContext db, IPaymentProvider provider, OrderService orders, ILogger<PaymentsController> log) : Controller
{
    /// <summary>Создаёт платёж и отправляет покупателя на страницу оплаты.</summary>
    [HttpGet("start/{id:guid}")]
    public async Task<IActionResult> Start(Guid id)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.PublicId == id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.Pending) return RedirectToAction(nameof(Result), new { id });

        try
        {
            var returnUrl = $"{Request.Scheme}://{Request.Host}/pay/result/{order.PublicId}";
            var result = await provider.StartAsync(order, returnUrl);
            order.Provider = provider.Name;
            order.ProviderPaymentId = result.ProviderPaymentId;
            await db.SaveChangesAsync();
            log.LogInformation("Заказ №{OrderId}: переход к оплате {Amount:0.##} ₽ через {Provider}, платёж {PaymentId}",
                order.Id, order.Amount, provider.Name, result.ProviderPaymentId);
            return Redirect(result.RedirectUrl);
        }
        catch (PaymentUnavailableException ex)
        {
            log.LogError("Заказ №{OrderId}: оплата невозможна — {Reason}", order.Id, ex.Message);
            TempData["Error"] = "Онлайн-оплата сейчас недоступна. Напишите нам на почту — мы поможем оплатить другим способом.";
            return RedirectToAction(nameof(Result), new { id });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Не удалось начать оплату заказа {Id}", order.Id);
            TempData["Error"] = "Платёжная система сейчас не отвечает. Попробуйте ещё раз через несколько минут.";
            return RedirectToAction(nameof(Result), new { id });
        }
    }

    /// <summary>Сюда платёжная система возвращает покупателя после оплаты.</summary>
    [HttpGet("result/{id:guid}")]
    public async Task<IActionResult> Result(Guid id)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.PublicId == id);
        if (order is null) return NotFound();

        // Уведомление от платёжной системы может прийти позже, чем вернётся покупатель, — проверяем сами.
        if (order.Status == OrderStatus.Pending && order.ProviderPaymentId is not null && order.Provider == provider.Name)
        {
            try
            {
                if (await provider.IsPaidAsync(order.ProviderPaymentId)) await orders.MarkPaidAsync(order);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Не удалось проверить статус платежа {Id}", order.ProviderPaymentId);
            }
        }

        var vm = new PaymentResultViewModel { Order = order };
        if (order.Kind == OrderKind.Booking)
            vm.Booking = await db.Bookings.Include(b => b.Slot).Include(b => b.ConsultationType)
                .FirstOrDefaultAsync(b => b.Id == order.ItemId);
        else
            vm.Article = await db.Articles.FirstOrDefaultAsync(a => a.Id == order.ItemId);

        return View(vm);
    }

    /// <summary>Адрес для уведомлений платёжной системы (указывается в её личном кабинете).</summary>
    [HttpPost("webhook")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook()
    {
        try
        {
            var paymentId = await provider.ReadWebhookPaymentIdAsync(Request);
            if (paymentId is null)
            {
                log.LogWarning("Уведомление об оплате без номера платежа (IP {ClientIp}) — пропущено", HttpContext.Connection.RemoteIpAddress);
                return Ok();
            }
            log.LogInformation("Уведомление от {Provider} о платеже {PaymentId}", provider.Name, paymentId);

            var order = await db.Orders.FirstOrDefaultAsync(o => o.ProviderPaymentId == paymentId);
            if (order is null)
            {
                log.LogWarning("Уведомление о неизвестном платеже {Id}", paymentId);
                return Ok();
            }

            if (await provider.IsPaidAsync(paymentId)) await orders.MarkPaidAsync(order);
            else log.LogInformation("Платёж {PaymentId} по заказу №{OrderId} пока не оплачен", paymentId, order.Id);
            return Ok();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Ошибка обработки уведомления об оплате");
            // 500 — платёжная система повторит уведомление позже.
            return StatusCode(500);
        }
    }

    // ---------- Тестовая оплата (работает, только если Payments:Provider = "Fake") ----------

    [HttpGet("fake/{id:guid}")]
    public async Task<IActionResult> Fake(Guid id)
    {
        if (provider is not FakePaymentProvider { Enabled: true }) return NotFound();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.PublicId == id);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost("fake/{id:guid}")]
    public async Task<IActionResult> Fake(Guid id, bool pay)
    {
        if (provider is not FakePaymentProvider { Enabled: true })
        {
            log.LogWarning("Попытка тестовой оплаты заказа {OrderId} при выключенной тестовой оплате, IP {ClientIp}",
                id, HttpContext.Connection.RemoteIpAddress);
            return NotFound();
        }
        var order = await db.Orders.FirstOrDefaultAsync(o => o.PublicId == id);
        if (order is null) return NotFound();

        if (pay) await orders.MarkPaidAsync(order);
        else log.LogInformation("Тестовая оплата заказа №{OrderId}: покупатель вернулся без оплаты", order.Id);
        return RedirectToAction(nameof(Result), new { id });
    }
}
