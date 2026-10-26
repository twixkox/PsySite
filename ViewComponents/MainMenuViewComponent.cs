using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;

namespace PsySite.ViewComponents;

public record MenuItem(string Title, string Url, int Order);

/// <summary>
/// Главное меню: постоянные разделы + страницы, у которых в админке стоит «Показывать в меню».
/// Порядок задаётся числом: у постоянных разделов он указан ниже, у страниц — в админке.
/// </summary>
public class MainMenuViewComponent(AppDbContext db) : ViewComponent
{
    private static readonly MenuItem[] Fixed =
    [
        new("Проекты", "/projects", 20),
        new("Консультации", "/consultations", 30),
        new("Тексты", "/texts", 40),
        new("Вебинары", "/webinars", 50),
        new("События", "/events", 70)
    ];

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var pages = await db.Pages.Where(p => p.ShowInMenu)
            .Select(p => new MenuItem(p.Title, "/" + p.Slug, p.MenuOrder))
            .ToListAsync();

        var items = Fixed.Concat(pages).OrderBy(i => i.Order).ToList();
        return View(items);
    }
}
