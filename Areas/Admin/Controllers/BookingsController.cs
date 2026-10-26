using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Areas.Admin.Controllers;

public class BookingDetailsViewModel
{
    public Booking Booking { get; set; } = null!;
    public List<Order> Orders { get; set; } = [];
    public List<Booking> OtherBookings { get; set; } = [];
}

/// <summary>Записи клиентов на консультации.</summary>
public class BookingsController(AppDbContext db, SiteClock clock, BookingService bookings) : AdminControllerBase
{
    /// <param name="filter">upcoming — предстоящие, past — прошедшие, cancelled — отменённые, all — все.</param>
    public async Task<IActionResult> Index(string filter = "upcoming", string? q = null)
    {
        await bookings.ReleaseExpiredAsync();
        var now = clock.Now;
        var query = db.Bookings.Include(b => b.Slot).Include(b => b.ConsultationType).AsQueryable();

        query = filter switch
        {
            "past" => query.Where(b => b.Slot!.StartsAt < now && b.Status != BookingStatus.Cancelled),
            "cancelled" => query.Where(b => b.Status == BookingStatus.Cancelled),
            "all" => query,
            _ => query.Where(b => b.Slot!.StartsAt >= now.Date && b.Status != BookingStatus.Cancelled)
        };
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(b => b.ClientName.Contains(q) || b.ClientEmail.Contains(q) || (b.ClientPhone != null && b.ClientPhone.Contains(q)));

        query = filter == "upcoming"
            ? query.OrderBy(b => b.Slot!.StartsAt)
            : query.OrderByDescending(b => b.Slot!.StartsAt);

        ViewData["Filter"] = filter;
        ViewData["Q"] = q;
        return View(await query.Take(500).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var b = await db.Bookings.Include(x => x.Slot).Include(x => x.ConsultationType).FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();

        return View(new BookingDetailsViewModel
        {
            Booking = b,
            Orders = await db.Orders.Where(o => o.Kind == OrderKind.Booking && o.ItemId == id).OrderByDescending(o => o.Id).ToListAsync(),
            OtherBookings = await db.Bookings.Include(x => x.Slot)
                .Where(x => x.ClientEmail == b.ClientEmail && x.Id != id)
                .OrderByDescending(x => x.Slot!.StartsAt).Take(20).ToListAsync()
        });
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, BookingStatus status)
    {
        var b = await db.Bookings.FindAsync(id);
        if (b is null) return NotFound();

        if (status == BookingStatus.Cancelled)
        {
            await bookings.CancelAsync(b);
            Flash("Запись отменена, время снова свободно. Если клиент оплатил — не забудьте вернуть деньги в кабинете платёжной системы.");
        }
        else if (b.Status == BookingStatus.Cancelled)
        {
            FlashError("Отменённую запись нельзя восстановить: время могли занять. Создайте новую запись на сайте.");
        }
        else
        {
            b.Status = status;
            await db.SaveChangesAsync();
            Flash("Статус обновлён");
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SaveNote(int id, string? adminNote)
    {
        var b = await db.Bookings.FindAsync(id);
        if (b is null) return NotFound();
        b.AdminNote = adminNote?.Trim();
        await db.SaveChangesAsync();
        Flash("Заметка сохранена");
        return RedirectToAction(nameof(Details), new { id });
    }
}
