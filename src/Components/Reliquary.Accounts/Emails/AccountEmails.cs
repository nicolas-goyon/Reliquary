using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reliquary.BuildingBlocks.Email;

namespace Reliquary.Accounts.Emails;

/// <summary>
/// The kinds of emails of the Accounts component.
/// </summary>
internal enum AccountEmailKind
{
    EmailCheck,
    PasswordReset,
    AccountExists,
}

/// <summary>
/// Writes the emails of the Accounts component in the language of the account, and sends them.
/// </summary>
/// <remarks>
/// The texts are in <c>Emails/en.json</c> and <c>Emails/fr.json</c>. The English texts follow the writing rules.
/// </remarks>
internal sealed class AccountEmails(IEmailSender sender, IOptions<AuthOptions> options)
{
    private static readonly FrozenDictionary<string, FrozenDictionary<string, EmailTemplate>> Templates = LoadTemplates();

    public Task SendAsync(AccountEmailKind kind, string to, string language, string displayName, string link, TimeSpan? lifetime, CancellationToken cancellationToken)
    {
        var message = Render(kind, to, language, displayName, link, lifetime);
        return sender.SendAsync(message, cancellationToken);
    }

    /// <summary>
    /// Makes a link to a page of the web interface.
    /// </summary>
    /// <param name="path">The path of the page, for example <c>/auth/check-email</c>.</param>
    /// <param name="token">The token of the link, or <see langword="null"/>.</param>
    /// <returns>The link.</returns>
    public string Link(string path, string? token = null)
    {
        var baseUrl = options.Value.PublicUrl.ToString().TrimEnd('/');
        return token is null ? $"{baseUrl}{path}" : $"{baseUrl}{path}?token={Uri.EscapeDataString(token)}";
    }

    public static EmailMessage Render(AccountEmailKind kind, string to, string language, string displayName, string link, TimeSpan? lifetime)
    {
        var texts = Templates.GetValueOrDefault(language) ?? Templates["en"];
        var template = texts[Key(kind)];
        var hours = lifetime is { } value ? Math.Max(1, (int)Math.Round(value.TotalHours)).ToString(CultureInfo.InvariantCulture) : string.Empty;
        var body = template.Body
            .Replace("{displayName}", displayName, StringComparison.Ordinal)
            .Replace("{link}", link, StringComparison.Ordinal)
            .Replace("{hours}", hours, StringComparison.Ordinal);
        return new EmailMessage(to, template.Subject, body);
    }

    private static string Key(AccountEmailKind kind) => kind switch
    {
        AccountEmailKind.EmailCheck => "email_check",
        AccountEmailKind.PasswordReset => "password_reset",
        AccountEmailKind.AccountExists => "account_exists",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The email kind is not known."),
    };

    private static FrozenDictionary<string, FrozenDictionary<string, EmailTemplate>> LoadTemplates()
    {
        var assembly = typeof(AccountEmails).Assembly;
        var result = new Dictionary<string, FrozenDictionary<string, EmailTemplate>>(StringComparer.Ordinal);
        foreach (var language in new[] { "en", "fr" })
        {
            using var stream = assembly.GetManifestResourceStream($"Emails/{language}.json")
                ?? throw new InvalidOperationException($"The assembly has no email texts for the language \"{language}\".");
            var templates = JsonSerializer.Deserialize<Dictionary<string, EmailTemplate>>(stream, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException($"The email texts for the language \"{language}\" are empty.");
            result[language] = templates.ToFrozenDictionary(StringComparer.Ordinal);
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private sealed record EmailTemplate(string Subject, string Body);
}
