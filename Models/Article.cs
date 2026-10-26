using System.ComponentModel.DataAnnotations;

namespace PsySite.Models;

/// <summary>Раздел сайта, к которому относится материал.</summary>
public enum ArticleCategory
{
    [Display(Name = "Проекты")] Project = 0,
    [Display(Name = "Тексты")] Text = 1,
    [Display(Name = "Ближайшие события")] Event = 2,
    [Display(Name = "Записи вебинаров")] Webinar = 3
}

/// <summary>
/// Один материал сайта: проект (лаборатория), статья, событие или запись вебинара.
/// Если указана цена — на странице появляется кнопка «Оплатить».
/// </summary>
public class Article
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Укажите заголовок")]
    [StringLength(200)]
    [Display(Name = "Заголовок")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Укажите адрес (slug)")]
    [StringLength(200)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Только латиница в нижнем регистре, цифры и дефис")]
    [Display(Name = "Адрес страницы (slug)")]
    public string Slug { get; set; } = "";

    [Display(Name = "Раздел")]
    public ArticleCategory Category { get; set; }

    [StringLength(500)]
    [Display(Name = "Краткое описание")]
    public string? Summary { get; set; }

    [Display(Name = "Текст (HTML)")]
    public string Body { get; set; } = "";

    [StringLength(300)]
    [Display(Name = "Картинка")]
    public string? ImagePath { get; set; }

    [Display(Name = "Цена, ₽ (пусто — бесплатно)")]
    [Range(0, 10_000_000)]
    public decimal? Price { get; set; }

    [Display(Name = "Дата события")]
    public DateTime? EventDate { get; set; }

    /// <summary>Ссылка, которую покупатель получает после оплаты (на запись вебинара, чат и т.п.).</summary>
    [StringLength(500)]
    [Display(Name = "Ссылка для оплативших")]
    public string? PaidAccessUrl { get; set; }

    [Display(Name = "Опубликовано")]
    public bool IsPublished { get; set; } = true;

    [Display(Name = "Показывать на главной")]
    public bool ShowOnHome { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
