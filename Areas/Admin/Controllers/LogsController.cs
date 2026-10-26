using Microsoft.AspNetCore.Mvc;
using PsySite.Logging;

namespace PsySite.Areas.Admin.Controllers;

public class LogsViewModel
{
    public List<LogFileInfo> Files { get; set; } = [];
    public string? File { get; set; }
    public string Level { get; set; } = "all";
    public string? Q { get; set; }
    public List<LogEntry> Entries { get; set; } = [];
    public int Total { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int Shown { get; set; }
}

/// <summary>Просмотр логов в админке: что происходило на сайте, ошибки, действия администратора.</summary>
public class LogsController(LogReader reader) : AdminControllerBase
{
    private const int MaxShown = 1000;

    /// <param name="file">Имя файла лога; по умолчанию — сегодняшний общий журнал.</param>
    /// <param name="level">all — всё, warn — предупреждения и ошибки, error — только ошибки, admin — действия в админке.</param>
    /// <param name="q">Поиск по тексту (номер записи, заказа, IP, адрес страницы...).</param>
    public IActionResult Index(string? file, string level = "all", string? q = null)
    {
        var vm = new LogsViewModel { Files = reader.ListFiles(), Level = level, Q = q };
        vm.File = reader.Resolve(file) is not null ? file
            : vm.Files.FirstOrDefault(f => f.Name.StartsWith("site-"))?.Name ?? vm.Files.FirstOrDefault()?.Name;

        var path = reader.Resolve(vm.File);
        if (path is null) return View(vm);

        var all = reader.Read(path);
        vm.Total = all.Count;
        vm.Errors = all.Count(e => e.IsError);
        vm.Warnings = all.Count(e => e.IsWarning);

        IEnumerable<LogEntry> filtered = level switch
        {
            "error" => all.Where(e => e.IsError),
            "warn" => all.Where(e => e.IsError || e.IsWarning),
            "admin" => all.Where(e => e.Source is "AdminAuditFilter" or "AccountController" || e.Message.StartsWith("Админка:")),
            _ => all
        };
        if (!string.IsNullOrWhiteSpace(q))
            filtered = filtered.Where(e => e.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                                        || e.Source.Contains(q, StringComparison.OrdinalIgnoreCase)
                                        || (e.Details?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        // Новые сверху; показываем последние 1000, чтобы страница не тормозила.
        var list = filtered.ToList();
        vm.Shown = list.Count;
        vm.Entries = list.TakeLast(MaxShown).Reverse().ToList();
        return View(vm);
    }

    public IActionResult Download(string file)
    {
        var path = reader.Resolve(file);
        if (path is null) return NotFound();
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return File(stream, "text/plain; charset=utf-8", file);
    }
}
