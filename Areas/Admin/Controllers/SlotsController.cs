using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Areas.Admin.Controllers;

public class SlotsViewModel
{
    public List<Slot> Slots { get; set; } = [];
    public Dictionary<int, Booking> BookingsBySlot { get; set; } = [];
    public GenerateSlotsForm Generate { get; set; } = new();
    public bool ShowPast { get; set; }
}

/// <summary>Форма «создать окна на период».</summary>
public class GenerateSlotsForm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<DayOfWeek> Days { get; set; } = [];
    /// <summary>Время начала через запятую: «11:00, 14:00, 17:30».</summary>
    public string Times { get; set; } = "11:00, 14:00, 17:00";
    public int DurationMinutes { get; set; } = 60;
}

/// <summary>Расписание: свободные и занятые окна для записи.</summary>
public class SlotsController(AppDbContext db, SiteClock clock) : AdminControllerBase
{
    public async Task<IActionResult> Index(bool past = false)
    {
        var today = clock.Now.Date;
        var query = past
            ? db.Slots.Where(s => s.StartsAt < today).OrderByDescending(s => s.StartsAt).Take(300)
            : db.Slots.Where(s => s.StartsAt >= today).OrderBy(s => s.StartsAt);
        var slots = await query.ToListAsync();

        var ids = slots.Select(s => s.Id).ToList();
        var bookings = await db.Bookings.Include(b => b.ConsultationType)
            .Where(b => ids.Contains(b.SlotId) && b.Status != BookingStatus.Cancelled)
            .ToListAsync();

        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return View(new SlotsViewModel
        {
            Slots = slots,
            BookingsBySlot = bookings.GroupBy(b => b.SlotId).ToDictionary(g => g.Key, g => g.First()),
            ShowPast = past,
            Generate = new GenerateSlotsForm
            {
                From = monday.AddDays(7),
                To = monday.AddDays(13),
                Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Generate([Bind(Prefix = "Generate")] GenerateSlotsForm form)
    {
        var times = new List<TimeSpan>();
        foreach (var part in (form.Times ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TimeSpan.TryParseExact(part.Trim(), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var t))
                times.Add(t);
            else
            {
                FlashError($"Не понял время «{part}». Пишите так: 11:00, 14:30");
                return RedirectToAction(nameof(Index));
            }
        }

        if (times.Count == 0 || form.Days.Count == 0 || form.To < form.From || (form.To - form.From).TotalDays > 120
            || form.DurationMinutes is < 15 or > 480)
        {
            FlashError("Проверьте период (не больше 120 дней), дни недели, время и длительность.");
            return RedirectToAction(nameof(Index));
        }

        // Чтобы не создать дубли, пропускаем окна, которые пересекаются с уже существующими.
        var existing = await db.Slots
            .Where(s => s.StartsAt >= form.From.Date && s.StartsAt < form.To.Date.AddDays(1))
            .Select(s => new { s.StartsAt, s.DurationMinutes }).ToListAsync();

        var now = clock.Now;
        var created = 0;
        for (var day = form.From.Date; day <= form.To.Date; day = day.AddDays(1))
        {
            if (!form.Days.Contains(day.DayOfWeek)) continue;
            foreach (var t in times)
            {
                var start = day + t;
                var end = start.AddMinutes(form.DurationMinutes);
                if (start <= now) continue;
                if (existing.Any(e => start < e.StartsAt.AddMinutes(e.DurationMinutes) && e.StartsAt < end)) continue;

                db.Slots.Add(new Slot { StartsAt = start, DurationMinutes = form.DurationMinutes });
                existing.Add(new { StartsAt = start, form.DurationMinutes });
                created++;
            }
        }
        await db.SaveChangesAsync();
        Flash(created == 0 ? "Новых окон не добавлено: всё время уже есть в расписании." : $"Добавлено окон: {created}");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Add(DateTime startsAt, int durationMinutes = 60)
    {
        if (startsAt <= clock.Now)
        {
            FlashError("Нельзя добавить окно в прошлом.");
            return RedirectToAction(nameof(Index));
        }
        db.Slots.Add(new Slot { StartsAt = startsAt, DurationMinutes = Math.Clamp(durationMinutes, 15, 480) });
        await db.SaveChangesAsync();
        Flash($"Окно {startsAt:dd.MM HH:mm} добавлено");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var slot = await db.Slots.FindAsync(id);
        if (slot is null) return NotFound();
        if (await db.Bookings.AnyAsync(b => b.SlotId == id))
        {
            FlashError("На это окно была запись — его нельзя удалить. Отмените запись, и окно освободится.");
            return RedirectToAction(nameof(Index));
        }
        db.Slots.Remove(slot);
        await db.SaveChangesAsync();
        Flash("Окно удалено");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Удалить все свободные окна в выбранный день (например, если заболели или уезжаете).</summary>
    [HttpPost]
    public async Task<IActionResult> ClearDay(DateTime day)
    {
        var bookedSlotIds = db.Bookings.Select(b => b.SlotId);
        var removed = await db.Slots
            .Where(s => s.StartsAt >= day.Date && s.StartsAt < day.Date.AddDays(1) && !s.IsBooked && !bookedSlotIds.Contains(s.Id))
            .ExecuteDeleteAsync();
        Flash($"Удалено свободных окон: {removed}");
        return RedirectToAction(nameof(Index));
    }
}
