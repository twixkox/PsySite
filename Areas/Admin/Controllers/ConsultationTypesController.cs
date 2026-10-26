using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;

namespace PsySite.Areas.Admin.Controllers;

/// <summary>Виды консультаций и цены.</summary>
public class ConsultationTypesController(AppDbContext db) : AdminControllerBase
{
    public async Task<IActionResult> Index() =>
        View(await db.ConsultationTypes.OrderByDescending(t => t.IsActive).ThenBy(t => t.Name).ToListAsync());

    public IActionResult Create() => View("Edit", new ConsultationType());

    public async Task<IActionResult> Edit(int id)
    {
        var type = await db.ConsultationTypes.FindAsync(id);
        return type is null ? NotFound() : View(type);
    }

    [HttpPost]
    public async Task<IActionResult> Save(ConsultationType model)
    {
        if (!ModelState.IsValid) return View("Edit", model);
        if (model.Id == 0) db.ConsultationTypes.Add(model); else db.ConsultationTypes.Update(model);
        await db.SaveChangesAsync();
        Flash("Сохранено");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var type = await db.ConsultationTypes.FindAsync(id);
        if (type is null) return NotFound();

        if (await db.Bookings.AnyAsync(b => b.ConsultationTypeId == id))
        {
            // На этот вид уже есть записи — удалять нельзя, иначе потеряется история. Просто скрываем.
            type.IsActive = false;
            Flash($"«{type.Name}» скрыт с сайта (по нему есть записи, поэтому он не удалён).");
        }
        else
        {
            db.ConsultationTypes.Remove(type);
            Flash($"«{type.Name}» удалён");
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
