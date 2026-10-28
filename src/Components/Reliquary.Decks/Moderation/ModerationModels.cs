using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Decks.Moderation;

/// <summary>
/// The settings of the moderation. The section is <c>Moderation</c>.
/// </summary>
public sealed class ModerationOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Moderation";

    /// <summary>Gets or sets the maximum number of reports of one user in 24 hours. The default is 20.</summary>
    public int ReportsPerDay { get; set; } = 20;
}

/// <summary>
/// The values of the moderation: item types, reasons, statuses and actions.
/// </summary>
public static class ModerationValues
{
    /// <summary>A public deck.</summary>
    public const string Deck = "deck";

    /// <summary>The profile of an author.</summary>
    public const string Author = "author";

    /// <summary>The maximum number of characters of the comment of a report and of the reason of an action.</summary>
    public const int MaxCommentLength = 500;

    /// <summary>The item types.</summary>
    public static readonly IReadOnlyList<string> ItemTypes = [Deck, Author];

    /// <summary>The reason codes of a report.</summary>
    public static readonly IReadOnlyList<string> Reasons = ["spam", "offensive", "copyright", "other"];

    /// <summary>The statuses of a report.</summary>
    public static readonly IReadOnlyList<string> Statuses = ["open", "resolved", "dismissed"];
}

/// <summary>
/// The stable error codes of the moderation endpoints.
/// </summary>
public static class ModerationErrorCodes
{
    /// <summary>The user has an open report about the item.</summary>
    public const string ReportExists = "report_exists";

    /// <summary>An administrator closed the report before.</summary>
    public const string ReportClosed = "report_closed";
}

/// <summary>The body of <c>POST /reports</c>.</summary>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the deck or the account ID of the author.</param>
/// <param name="Reason"><c>spam</c>, <c>offensive</c>, <c>copyright</c> or <c>other</c>.</param>
/// <param name="Comment">A comment of 500 characters or fewer.</param>
public sealed record CreateReportRequest(string? ItemType, Guid? ItemId, string? Reason, string? Comment);

/// <summary>A report, as its author sees it.</summary>
/// <param name="Id">The ID of the report.</param>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the item.</param>
/// <param name="Reason">The reason code.</param>
/// <param name="Comment">The comment.</param>
/// <param name="Status"><c>open</c>, <c>resolved</c> or <c>dismissed</c>.</param>
/// <param name="CreatedAt">The time of the report.</param>
public sealed record ReportResponse(Guid Id, string ItemType, Guid ItemId, string Reason, string Comment, string Status, DateTimeOffset CreatedAt);

/// <summary>A user in the moderation data. The name is empty when the account does not exist.</summary>
/// <param name="Id">The account ID.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record ModerationUserResponse(Guid Id, string DisplayName);

/// <summary>A report, as an administrator sees it.</summary>
/// <param name="Id">The ID of the report.</param>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the item.</param>
/// <param name="ItemName">The name of the deck at the time of the report, or the display name of the author.</param>
/// <param name="Owner">The owner of the item.</param>
/// <param name="Reporter">The user who sent the report.</param>
/// <param name="Reason">The reason code.</param>
/// <param name="Comment">The comment.</param>
/// <param name="Status"><c>open</c>, <c>resolved</c> or <c>dismissed</c>.</param>
/// <param name="ItemHidden">Whether the item is hidden now.</param>
/// <param name="CreatedAt">The time of the report.</param>
/// <param name="ClosedAt">The time when an administrator closed the report, or <see langword="null"/>.</param>
public sealed record ModerationReportResponse(
    Guid Id,
    string ItemType,
    Guid ItemId,
    string ItemName,
    ModerationUserResponse Owner,
    ModerationUserResponse Reporter,
    string Reason,
    string Comment,
    string Status,
    bool ItemHidden,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>The body of the actions of an administrator.</summary>
/// <param name="Reason">The reason of the action, 500 characters or fewer. The moderation log keeps it.</param>
public sealed record ModerationActionRequest(string? Reason);

/// <summary>A hidden item.</summary>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the item.</param>
/// <param name="ItemName">The name of the deck or of the author when the administrator hid it.</param>
/// <param name="Owner">The owner of the item.</param>
/// <param name="Reason">The reason of the administrator.</param>
/// <param name="HiddenAt">The time of the action.</param>
public sealed record HiddenItemResponse(string ItemType, Guid ItemId, string ItemName, ModerationUserResponse Owner, string Reason, DateTimeOffset HiddenAt);

/// <summary>An entry of the moderation log.</summary>
/// <param name="Id">The ID of the entry.</param>
/// <param name="Action"><c>hide</c>, <c>show</c>, <c>resolve</c> or <c>dismiss</c>.</param>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the item, or <see langword="null"/> after the deletion of the account of the owner.</param>
/// <param name="ItemName">The name of the item, or an empty text after the deletion of the account of the owner.</param>
/// <param name="Moderator">The administrator, or <see langword="null"/> after the deletion of the account of the administrator.</param>
/// <param name="ReportId">The report of the action, or <see langword="null"/>.</param>
/// <param name="Reason">The reason of the administrator.</param>
/// <param name="CreatedAt">The time of the action.</param>
public sealed record ModerationLogEntryResponse(
    Guid Id,
    string Action,
    string ItemType,
    Guid? ItemId,
    string ItemName,
    ModerationUserResponse? Moderator,
    Guid? ReportId,
    string Reason,
    DateTimeOffset CreatedAt);

/// <summary>An administrator hid a public deck or an author profile.</summary>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the item.</param>
[DomainEvent("decks.content.hidden", 1)]
public sealed record ContentHidden(string ItemType, Guid ItemId) : IDomainEvent;

/// <summary>An administrator showed a hidden deck or author profile again.</summary>
/// <param name="ItemType"><c>deck</c> or <c>author</c>.</param>
/// <param name="ItemId">The ID of the item.</param>
[DomainEvent("decks.content.shown", 1)]
public sealed record ContentShown(string ItemType, Guid ItemId) : IDomainEvent;
