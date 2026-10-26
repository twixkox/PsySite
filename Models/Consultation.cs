using System.ComponentModel.DataAnnotations;

namespace PsySite.Models;

/// <summary>Вид консультации: например «Онлайн, 60 минут» или «Очно, 90 минут».</summary>
public class ConsultationType
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Укажите название")]
    [StringLength(200)]
    [Display(Name = "Название")]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Range(15, 480, ErrorMessage = "От 15 до 480 минут")]
    [Display(Name = "Длительность, мин")]
    public int DurationMinutes { get; set; } = 60;

    [Range(0, 10_000_000)]
    [Display(Name = "Цена, ₽")]
    public decimal Price { get; set; }

    [Display(Name = "Требовать предоплату")]
    public bool RequirePrepayment { get; set; } = true;

    [Display(Name = "Активна")]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Свободное окно в расписании. Админ создаёт окна, клиент выбирает одно из них при записи.
/// Время хранится в часовом поясе сайта (см. Site:TimeZone в appsettings.json).
/// </summary>
public class Slot
{
    public int Id { get; set; }

    [Display(Name = "Начало")]
    public DateTime StartsAt { get; set; }

    [Display(Name = "Длительность, мин")]
    public int DurationMinutes { get; set; } = 60;

    /// <summary>Окно занято, если на него есть активная (не отменённая) запись.</summary>
    public bool IsBooked { get; set; }

    public DateTime EndsAt => StartsAt.AddMinutes(DurationMinutes);
}

public enum BookingStatus
{
    [Display(Name = "Ждёт оплаты")] AwaitingPayment = 0,
    [Display(Name = "Новая")] New = 1,
    [Display(Name = "Подтверждена")] Confirmed = 2,
    [Display(Name = "Проведена")] Done = 3,
    [Display(Name = "Отменена")] Cancelled = 4
}

/// <summary>Запись клиента на консультацию.</summary>
public class Booking
{
    public int Id { get; set; }

    public int ConsultationTypeId { get; set; }
    public ConsultationType? ConsultationType { get; set; }

    public int SlotId { get; set; }
    public Slot? Slot { get; set; }

    [Required, StringLength(200)]
    public string ClientName { get; set; } = "";

    [Required, StringLength(200)]
    public string ClientEmail { get; set; } = "";

    [StringLength(50)]
    public string? ClientPhone { get; set; }

    /// <summary>Как удобнее связаться: Telegram, WhatsApp, Zoom и т.п.</summary>
    [StringLength(200)]
    public string? ContactMethod { get; set; }

    [StringLength(2000)]
    public string? Comment { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.New;

    /// <summary>Заметка для себя, клиент её не видит.</summary>
    [StringLength(2000)]
    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
