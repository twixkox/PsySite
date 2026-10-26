using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Areas.Admin.Controllers;

/// <summary>Заказы и оплаты.</summary>
public class OrdersController(AppDbContext db, OrderService orders) : AdminControllerBase
{
    public async Task<IActionResult> Index(OrderStatus? status)
    {
        var query = db.Orders.AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);
        ViewData["Status"] = status;
        return View(await query.OrderByDescending(o => o.Id).Take(500).ToListAsync());
    }

    /// <summary>Отметить оплаченным вручную — например, если клиент перевёл деньги по реквизитам.</summary>
    [HttpPost]
    public async Task<IActionResult> MarkPaid(int id)
    {
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.Pending)
        {
            FlashError("Отметить оплаченным можно только заказ в статусе «Создан».");
            return RedirectToAction(nameof(Index));
        }
        order.Provider ??= "Вручную";
        await orders.MarkPaidAsync(order);
        Flash($"Заказ №{id} отмечен оплаченным, клиенту отправлено письмо.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Пометить, что деньги возвращены (сам возврат делается в кабинете платёжной системы).</summary>
    [HttpPost]
    public async Task<IActionResult> MarkRefunded(int id)
    {
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.Paid)
        {
            FlashError("Возврат можно отметить только у оплаченного заказа.");
            return RedirectToAction(nameof(Index));
        }
        order.Status = OrderStatus.Refunded;
        await db.SaveChangesAsync();
        Flash($"Заказ №{id} отмечен как возвращённый.");
        return RedirectToAction(nameof(Index));
    }
}
