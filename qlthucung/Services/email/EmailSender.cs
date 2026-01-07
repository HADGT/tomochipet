using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using qlthucung.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;

namespace qlthucung.Services.email
{
    public class EmailSender : IEmailSender
    {
        private readonly SmtpSettings _smtpSettings;

        public EmailSender(IOptions<SmtpSettings> smtpSettings)
        {
            _smtpSettings = smtpSettings.Value;
        }
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var smtpClient = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
            {
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_smtpSettings.UserName, _smtpSettings.Password),
                EnableSsl = _smtpSettings.EnableSSL
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_smtpSettings.UserName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };
            mailMessage.To.Add(email);

            return smtpClient.SendMailAsync(mailMessage);
        }
    }
}
