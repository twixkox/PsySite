using System.ComponentModel.DataAnnotations;

namespace PsySite.Models;

public enum OrderKind
{
    [Display(Name = "Консультация")] Booking = 0,
    [Display(Name = "Материал")] Article = 1
}

public enum OrderStatus
{
    [Display(Name = "Создан")] Pending = 0,
    [Display(Name = "Оплачен")] Paid = 1,
    [Display(Name = "Отменён")] Cancelled = 2,
    [Display(Name = "Возврат")] Refunded = 3
}

/// <summary>
/// Заказ на оплату. Создаётся при нажатии «Оплатить», отмечается оплаченным,
/// когда платёжная система подтверждает платёж.
/// </summary>
public class Order
{
    public int Id { get; set; }

    /// <summary>Публичный номер заказа для ссылок (чтобы не светить порядковый Id).</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public OrderKind Kind { get; set; }

    /// <summary>Id записи (Booking) или материала (Article) — зависит от Kind.</summary>
    public int ItemId { get; set; }

    [StringLength(300)]
    public string Description { get; set; } = "";

    public decimal Amount { get; set; }

    [Required, StringLength(200)]
    public string CustomerEmail { get; set; } = "";

    [StringLength(200)]
    public string? CustomerName { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>Какой платёжной системой оплачивается (Fake, YooKassa, ...).</summary>
    [StringLength(50)]
    public string? Provider { get; set; }

    /// <summary>Id платежа в платёжной системе.</summary>
    [StringLength(100)]
    public string? ProviderPaymentId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PaidAt { get; set; }
}

/// <summary>Подписчик рассылки.</summary>
public class Subscriber
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Email { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
