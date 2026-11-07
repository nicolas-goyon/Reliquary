using Reliquary.Decks.Api;

namespace Reliquary.Decks.Versions;

/// <summary>A version of a deck.</summary>
/// <param name="Id">The ID of the version.</param>
/// <param name="Number">The number of the version in its deck: 1, 2, 3 and more.</param>
/// <param name="Kind"><c>commit</c>, <c>merge</c>, <c>revert</c> or <c>restore</c>.</param>
/// <param name="Message">The message.</param>
/// <param name="ParentIds">The parent versions: none for the first version, two for a merge.</param>
/// <param name="CreatedAt">The time of the version.</param>
/// <param name="CardCount">The number of cards in the commander, companion and main zones.</param>
/// <param name="Assembled">Whether the version is the assembled version of the deck.</param>
public sealed record DeckVersionResponse(Guid Id, int Number, string Kind, string Message, IReadOnlyList<Guid> ParentIds, DateTimeOffset CreatedAt, int CardCount, bool Assembled);

/// <summary>The history of a deck.</summary>
/// <param name="Versions">The versions, the most recent first.</param>
/// <param name="WorkingListChanged">Whether the working list differs from the most recent version.</param>
/// <param name="HeadVersionId">The most recent version, or <see langword="null"/>.</param>
/// <param name="AssembledVersionId">The assembled version, or <see langword="null"/>.</param>
public sealed record DeckHistoryResponse(IReadOnlyList<DeckVersionResponse> Versions, bool WorkingListChanged, Guid? HeadVersionId, Guid? AssembledVersionId);

/// <summary>A frozen line of a version.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Note">The note.</param>
/// <param name="Categories">The categories.</param>
/// <param name="Tags">The names of the local tags.</param>
/// <param name="GlobalTags">The names of the global tags.</param>
/// <param name="PurchasePreferences">The purchase preferences of the line, or <see langword="null"/> when the line accepts each copy.</param>
public sealed record DeckVersionLineResponse(Guid OracleId, Guid? PrintingId, string? Finish, int Quantity, string Zone, string Note, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags, IReadOnlyList<string> GlobalTags, Purchases.PurchasePreferences? PurchasePreferences = null);

/// <summary>A version with its lines and the data of its cards.</summary>
/// <param name="Version">The version.</param>
/// <param name="Lines">The frozen lines.</param>
/// <param name="Cards">The cards of the lines, by Oracle ID.</param>
/// <param name="Printings">The printings of the lines and the default printings of the cards, by ID.</param>
public sealed record DeckVersionDetailResponse(DeckVersionResponse Version, IReadOnlyList<DeckVersionLineResponse> Lines, IReadOnlyDictionary<Guid, DeckCardResponse> Cards, IReadOnlyDictionary<Guid, DeckPrintingResponse> Printings);

/// <summary>A change of the quantity of a card in a zone.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Before">The quantity in the first list.</param>
/// <param name="After">The quantity in the second list.</param>
public sealed record QuantityChangeResponse(Guid OracleId, string Zone, int Before, int After);

/// <summary>Copies of one printing and finish.</summary>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Quantity">The quantity.</param>
public sealed record PrintingCopiesResponse(Guid? PrintingId, string? Finish, int Quantity);

/// <summary>A change of the printings of a card in a zone, with the same quantity.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Before">The printings in the first list.</param>
/// <param name="After">The printings in the second list.</param>
public sealed record PrintingChangeResponse(Guid OracleId, string Zone, IReadOnlyList<PrintingCopiesResponse> Before, IReadOnlyList<PrintingCopiesResponse> After);

/// <summary>A change of the note of a card in a zone.</summary>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Before">The note in the first list.</param>
/// <param name="After">The note in the second list.</param>
public sealed record NoteChangeResponse(Guid OracleId, string Zone, string Before, string After);

/// <summary>The differences between two lists.</summary>
/// <param name="Quantities">The added and removed cards.</param>
/// <param name="Printings">The changes of printing or finish only. The main difference does not show them.</param>
/// <param name="Notes">The changes of the notes.</param>
/// <param name="Cards">The cards of the changes, by Oracle ID.</param>
/// <param name="PrintingData">The printings of the changes, by ID.</param>
public sealed record ListChangesResponse(
    IReadOnlyList<QuantityChangeResponse> Quantities,
    IReadOnlyList<PrintingChangeResponse> Printings,
    IReadOnlyList<NoteChangeResponse> Notes,
    IReadOnlyDictionary<Guid, DeckCardResponse> Cards,
    IReadOnlyDictionary<Guid, DeckPrintingResponse> PrintingData);

/// <summary>A variant of a deck.</summary>
/// <param name="Id">The deck ID of the variant. The endpoints of the decks work with it.</param>
/// <param name="Name">The name.</param>
/// <param name="BaseVersionId">The version of the main deck where the variant starts.</param>
/// <param name="HeadVersionId">The most recent version of the variant.</param>
/// <param name="UpdatedAt">The time of the last change.</param>
public sealed record DeckVariantResponse(Guid Id, string Name, Guid? BaseVersionId, Guid? HeadVersionId, DateTimeOffset UpdatedAt);

/// <summary>The body of a commit, a restore, a revert or a merge.</summary>
/// <param name="Message">The message of the new version. Some operations have a default message.</param>
public sealed record VersionMessageRequest(string? Message);

/// <summary>The body of a new variant.</summary>
/// <param name="Name">The name of the variant, for example "Budget".</param>
/// <param name="BaseVersionId">The version of the main deck where the variant starts. The default is the most recent version.</param>
public sealed record CreateVariantRequest(string? Name, Guid? BaseVersionId);

/// <summary>The body of the assembly of a deck.</summary>
/// <param name="VersionId">The assembled version. The default is the most recent version, after a commit of the working list when it changed.</param>
public sealed record AssembleDeckRequest(Guid? VersionId);

/// <summary>The assembled version of a deck.</summary>
/// <param name="DeckId">The deck.</param>
/// <param name="AssembledVersionId">The assembled version, or <see langword="null"/> when the deck is not assembled.</param>
public sealed record AssembledDeckResponse(Guid DeckId, Guid? AssembledVersionId);
