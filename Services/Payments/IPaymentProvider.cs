using PsySite.Models;

namespace PsySite.Services.Payments;

/// <summary>Результат создания платежа: куда отправить покупателя и какой Id дала платёжная система.</summary>
public record PaymentStartResult(string RedirectUrl, string? ProviderPaymentId);

/// <summary>
/// Общий интерфейс платёжной системы. Чтобы подключить новую (CloudPayments, Robokassa, Tinkoff...),
/// достаточно написать класс, реализующий этот интерфейс, и зарегистрировать его в Program.cs.
/// Остальной код сайта менять не придётся.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Короткое имя, сохраняется в заказе.</summary>
    string Name { get; }

    /// <summary>Создать платёж и вернуть адрес страницы оплаты.</summary>
    Task<PaymentStartResult> StartAsync(Order order, string returnUrl, CancellationToken ct = default);

    /// <summary>Спросить у платёжной системы, оплачен ли платёж (используем, чтобы не верить уведомлениям на слово).</summary>
    Task<bool> IsPaidAsync(string providerPaymentId, CancellationToken ct = default);

    /// <summary>Разобрать уведомление (webhook) от платёжной системы и вернуть Id платежа, если оно о нём.</summary>
    Task<string?> ReadWebhookPaymentIdAsync(HttpRequest request, CancellationToken ct = default);
}
