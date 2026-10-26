using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PsySite.Models;

namespace PsySite.Services.Payments;

/// <summary>
/// Оплата через ЮKassa (https://yookassa.ru/developers/api).
/// Включается так: Payments:Provider = "YooKassa", плюс ShopId и SecretKey из личного кабинета ЮKassa.
/// В кабинете ЮKassa укажите адрес для уведомлений: https://ваш-сайт/pay/webhook
/// и включите событие payment.succeeded.
///
/// Внимание: по 54-ФЗ продавцу (ИП, ООО) нужно передавать данные для чека (поле "receipt").
/// Самозанятым ЮKassa может сама регистрировать чеки в «Мой налог». Уточните у бухгалтера
/// и при необходимости добавьте receipt в метод StartAsync.
/// </summary>
public class YooKassaPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "YooKassa";
    public string Name => ProviderName;

    private readonly HttpClient _http;
    private readonly ILogger<YooKassaPaymentProvider> _log;

    public YooKassaPaymentProvider(HttpClient http, IOptions<PaymentOptions> options, ILogger<YooKassaPaymentProvider> log)
    {
        _http = http;
        _log = log;
        var o = options.Value.YooKassa;
        _http.BaseAddress = new Uri("https://api.yookassa.ru/v3/");
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ShopId}:{o.SecretKey}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<PaymentStartResult> StartAsync(Order order, string returnUrl, CancellationToken ct = default)
    {
        var body = new
        {
            amount = new { value = order.Amount.ToString("0.00", CultureInfo.InvariantCulture), currency = "RUB" },
            capture = true,
            confirmation = new { type = "redirect", return_url = returnUrl },
            description = Truncate($"Заказ №{order.Id}: {order.Description}", 128),
            metadata = new { orderId = order.PublicId.ToString() }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "payments") { Content = JsonContent.Create(body) };
        // Ключ идемпотентности: если запрос повторится, ЮKassa не создаст второй платёж.
        req.Headers.Add("Idempotence-Key", order.PublicId.ToString());

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("ЮKassa: ошибка создания платежа {Status}: {Body}", resp.StatusCode, text);
            throw new InvalidOperationException("Не удалось создать платёж в ЮKassa.");
        }

        var payment = JsonSerializer.Deserialize<YkPayment>(text)
            ?? throw new InvalidOperationException("Пустой ответ ЮKassa.");
        var url = payment.Confirmation?.ConfirmationUrl
            ?? throw new InvalidOperationException("ЮKassa не вернула ссылку на оплату.");
        return new PaymentStartResult(url, payment.Id);
    }

    public async Task<bool> IsPaidAsync(string providerPaymentId, CancellationToken ct = default)
    {
        var payment = await _http.GetFromJsonAsync<YkPayment>($"payments/{Uri.EscapeDataString(providerPaymentId)}", ct);
        return payment?.Status == "succeeded";
    }

    public async Task<string?> ReadWebhookPaymentIdAsync(HttpRequest request, CancellationToken ct = default)
    {
        // Уведомлению не доверяем: берём из него только Id, а статус потом проверяем запросом в API (IsPaidAsync).
        var note = await JsonSerializer.DeserializeAsync<YkNotification>(request.Body, cancellationToken: ct);
        return note?.Object?.Id;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private class YkPayment
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("confirmation")] public YkConfirmation? Confirmation { get; set; }
    }

    private class YkConfirmation
    {
        [JsonPropertyName("confirmation_url")] public string? ConfirmationUrl { get; set; }
    }

    private class YkNotification
    {
        [JsonPropertyName("event")] public string? Event { get; set; }
        [JsonPropertyName("object")] public YkPayment? Object { get; set; }
    }
}
