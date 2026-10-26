namespace PsySite.Models;

/// <summary>Соответствие разделов сайта и адресов: /projects, /texts, /events, /webinars.</summary>
public static class Sections
{
    public const string RouteRegex = "^(projects|texts|events|webinars)$";

    public static readonly IReadOnlyDictionary<string, ArticleCategory> BySlug = new Dictionary<string, ArticleCategory>
    {
        ["projects"] = ArticleCategory.Project,
        ["texts"] = ArticleCategory.Text,
        ["events"] = ArticleCategory.Event,
        ["webinars"] = ArticleCategory.Webinar
    };

    public static string Slug(ArticleCategory c) => c switch
    {
        ArticleCategory.Project => "projects",
        ArticleCategory.Text => "texts",
        ArticleCategory.Event => "events",
        ArticleCategory.Webinar => "webinars",
        _ => "texts"
    };

    public static string Title(ArticleCategory c) => c switch
    {
        ArticleCategory.Project => "Проекты",
        ArticleCategory.Text => "Тексты",
        ArticleCategory.Event => "Ближайшие события",
        ArticleCategory.Webinar => "Записи вебинаров",
        _ => ""
    };

    public static string Intro(ArticleCategory c) => c switch
    {
        ArticleCategory.Project => "Лаборатории, курсы и группы. Можно присоединиться онлайн из любого города.",
        ArticleCategory.Text => "Статьи и заметки о психологии, отношениях и переменах.",
        ArticleCategory.Event => "Вебинары, встречи и лекции, которые скоро пройдут.",
        ArticleCategory.Webinar => "Записи прошедших вебинаров: можно купить и посмотреть в удобное время.",
        _ => ""
    };
}
