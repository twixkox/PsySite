using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PsySite.Areas.Admin;

namespace PsySite.Areas.Admin.Controllers;

/// <summary>
/// Общий предок всех контроллеров админки: только для вошедшего администратора.
/// Каждое изменение записывается в журнал (см. AdminAuditFilter).
/// </summary>
[Area("Admin")]
[Authorize]
[TypeFilter(typeof(AdminAuditFilter))]
public abstract class AdminControllerBase : Controller
{
    protected void Flash(string message) => TempData["Message"] = message;
    protected void FlashError(string message) => TempData["Error"] = message;
}
