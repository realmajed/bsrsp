using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;

namespace WebApplication1.Services
{
    public class SMTPEmailSender: IEmailSender
    {
        private readonly IConfiguration _config;

        public SMTPEmailSender(IConfiguration config)
        {
            _config = config;
        }

        async Task IEmailSender.SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // SMTP settings
            using var smtpClient = new SmtpClient(_config["Smtp:Server"])
            {
                Port = int.Parse(_config["Smtp:Port"]!),
                Credentials = new NetworkCredential(
                    _config["Smtp:User"],
                    _config["Smtp:Password"]
                ),
                EnableSsl = bool.Parse(_config["Smtp:EnableSsl"] ?? "true")
            };

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(
                    _config["Smtp:Email"]!,
                    _config["Smtp:Name"]
                ),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };

            mailMessage.To.Add(email);

            // SendMailAsync keeps reservation actions from blocking on synchronous email I/O.
            await smtpClient.SendMailAsync(mailMessage);
        }
    }
}
