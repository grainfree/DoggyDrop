using Microsoft.AspNetCore.Identity.UI.Services;

namespace DoggyDrop.Services;

// Identity stays immediate and non-throwing, independent of outbox/preferences.
public sealed class EmailSender(IEmailTransport transport) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        try { await transport.SendAsync(email, new RenderedEmail(subject, htmlMessage, ""), CancellationToken.None); }
        catch (Exception) { /* Do not disclose tokens, recipients or provider details. */ }
    }
}
