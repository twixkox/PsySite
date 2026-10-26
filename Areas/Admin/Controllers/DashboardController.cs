using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Logging;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Areas.Admin.Controllers;

public class DashboardViewModel
{
    public List<Booking> Upcoming { get; set; } = [];
    public int NewBookings { get; set; }
    public int FreeSlots { get; set; }
    public decimal PaidThisMonth { get; set; }
    public int PaidOrdersThisMonth { get; set; }
    public int Subscribers { get; set; }
    public List<Order> RecentOrders { get; set; } = [];
    public int ErrorsToday { get; set; }
}

public class DashboardController(AppDbContext db, SiteClock clock, BookingService bookings, LogReader logs) : AdminControllerBase
{
    public async Task<IActionResult> Index()
    {
        await bookings.ReleaseExpiredAsync();
        var now = clock.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        // SQLite не умеет суммировать decimal на стороне базы, поэтому складываем в C#.
        var paidAmounts = await db.Orders
            .Where(o => o.Status == OrderStatus.Paid && o.PaidAt >= monthStart)
            .Select(o => o.Amount).ToListAsync();

        var vm = new DashboardViewModel
        {
            Upcoming = await db.Bookings
                .Include(b => b.Slot).Include(b => b.ConsultationType)
                .Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.AwaitingPayment
                            && b.Slot!.StartsAt >= now.Date && b.Slot.StartsAt < now.Date.AddDays(8))
                .OrderBy(b => b.Slot!.StartsAt).ToListAsync(),
            NewBookings = await db.Bookings.CountAsync(b => b.Status == BookingStatus.New),
            FreeSlots = await db.Slots.CountAsync(s => !s.IsBooked && s.StartsAt > now),
            PaidThisMonth = paidAmounts.Sum(),
            PaidOrdersThisMonth = paidAmounts.Count,
            Subscribers = await db.Subscribers.CountAsync(),
            RecentOrders = await db.Orders.OrderByDescending(o => o.Id).Take(5).ToListAsync(),
            ErrorsToday = logs.CountTodayErrors()
        };
        return View(vm);
    }
}
