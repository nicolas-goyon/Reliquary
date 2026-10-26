using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Reliquary.BuildingBlocks.Email;

namespace Reliquary.Platform.Email;

/// <summary>
/// The settings of the emails. The section is <c>Email</c>.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Email";

    /// <summary>Gets or sets the sender address, for example <c>Reliquary &lt;no-reply@example.org&gt;</c>.</summary>
    public string From { get; set; } = "Reliquary <no-reply@localhost>";

    /// <summary>Gets or sets the SMTP server. Without a host, the platform writes the emails to the log.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>
/// The settings of the SMTP server.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>Gets or sets the host name. Without a value, the emails go to the log.</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Gets or sets the user name, when the server needs authentication.</summary>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the password, when the server needs authentication.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the TLS mode: <c>Auto</c>, <c>None</c>, <c>StartTls</c> or <c>SslOnConnect</c>.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;
}

/// <summary>
/// Sends the emails through an SMTP server.
/// </summary>
/// <param name="options">The settings of the emails.</param>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(settings.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient();
        var host = settings.Smtp.Host ?? throw new InvalidOperationException("The configuration has no SMTP host. Set Email:Smtp:Host.");
        await client.ConnectAsync(host, settings.Smtp.Port, settings.Smtp.Security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            await client.AuthenticateAsync(settings.Smtp.UserName, settings.Smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>
/// Writes the emails to the log, when the instance has no SMTP server. Use it only for development.
/// </summary>
/// <param name="logger">The logger.</param>
internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        logger.EmailWrittenToLog(message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}
