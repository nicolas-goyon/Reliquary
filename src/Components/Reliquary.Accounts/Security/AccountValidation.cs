using System.Net.Mail;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.Security;

/// <summary>
/// Checks the fields of the account requests.
/// </summary>
internal static class AccountValidation
{
    /// <summary>The minimum length of a password.</summary>
    public const int MinPasswordLength = 10;

    /// <summary>The maximum length of a password.</summary>
    public const int MaxPasswordLength = 128;

    /// <summary>The maximum length of a display name.</summary>
    public const int MaxDisplayNameLength = 50;

    /// <summary>The interface languages.</summary>
    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.Ordinal) { "en", "fr" };

    /// <summary>
    /// Normalizes an email address for the comparison: no spaces at the ends, lowercase.
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <returns>The normalized address.</returns>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static IEnumerable<FieldError> Email(string? email, string field = "email")
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            yield return new FieldError(field, FieldErrorCodes.Required, "The email address is necessary.");
        }
        else if (email.Length > 254 || !MailAddress.TryCreate(email.Trim(), out var address) || address.Address != email.Trim())
        {
            yield return new FieldError(field, FieldErrorCodes.InvalidFormat, "The email address does not have a correct format.");
        }
    }

    public static IEnumerable<FieldError> Password(string? password, string field = "password")
    {
        if (string.IsNullOrEmpty(password))
        {
            yield return new FieldError(field, FieldErrorCodes.Required, "The password is necessary.");
        }
        else if (password.Length < MinPasswordLength)
        {
            yield return new FieldError(field, FieldErrorCodes.TooShort, $"The password must have {MinPasswordLength} characters or more.");
        }
        else if (password.Length > MaxPasswordLength)
        {
            yield return new FieldError(field, FieldErrorCodes.TooLong, $"The password must have {MaxPasswordLength} characters or fewer.");
        }
    }

    public static IEnumerable<FieldError> DisplayName(string? displayName, bool required)
    {
        if (displayName is null)
        {
            if (required)
            {
                yield return new FieldError("displayName", FieldErrorCodes.Required, "The display name is necessary.");
            }
        }
        else if (string.IsNullOrWhiteSpace(displayName))
        {
            yield return new FieldError("displayName", FieldErrorCodes.TooShort, "The display name must contain a character that is not a space.");
        }
        else if (displayName.Trim().Length > MaxDisplayNameLength)
        {
            yield return new FieldError("displayName", FieldErrorCodes.TooLong, $"The display name must have {MaxDisplayNameLength} characters or fewer.");
        }
    }

    public static IEnumerable<FieldError> Language(string? language, bool required)
    {
        if (language is null)
        {
            if (required)
            {
                yield return new FieldError("language", FieldErrorCodes.Required, "The language is necessary.");
            }
        }
        else if (!Languages.Contains(language))
        {
            yield return new FieldError("language", FieldErrorCodes.NotAllowed, "The language must be \"en\" or \"fr\".");
        }
    }
}
