using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PsySite.Security;

namespace PsySite.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController(IOptions<AdminOptions> options, IWebHostEnvironment env, ILogger<AccountController> log) : Controller
{
    private readonly AdminOptions _admin = options.Value;
    private string? PasswordProblem => AdminPassword.Problem(_admin.Password, env.IsDevelopment());

    [HttpGet("admin/login")]
    public IActionResult Login(string? returnUrl)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["PasswordProblem"] = PasswordProblem;
        return View();
    }

    [HttpPost("admin/login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(string login, string password, string? returnUrl)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["PasswordProblem"] = PasswordProblem;

        if (PasswordProblem is not null || !Same(login, _admin.Login) || !Same(password, _admin.Password))
        {
            if (PasswordProblem is not null)
                log.LogError("Вход в админку невозможен: {Problem}", PasswordProblem);
            else
                log.LogWarning("Неудачная попытка входа в админку: логин «{Login}», IP {ClientIp}",
                    (login ?? "").Length > 50 ? login![..50] : login, HttpContext.Connection.RemoteIpAddress);
            ViewData["Error"] = "Неверный логин или пароль.";
            return View();
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, _admin.Login), new Claim(ClaimTypes.Role, "Admin"),
             new Claim(AdminPassword.StampClaim, AdminPassword.Stamp(_admin.Password))],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });

        log.LogInformation("Вход в админку: {Login}, IP {ClientIp}", _admin.Login, HttpContext.Connection.RemoteIpAddress);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/admin");
    }

    [HttpPost("admin/logout")]
    public async Task<IActionResult> Logout()
    {
        log.LogInformation("Выход из админки: {Login}", User.Identity?.Name);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    /// <summary>Сравнение строк за постоянное время, чтобы пароль нельзя было подобрать по скорости ответа.</summary>
    private static bool Same(string? a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? ""), Encoding.UTF8.GetBytes(b));
}
