using DoggyDrop.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DoggyDrop.Services;

public interface IEmailTransport
{
    Task<EmailFailure> SendAsync(string recipient, RenderedEmail email, CancellationToken ct);
}

public sealed class SmtpEmailTransport(IOptions<EmailSettings> options, IHostEnvironment environment) : IEmailTransport
{
    public async Task<EmailFailure> SendAsync(string recipient, RenderedEmail email, CancellationToken ct)
    {
        if (!environment.IsProduction()) return EmailFailure.Disabled;
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SmtpServer) || settings.SmtpPort is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(settings.SmtpUser) || string.IsNullOrWhiteSpace(settings.SmtpPass) ||
            !MailboxAddress.TryParse(settings.SenderEmail, out var from)) return EmailFailure.Configuration;
        if (recipient.Any(char.IsControl) || !MailboxAddress.TryParse(recipient, out var to)) return EmailFailure.Recipient;
        if (email.Subject.Any(char.IsControl)) return EmailFailure.Rendering;
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.SenderName, from.Address));
            message.To.Add(to); message.Subject = email.Subject;
            message.Body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Text }.ToMessageBody();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var client = new SmtpClient { Timeout = 30000 };
            await client.ConnectAsync(settings.SmtpServer, settings.SmtpPort, SecureSocketOptions.StartTls, timeout.Token);
            await client.AuthenticateAsync(settings.SmtpUser, settings.SmtpPass, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            try { await client.DisconnectAsync(true, timeout.Token); } catch (Exception) { }
            return EmailFailure.None;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (MailKit.Security.AuthenticationException) { return EmailFailure.Configuration; }
        catch (SmtpCommandException error) { return (int)error.StatusCode >= 500 ? EmailFailure.Recipient : EmailFailure.Transient; }
        catch (Exception) { return EmailFailure.Transient; }
    }
}
