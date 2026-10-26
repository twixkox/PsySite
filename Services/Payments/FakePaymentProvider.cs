using Microsoft.Extensions.Options;
using PsySite.Models;

namespace PsySite.Services.Payments;

/// <summary>Платёж нельзя начать: онлайн-оплата не подключена или отключена.</summary>
public class PaymentUnavailableException(string message) : Exception(message);

/// <summary>
/// Тестовая «оплата» без денег: открывает страницу с кнопками «Оплатить» и «Отменить».
///
/// ВАЖНО: работает только в режиме разработки (Development). На боевом сайте любой посетитель
/// мог бы нажать «Оплатить (тест)» и получить консультацию бесплатно, поэтому там она выключена,
/// пока явно не разрешена настройкой Payments:AllowFakeInProduction (например, на тестовом сервере).
/// </summary>
public class FakePaymentProvider(IWebHostEnvironment env, IOptions<PaymentOptions> options) : IPaymentProvider
{
    public const string ProviderName = "Fake";
    public string Name => ProviderName;

    public bool Enabled => env.IsDevelopment() || options.Value.AllowFakeInProduction;

    public Task<PaymentStartResult> StartAsync(Order order, string returnUrl, CancellationToken ct = default)
    {
        if (!Enabled)
            throw new PaymentUnavailableException(
                "Тестовая оплата отключена на боевом сайте. Подключите платёжную систему (Payments:Provider).");
        return Task.FromResult(new PaymentStartResult($"/pay/fake/{order.PublicId}", "fake-" + order.PublicId.ToString("N")));
    }

    // Статус тестового заказа меняется напрямую на странице /pay/fake, спрашивать некого.
    public Task<bool> IsPaidAsync(string providerPaymentId, CancellationToken ct = default) => Task.FromResult(false);

    public Task<string?> ReadWebhookPaymentIdAsync(HttpRequest request, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
