using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Logging;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Controllers;

/// <summary>Подписка на рассылку из формы в подвале сайта.</summary>
public class SubscribeController(AppDbContext db, SiteClock clock, ILogger<SubscribeController> log) : Controller
{
    [HttpPost("subscribe")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Subscribe(string? email, bool consent, string? website, string? returnUrl)
    {
        var ajax = Request.Headers.XRequestedWith == "fetch";
        string? error = null;

        if (!string.IsNullOrEmpty(website))
        {
            // бот — молча делаем вид, что всё хорошо
            log.LogWarning("Подписка: сработала ловушка для ботов, IP {ClientIp}", HttpContext.Connection.RemoteIpAddress);
        }
        else if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            error = "Проверьте адрес почты.";
        else if (!consent)
            error = "Нужно согласие на обработку данных.";
        else
        {
            var normalized = email.Trim().ToLowerInvariant();
            if (!await db.Subscribers.AnyAsync(s => s.Email == normalized))
            {
                db.Subscribers.Add(new Subscriber { Email = normalized, CreatedAt = clock.Now });
                await db.SaveChangesAsync();
                log.LogInformation("Новый подписчик рассылки: {Email}", LogMask.Email(normalized));
            }
            else
            {
                log.LogDebug("Повторная подписка: {Email} уже в списке", LogMask.Email(normalized));
            }
        }

        if (ajax) return Json(new { ok = error is null, error });

        TempData[error is null ? "Message" : "Error"] = error ?? "Спасибо! Вы подписались на рассылку.";
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
