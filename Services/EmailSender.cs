using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using PsySite.Logging;

namespace PsySite.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}

/// <summary>
/// Отправка писем через SMTP (подойдёт Яндекс, Mail.ru, Gmail, Unisender и т.п.).
/// Если Smtp:Host не задан, письмо не отправляется, а выводится в лог — удобно при разработке.
/// Ошибка отправки не роняет сайт: она пишется в лог.
/// </summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> log) : IEmailSender
{
    private readonly SmtpOptions _o = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_o.Host))
        {
            log.LogInformation("Письмо «{Subject}» для {To} не отправлено: SMTP не настроен", subject, LogMask.Email(to));
            // Текст письма содержит данные клиента, поэтому пишется только на подробном уровне (Debug) — при разработке.
            log.LogDebug("Текст письма «{Subject}»:\n{Body}", subject, htmlBody);
            return;
        }

        try
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(string.IsNullOrEmpty(_o.From) ? _o.User : _o.From, _o.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);

            using var client = new SmtpClient(_o.Host, _o.Port)
            {
                EnableSsl = _o.UseSsl,
                Credentials = new NetworkCredential(_o.User, _o.Password)
            };
            await client.SendMailAsync(msg);
            log.LogInformation("Письмо «{Subject}» отправлено на {To}", subject, LogMask.Email(to));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Не удалось отправить письмо «{Subject}» на {To} через {Host}", subject, LogMask.Email(to), _o.Host);
        }
    }
}
