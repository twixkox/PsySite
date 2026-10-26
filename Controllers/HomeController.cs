using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PsySite.Data;
using PsySite.Models;
using PsySite.Services;

namespace PsySite.Controllers;

public class HomeViewModel
{
    public Page? Intro { get; set; }
    public List<Article> Projects { get; set; } = [];
    public List<Article> Events { get; set; } = [];
    public List<Article> Texts { get; set; } = [];
    public List<ConsultationType> ConsultationTypes { get; set; } = [];
}

public class HomeController(AppDbContext db, SiteClock clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var now = clock.Now;
        var vm = new HomeViewModel
        {
            Intro = await db.Pages.FirstOrDefaultAsync(p => p.Slug == "home"),
            Projects = await db.Articles
                .Where(a => a.IsPublished && a.Category == ArticleCategory.Project && a.ShowOnHome)
                .OrderByDescending(a => a.CreatedAt).Take(6).ToListAsync(),
            Events = await db.Articles
                .Where(a => a.IsPublished && a.Category == ArticleCategory.Event && a.EventDate >= now)
                .OrderBy(a => a.EventDate).Take(3).ToListAsync(),
            Texts = await db.Articles
                .Where(a => a.IsPublished && a.Category == ArticleCategory.Text)
                .OrderByDescending(a => a.CreatedAt).Take(3).ToListAsync(),
            ConsultationTypes = await db.ConsultationTypes.Where(t => t.IsActive).ToListAsync()
        };
        return View(vm);
    }

    /// <summary>Текстовые страницы: /about, /book, /contacts, /privacy-policy и т.д.</summary>
    [HttpGet("{slug:regex(^[[a-z0-9-]]+$)}", Order = 1000)]
    public async Task<IActionResult> Page(string slug)
    {
        if (slug == "home") return RedirectPermanent("/");
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Slug == slug);
        return page is null ? NotFound() : View(page);
    }

    [HttpGet("error/{code:int?}")]
    [HttpPost("error/{code:int?}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Error(int? code)
    {
        Response.StatusCode = code ?? 500;
        ViewData["Code"] = code ?? 500;
        return View();
    }
}
