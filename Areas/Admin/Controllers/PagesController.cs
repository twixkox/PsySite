using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Areas.Admin.Controllers;

/// <summary>Текстовые страницы: «Обо мне», «Книга», «Контакты», документы.</summary>
public class PagesController(AppDbContext db, SiteClock clock) : AdminControllerBase
{
    /// <summary>Страницы, на которые ссылается код сайта — их можно редактировать, но не удалять.</summary>
    public static readonly string[] SystemSlugs =
        ["home", "consultations", "privacy-policy", "offer", "agreement", "cookies"];

    /// <summary>Адреса, которые уже заняты разделами сайта.</summary>
    private static readonly string[] ReservedSlugs =
        ["admin", "projects", "texts", "events", "webinars", "pay", "subscribe", "error", "css", "js", "img", "uploads"];

    public async Task<IActionResult> Index() =>
        View(await db.Pages.OrderByDescending(p => p.ShowInMenu).ThenBy(p => p.MenuOrder).ThenBy(p => p.Title).ToListAsync());

    public IActionResult Create() => View("Edit", new Page { MenuOrder = 90 });

    public async Task<IActionResult> Edit(int id)
    {
        var page = await db.Pages.FindAsync(id);
        return page is null ? NotFound() : View(page);
    }

    [HttpPost]
    public async Task<IActionResult> Save(Page model)
    {
        model.Slug = (model.Slug ?? "").Trim().ToLowerInvariant();

        if (model.Id != 0)
        {
            var old = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == model.Id);
            if (old is null) return NotFound();
            if (SystemSlugs.Contains(old.Slug) && old.Slug != model.Slug)
                ModelState.AddModelError(nameof(Page.Slug), "Адрес служебной страницы менять нельзя");
        }
        if (ReservedSlugs.Contains(model.Slug))
            ModelState.AddModelError(nameof(Page.Slug), "Этот адрес занят разделом сайта");
        if (await db.Pages.AnyAsync(p => p.Id != model.Id && p.Slug == model.Slug))
            ModelState.AddModelError(nameof(Page.Slug), "Страница с таким адресом уже есть");

        if (!ModelState.IsValid) return View("Edit", model);

        model.UpdatedAt = clock.Now;
        if (model.Id == 0) db.Pages.Add(model); else db.Pages.Update(model);
        await db.SaveChangesAsync();
        Flash("Сохранено");
        return RedirectToAction(nameof(Edit), new { id = model.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var page = await db.Pages.FindAsync(id);
        if (page is null) return NotFound();
        if (SystemSlugs.Contains(page.Slug))
        {
            FlashError("Это служебная страница, её нельзя удалить.");
            return RedirectToAction(nameof(Edit), new { id });
        }
        db.Pages.Remove(page);
        await db.SaveChangesAsync();
        Flash($"Страница «{page.Title}» удалена");
        return RedirectToAction(nameof(Index));
    }
}
