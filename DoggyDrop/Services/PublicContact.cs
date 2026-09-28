using System.Net.Mail;

namespace DoggyDrop.Services;

// Public presentation configuration only. Never read SMTP credentials or source contacts.
public sealed record PublicContact(string Email, string Mailto)
{
    public static PublicContact? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsControl)) return null;
        var email = value.Trim();
        if (email.Any(char.IsWhiteSpace) || email.Any(c => c > 127) ||
            email.IndexOfAny(['<', '>', '"', '\\', '?', '#']) >= 0 ||
            !MailAddress.TryCreate(email, out var address) || address.DisplayName.Length != 0 ||
            address.Address != email || !address.Host.Contains('.') ||
            Uri.CheckHostName(address.Host) != UriHostNameType.Dns) return null;
        // This configured ASCII mailbox uses a conventional, at-most-64-octet local part.
        if (address.User.Length > 64 || address.User.StartsWith('.') || address.User.EndsWith('.') ||
            address.User.Contains("..", StringComparison.Ordinal)) return null;
        return new(email, "mailto:" + Uri.EscapeDataString(address.User) + "@" + address.Host);
    }
}
