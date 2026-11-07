namespace Reliquary.BuildingBlocks.Email;

/// <summary>
/// Sends emails to the users, for example the link to check an email address.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email.
    /// </summary>
    /// <param name="message">The email.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the mail server accepted the email.</returns>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// An email to one recipient.
/// </summary>
/// <param name="To">The address of the recipient.</param>
/// <param name="Subject">The subject.</param>
/// <param name="TextBody">The body as plain text.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody);
