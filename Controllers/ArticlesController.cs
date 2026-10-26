using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Controllers;

public class ArticleListViewModel
{
    public ArticleCategory Category { get; set; }
    public List<Article> Items { get; set; } = [];
    public List<Article> PastEvents { get; set; } = [];
}

/// <summary>Форма покупки платного материала.</summary>
public class BuyForm
{
    [Required(ErrorMessage = "Укажите имя")]
    [StringLength(200)]
    [Display(Name = "Имя")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Укажите почту")]
    [EmailAddress(ErrorMessage = "Похоже, в адресе почты ошибка")]
    [StringLength(200)]
    [Display(Name = "Почта")]
    public string Email { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Нужно согласие с офертой и обработкой данных")]
    public bool Consent { get; set; }

    /// <summary>Ловушка для ботов: люди это поле не видят и не заполняют.</summary>
    public string? Website { get; set; }
}

public class ArticleDetailsViewModel
{
    public Article Article { get; set; } = null!;
    public BuyForm Buy { get; set; } = new();
}

[Route("{section:regex(" + Sections.RouteRegex + ")}")]
public class ArticlesController(AppDbContext db, SiteClock clock, OrderService orders, ILogger<ArticlesController> log) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string section)
    {
        var category = Sections.BySlug[section];
        var query = db.Articles.Where(a => a.IsPublished && a.Category == category);
        var vm = new ArticleListViewModel { Category = category };

        if (category == ArticleCategory.Event)
        {
            var now = clock.Now;
            vm.Items = await query.Where(a => a.EventDate == null || a.EventDate >= now)
                .OrderBy(a => a.EventDate).ToListAsync();
            vm.PastEvents = await query.Where(a => a.EventDate < now)
                .OrderByDescending(a => a.EventDate).Take(10).ToListAsync();
        }
        else
        {
            vm.Items = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();
        }
        return View(vm);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string section, string slug)
    {
        var article = await Find(section, slug);
        if (article is null) return NotFound();
        return View(new ArticleDetailsViewModel { Article = article });
    }

    [HttpPost("{slug}")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Details(string section, string slug, [Bind(Prefix = "Buy")] BuyForm form)
    {
        var article = await Find(section, slug);
        if (article is null) return NotFound();
        if (article.Price is not > 0) return RedirectToAction(nameof(Details), new { section, slug });

        if (!string.IsNullOrEmpty(form.Website))
        {
            log.LogWarning("Покупка: сработала ловушка для ботов, IP {ClientIp}", HttpContext.Connection.RemoteIpAddress);
            return RedirectToAction(nameof(Details), new { section, slug });
        }

        if (!ModelState.IsValid)
            return View(new ArticleDetailsViewModel { Article = article, Buy = form });

        var order = await orders.CreateForArticleAsync(article, form.Email, form.Name);
        return RedirectToAction("Start", "Payments", new { id = order.PublicId });
    }

    private Task<Article?> Find(string section, string slug)
    {
        var category = Sections.BySlug[section];
        return db.Articles.FirstOrDefaultAsync(a => a.IsPublished && a.Category == category && a.Slug == slug);
    }
}
