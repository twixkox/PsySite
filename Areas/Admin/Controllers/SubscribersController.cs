using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;

namespace PsySite.Areas.Admin.Controllers;

/// <summary>Подписчики рассылки. Список можно выгрузить в CSV и загрузить в сервис рассылок.</summary>
public class SubscribersController(AppDbContext db, ILogger<SubscribersController> log) : AdminControllerBase
{
    public async Task<IActionResult> Index() =>
        View(await db.Subscribers.OrderByDescending(s => s.CreatedAt).ToListAsync());

    public async Task<IActionResult> Export()
    {
        var list = await db.Subscribers.OrderBy(s => s.CreatedAt).ToListAsync();
        log.LogInformation("Админка: {User} выгрузил(а) список подписчиков в CSV, адресов: {Count}", User.Identity?.Name, list.Count);
        var csv = new StringBuilder("email;date\n");
        foreach (var s in list) csv.Append(s.Email).Append(';').Append(s.CreatedAt.ToString("yyyy-MM-dd HH:mm")).Append('\n');

        // BOM, чтобы Excel правильно открыл файл.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"subscribers-{DateTime.Now:yyyy-MM-dd}.csv");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await db.Subscribers.Where(s => s.Id == id).ExecuteDeleteAsync();
        if (deleted > 0) Flash("Подписчик удалён");
        return RedirectToAction(nameof(Index));
    }
}
