using Microsoft.EntityFrameworkCore;
using PsySite.Models;

namespace PsySite.Data;

/// <summary>
/// Заполняет пустую базу примерами, чтобы сразу было что посмотреть.
/// Всё это потом редактируется или удаляется в админке.
/// </summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(AppDbContext db)
    {
        if (!await db.Pages.AnyAsync())
        {
            db.Pages.AddRange(
                new Page
                {
                    // Текст блока «Обо мне» на главной странице.
                    Slug = "home", Title = "Главная: о себе",
                    Body = """
                        <h3>Психолог</h3>
                        <p>Работаю со взрослыми онлайн и очно. Использую краткосрочные подходы, которые помогают увидеть изменения уже за несколько встреч.</p>
                        <h3>Лектор</h3>
                        <p>Веду вебинары, групповые лаборатории и курсы для помогающих специалистов.</p>
                        <h3>Автор</h3>
                        <p>Пишу о внутренней опоре, свободе и переменах в жизни.</p>
                        """
                },
                new Page
                {
                    // Вступление над формой записи на странице /consultations.
                    Slug = "consultations", Title = "Консультации",
                    Body = """
                        <p>Индивидуальная работа помогает разобраться в сложной ситуации, принять решение или пройти через перемены.</p>
                        <p>Выберите формат и удобное время. После записи вы получите письмо с подтверждением.</p>
                        """
                },
                new Page
                {
                    Slug = "about", Title = "Обо мне", ShowInMenu = true, MenuOrder = 10,
                    Body = """
                        <p>Здесь будет рассказ о себе: образование, подходы, с какими запросами вы работаете и как проходит работа.</p>
                        <h2>Образование</h2>
                        <ul><li>Базовое психологическое образование</li><li>Повышение квалификации</li></ul>
                        <h2>Как я работаю</h2>
                        <p>Опишите формат встреч, длительность и то, чего клиенту ждать от первой консультации.</p>
                        """
                },
                new Page
                {
                    Slug = "book", Title = "Книга", ShowInMenu = true, MenuOrder = 60,
                    Body = "<p>Описание книги, где её купить и отзывы читателей.</p>"
                },
                new Page
                {
                    Slug = "contacts", Title = "Контакты", ShowInMenu = true, MenuOrder = 80,
                    Body = """
                        <p>Почта: <a href="mailto:hello@example.com">hello@example.com</a></p>
                        <p>Telegram: <a href="https://t.me/example">@example</a></p>
                        <p>Отвечаем в будние дни с 10:00 до 18:00 по Москве.</p>
                        """
                },
                new Page { Slug = "privacy-policy", Title = "Политика конфиденциальности", Body = "<p>Текст политики обработки персональных данных.</p>" },
                new Page { Slug = "offer", Title = "Публичная оферта", Body = "<p>Текст публичной оферты.</p>" },
                new Page { Slug = "agreement", Title = "Согласие на обработку персональных данных", Body = "<p>Текст согласия.</p>" },
                new Page { Slug = "cookies", Title = "Использование cookie", Body = "<p>Какие cookie использует сайт и зачем.</p>" }
            );
        }

        if (!await db.ConsultationTypes.AnyAsync())
        {
            db.ConsultationTypes.AddRange(
                new ConsultationType
                {
                    Name = "Онлайн-консультация", DurationMinutes = 60, Price = 5000,
                    Description = "Встреча по видеосвязи. Подходит, если вы в другом городе или стране."
                },
                new ConsultationType
                {
                    Name = "Очная консультация", DurationMinutes = 60, Price = 6000,
                    Description = "Встреча в кабинете."
                },
                new ConsultationType
                {
                    Name = "Знакомство", DurationMinutes = 20, Price = 0, RequirePrepayment = false,
                    Description = "Короткая бесплатная встреча, чтобы понять, подходим ли мы друг другу."
                }
            );
        }

        if (!await db.Slots.AnyAsync())
        {
            // Окна на ближайшие две недели: будни, 11:00, 14:00 и 17:00.
            var day = DateTime.Today.AddDays(1);
            for (var i = 0; i < 14; i++, day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                foreach (var hour in new[] { 11, 14, 17 })
                    db.Slots.Add(new Slot { StartsAt = day.AddHours(hour), DurationMinutes = 60 });
            }
        }

        if (!await db.Articles.AnyAsync())
        {
            db.Articles.AddRange(
                new Article
                {
                    Category = ArticleCategory.Project, Slug = "inner-house", Title = "Лаборатория «Внутренний дом»",
                    Summary = "Шесть недель онлайн-встреч в небольшой группе о границах, опоре и уюте внутри себя.",
                    Body = "<p>Подробное описание программы, расписание встреч и что входит в участие.</p>",
                    Price = 12000, ShowOnHome = true
                },
                new Article
                {
                    Category = ArticleCategory.Project, Slug = "fairy-tales", Title = "Лаборатория «Сказки о силе»",
                    Summary = "Работа с сюжетами сказок как с картой собственных ресурсов.",
                    Body = "<p>Описание лаборатории.</p>", Price = 9000, ShowOnHome = true
                },
                new Article
                {
                    Category = ArticleCategory.Project, Slug = "private-practice", Title = "Курс «Частная практика»",
                    Summary = "Для начинающих специалистов: как выстроить практику и не выгореть.",
                    Body = "<p>Описание курса.</p>", Price = 15000, ShowOnHome = true
                },
                new Article
                {
                    Category = ArticleCategory.Text, Slug = "about-short-therapy", Title = "Что такое краткосрочная терапия",
                    Summary = "Чем короткая работа отличается от долгой и кому она подходит.",
                    Body = "<p>Текст статьи.</p>"
                },
                new Article
                {
                    Category = ArticleCategory.Event, Slug = "open-webinar", Title = "Открытый вебинар: как просить о помощи",
                    Summary = "Бесплатная встреча в Zoom.", Body = "<p>Описание вебинара и ссылка на регистрацию.</p>",
                    EventDate = DateTime.Today.AddDays(10).AddHours(19)
                },
                new Article
                {
                    Category = ArticleCategory.Webinar, Slug = "boundaries-recording", Title = "Запись вебинара «Личные границы»",
                    Summary = "2 часа видео и рабочая тетрадь.", Body = "<p>Что внутри записи.</p>",
                    Price = 1500, PaidAccessUrl = "https://example.com/video"
                }
            );
        }

        await db.SaveChangesAsync();
    }
}
