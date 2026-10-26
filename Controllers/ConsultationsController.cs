using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Controllers;

/// <summary>Поля формы записи на консультацию.</summary>
public class BookingForm
{
    [Range(1, int.MaxValue, ErrorMessage = "Выберите формат консультации")]
    public int ConsultationTypeId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Выберите удобное время")]
    public int SlotId { get; set; }

    [Required(ErrorMessage = "Укажите имя")]
    [StringLength(200)]
    [Display(Name = "Имя")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Укажите почту")]
    [EmailAddress(ErrorMessage = "Похоже, в адресе почты ошибка")]
    [StringLength(200)]
    [Display(Name = "Почта")]
    public string Email { get; set; } = "";

    [StringLength(50)]
    [Display(Name = "Телефон")]
    public string? Phone { get; set; }

    [StringLength(200)]
    [Display(Name = "Как удобнее связаться")]
    public string? ContactMethod { get; set; }

    [StringLength(2000)]
    [Display(Name = "С чем хотите прийти (по желанию)")]
    public string? Comment { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Нужно согласие с офертой и обработкой данных")]
    public bool Consent { get; set; }

    /// <summary>Ловушка для ботов.</summary>
    public string? Website { get; set; }
}

public class ConsultationsViewModel
{
    public Page? Intro { get; set; }
    public List<ConsultationType> Types { get; set; } = [];
    public List<Slot> Slots { get; set; } = [];
    public BookingForm Form { get; set; } = new();
    public string? Error { get; set; }
}

[Route("consultations")]
public class ConsultationsController(AppDbContext db, BookingService bookings, OrderService orders, ILogger<ConsultationsController> log) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? type) =>
        View(await BuildAsync(new BookingForm { ConsultationTypeId = type ?? 0 }));

    [HttpPost("")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Index([Bind(Prefix = "Form")] BookingForm form)
    {
        if (!string.IsNullOrEmpty(form.Website))
        {
            log.LogWarning("Запись: сработала ловушка для ботов, IP {ClientIp}", HttpContext.Connection.RemoteIpAddress);
            return RedirectToAction(nameof(Thanks));
        }
        if (!ModelState.IsValid) return View(await BuildAsync(form));

        var (booking, error) = await bookings.CreateAsync(new BookingRequest(
            form.ConsultationTypeId, form.SlotId, form.Name, form.Email,
            form.Phone, form.ContactMethod, form.Comment));

        if (booking is null)
        {
            form.SlotId = 0;
            return View(await BuildAsync(form, error));
        }

        if (booking.Status == BookingStatus.AwaitingPayment)
        {
            var order = await orders.CreateForBookingAsync(booking);
            return RedirectToAction("Start", "Payments", new { id = order.PublicId });
        }
        return RedirectToAction(nameof(Thanks));
    }

    [HttpGet("thanks")]
    public IActionResult Thanks() => View();

    private async Task<ConsultationsViewModel> BuildAsync(BookingForm form, string? error = null)
    {
        var types = await db.ConsultationTypes.Where(t => t.IsActive).ToListAsync();
        if (form.ConsultationTypeId == 0 && types.Count > 0) form.ConsultationTypeId = types[0].Id;
        return new ConsultationsViewModel
        {
            Intro = await db.Pages.FirstOrDefaultAsync(p => p.Slug == "consultations"),
            Types = types,
            Slots = await bookings.GetAvailableSlotsAsync(),
            Form = form,
            Error = error
        };
    }
}
