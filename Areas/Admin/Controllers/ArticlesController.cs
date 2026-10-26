using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Areas.Admin.Controllers;

/// <summary>Проекты, тексты, события и записи вебинаров.</summary>
public class ArticlesController(AppDbContext db, ImageStorage images, SiteClock clock) : AdminControllerBase
{
    public async Task<IActionResult> Index(ArticleCategory? category)
    {
        var query = db.Articles.AsQueryable();
        if (category is not null) query = query.Where(a => a.Category == category);
        ViewData["Category"] = category;
        return View(await query.OrderBy(a => a.Category).ThenByDescending(a => a.CreatedAt).ToListAsync());
    }

    public IActionResult Create(ArticleCategory category = ArticleCategory.Project) =>
        View("Edit", new Article { Category = category });

    public async Task<IActionResult> Edit(int id)
    {
        var article = await db.Articles.FindAsync(id);
        return article is null ? NotFound() : View(article);
    }

    [HttpPost]
    public async Task<IActionResult> Save(Article model, IFormFile? image, bool removeImage)
    {
        model.Slug = (model.Slug ?? "").Trim().ToLowerInvariant();
        if (await db.Articles.AnyAsync(a => a.Id != model.Id && a.Category == model.Category && a.Slug == model.Slug))
            ModelState.AddModelError(nameof(Article.Slug), "Такой адрес в этом разделе уже есть");

        if (image is { Length: > 0 })
        {
            var (path, error) = await images.SaveAsync(image);
            if (error is not null) ModelState.AddModelError("image", error);
            else model.ImagePath = path;
        }
        else if (removeImage)
        {
            model.ImagePath = null;
        }

        if (!ModelState.IsValid) return View("Edit", model);

        if (model.Id == 0)
        {
            model.CreatedAt = clock.Now;
            db.Articles.Add(model);
        }
        else
        {
            var existing = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == model.Id);
            if (existing is null) return NotFound();
            model.CreatedAt = existing.CreatedAt;
            db.Articles.Update(model);
        }
        await db.SaveChangesAsync();
        Flash("Сохранено");
        return RedirectToAction(nameof(Edit), new { id = model.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article is null) return NotFound();
        if (await db.Orders.AnyAsync(o => o.Kind == OrderKind.Article && o.ItemId == id && o.Status == OrderStatus.Paid))
        {
            FlashError("По этому материалу есть оплаты — удалять нельзя. Снимите галочку «Опубликовано», чтобы скрыть его.");
            return RedirectToAction(nameof(Edit), new { id });
        }
        db.Articles.Remove(article);
        await db.SaveChangesAsync();
        Flash($"«{article.Title}» удалено");
        return RedirectToAction(nameof(Index), new { category = article.Category });
    }
}
