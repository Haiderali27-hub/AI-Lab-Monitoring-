using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Backend_API.Services.Notifications;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config["Email:SenderName"],
                _config["Email:SenderEmail"]
            ));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"]!),
                SecureSocketOptions.StartTls
            );
            await client.AuthenticateAsync(
                _config["Email:SenderEmail"],
                _config["Email:AppPassword"]
            );
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Never crash the main flow because of email failure
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
