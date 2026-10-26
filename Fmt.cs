using System.Globalization;

namespace PsySite;

/// <summary>Небольшие помощники для красивого вывода цен и дат в шаблонах.</summary>
public static class Fmt
{
    private static readonly CultureInfo Ru = new("ru-RU");

    public static string Price(decimal value) =>
        value == 0 ? "бесплатно" : value.ToString(value % 1 == 0 ? "#,0" : "#,0.00", Ru) + " ₽";

    public static string Date(DateTime d) => d.ToString("d MMMM yyyy", Ru);

    public static string DateTime(DateTime d) => d.ToString("d MMMM, HH:mm", Ru);

    public static string DayTitle(DateTime d)
    {
        var s = d.ToString("dddd, d MMMM", Ru);
        return char.ToUpper(s[0], Ru) + s[1..];
    }

    public static string Time(DateTime d) => d.ToString("HH:mm", Ru);

    /// <summary>Склонение по числу: Plural(21, "ошибка", "ошибки", "ошибок") → «21 ошибка».</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var n10 = Math.Abs(n) % 10; var n100 = Math.Abs(n) % 100;
        var word = n10 == 1 && n100 != 11 ? one : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? few : many;
        return $"{n} {word}";
    }

    /// <summary>Русское название значения перечисления из атрибута [Display(Name = "...")].</summary>
    public static string Name(Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        var attr = member?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DataAnnotations.DisplayAttribute;
        return attr?.Name ?? value.ToString();
    }
}
