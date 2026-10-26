using System.ComponentModel.DataAnnotations;

namespace PsySite.Models;

/// <summary>
/// Простая текстовая страница: «Про меня», «Книга», «Контакты», политика конфиденциальности, оферта и т.п.
/// Доступна по адресу /{Slug}.
/// </summary>
public class Page
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Укажите адрес (slug)")]
    [StringLength(100)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Только латиница в нижнем регистре, цифры и дефис")]
    [Display(Name = "Адрес страницы (slug)")]
    public string Slug { get; set; } = "";

    [Required(ErrorMessage = "Укажите заголовок")]
    [StringLength(200)]
    [Display(Name = "Заголовок")]
    public string Title { get; set; } = "";

    [Display(Name = "Текст (HTML)")]
    public string Body { get; set; } = "";

    [Display(Name = "Показывать в меню")]
    public bool ShowInMenu { get; set; }

    [Display(Name = "Порядок в меню")]
    public int MenuOrder { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
