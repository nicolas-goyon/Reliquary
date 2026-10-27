namespace Reliquary.Decks.Api;

/// <summary>The body of a new deck.</summary>
/// <param name="Name">The name of the deck.</param>
/// <param name="Format">The format, for example <c>commander</c>. The default is <c>commander</c>.</param>
/// <param name="Visibility">The visibility: <c>private</c>, <c>unlisted</c> or <c>public</c>. The default is <c>private</c>.</param>
/// <param name="FolderId">The folder of the deck, or <see langword="null"/>.</param>
/// <param name="Description">The description of the deck.</param>
/// <param name="Lines">The first lines of the deck.</param>
public sealed record CreateDeckRequest(string? Name, string? Format, string? Visibility, Guid? FolderId, string? Description, IReadOnlyList<LineRequest>? Lines);

/// <summary>The changes of a deck. A missing field does not change.</summary>
/// <param name="Name">The name.</param>
/// <param name="Format">The format.</param>
/// <param name="Visibility">The visibility.</param>
/// <param name="FolderId">The new folder.</param>
/// <param name="ClearFolder">Set to <see langword="true"/> to remove the deck from its folder.</param>
/// <param name="Description">The description.</param>
/// <param name="Pinned">Whether the deck is at the top of the list.</param>
/// <param name="Archived">Whether the deck is archived.</param>
/// <param name="ExpectedVersion">The version that the client read. The API refuses the change when the deck has another version.</param>
public sealed record UpdateDeckRequest(string? Name, string? Format, string? Visibility, Guid? FolderId, bool? ClearFolder, string? Description, bool? Pinned, bool? Archived, int? ExpectedVersion);

/// <summary>A line to add or to set.</summary>
/// <param name="OracleId">The Oracle card. Give the card or its name.</param>
/// <param name="Name">A name of the card: Oracle name, face name, localized name or alternate name.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/> for no specific printing.</param>
/// <param name="Finish">The finish: <c>nonfoil</c>, <c>foil</c> or <c>etched</c>, or <see langword="null"/>.</param>
/// <param name="Quantity">The quantity, from 1 to 999. The default is 1.</param>
/// <param name="Zone">The zone. The default is <c>main</c>.</param>
/// <param name="Note">A note.</param>
/// <param name="Tags">The local tags of the line, by name. The API creates the tags that do not exist.</param>
/// <param name="GlobalTags">The global tags of the line, by name. The API creates the tags that do not exist.</param>
/// <param name="Categories">The categories of the line, for example <c>ramp</c>.</param>
public sealed record LineRequest(
    Guid? OracleId,
    string? Name,
    Guid? PrintingId,
    string? Finish,
    int? Quantity,
    string? Zone,
    string? Note,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? GlobalTags = null,
    IReadOnlyList<string>? Categories = null);

/// <summary>The lines to add to a deck.</summary>
/// <param name="Lines">The lines.</param>
public sealed record AddLinesRequest(IReadOnlyList<LineRequest>? Lines);

/// <summary>All the lines of a deck.</summary>
/// <param name="Lines">The lines. The deck gets these lines only.</param>
/// <param name="ExpectedVersion">The version that the client read. The API refuses the change when the deck has another version.</param>
public sealed record ReplaceLinesRequest(IReadOnlyList<LineRequest>? Lines, int? ExpectedVersion);

/// <summary>The changes of a deck line. A missing field does not change.</summary>
/// <param name="Quantity">The quantity. 0 removes the line.</param>
/// <param name="Zone">The zone.</param>
/// <param name="PrintingId">The printing.</param>
/// <param name="ClearPrinting">Set to <see langword="true"/> to remove the printing.</param>
/// <param name="Finish">The finish.</param>
/// <param name="ClearFinish">Set to <see langword="true"/> to remove the finish.</param>
/// <param name="Note">The note.</param>
/// <param name="Tags">The local tags. The list replaces the local tags of the line.</param>
/// <param name="GlobalTags">The global tags. The list replaces the global tags of the line.</param>
/// <param name="Categories">The categories. The list replaces the categories of the line.</param>
public sealed record UpdateLineRequest(
    int? Quantity,
    string? Zone,
    Guid? PrintingId,
    bool? ClearPrinting,
    string? Finish,
    bool? ClearFinish,
    string? Note,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? GlobalTags = null,
    IReadOnlyList<string>? Categories = null);

/// <summary>A deck with its lines, and the cards and printings of the lines.</summary>
/// <param name="Id">The ID of the deck.</param>
/// <param name="OwnerId">The owner of the deck.</param>
/// <param name="Name">The name.</param>
/// <param name="Format">The format.</param>
/// <param name="Visibility">The visibility.</param>
/// <param name="FolderId">The folder, or <see langword="null"/>.</param>
/// <param name="Description">The description.</param>
/// <param name="SourceDeckId">The deck that the user copied, or <see langword="null"/>.</param>
/// <param name="Pinned">Whether the deck is at the top of the list.</param>
/// <param name="Archived">Whether the deck is archived.</param>
/// <param name="Version">The version. Each change increases it.</param>
/// <param name="CreatedAt">The time of the creation.</param>
/// <param name="UpdatedAt">The time of the last change.</param>
/// <param name="IsOwner">Whether the user owns the deck and can change it.</param>
/// <param name="Counts">The number of cards in each zone.</param>
/// <param name="Lines">The lines.</param>
/// <param name="Cards">The cards of the lines, by Oracle ID.</param>
/// <param name="Printings">The printings of the lines and the default printings of the cards, by ID.</param>
/// <param name="Tags">The tags of the lines, by ID.</param>
public sealed record DeckResponse(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Format,
    string Visibility,
    Guid? FolderId,
    string Description,
    Guid? SourceDeckId,
    bool Pinned,
    bool Archived,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsOwner,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<DeckLineResponse> Lines,
    IReadOnlyDictionary<Guid, DeckCardResponse> Cards,
    IReadOnlyDictionary<Guid, DeckPrintingResponse> Printings,
    IReadOnlyDictionary<Guid, DeckTagResponse> Tags);

/// <summary>A tag of a deck line.</summary>
/// <param name="Name">The name of the tag.</param>
/// <param name="Global">Whether the tag is a global tag of the user. A local tag belongs to the deck.</param>
public sealed record DeckTagResponse(string Name, bool Global);

/// <summary>A line of a deck.</summary>
/// <param name="Id">The ID of the line.</param>
/// <param name="OracleId">The Oracle card.</param>
/// <param name="PrintingId">The printing, or <see langword="null"/>.</param>
/// <param name="Finish">The finish, or <see langword="null"/>.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Zone">The zone.</param>
/// <param name="Note">The note.</param>
/// <param name="TagIds">The tags of the line. The deck response gives their names.</param>
/// <param name="Categories">The categories of the line.</param>
public sealed record DeckLineResponse(Guid Id, Guid OracleId, Guid? PrintingId, string? Finish, int Quantity, string Zone, string Note, IReadOnlyList<Guid> TagIds, IReadOnlyList<string> Categories);

/// <summary>A deck as text.</summary>
/// <param name="Text">The text, in the deck text format.</param>
/// <param name="Version">The version of the deck.</param>
public sealed record DeckTextResponse(string Text, int Version);

/// <summary>The new content of a deck as text.</summary>
/// <param name="Text">The text, in the deck text format.</param>
/// <param name="ExpectedVersion">The version that the client read. The API refuses the change when the deck has another version.</param>
/// <param name="DryRun">Set to <see langword="true"/> to check the text without a change of the deck.</param>
public sealed record SetDeckTextRequest(string? Text, int? ExpectedVersion, bool? DryRun);

/// <summary>A line of a deck text that the API cannot use.</summary>
/// <param name="Line">The number of the line. The first line has the number 1.</param>
/// <param name="Code">The reason code: <c>missing_name</c>, <c>quantity_out_of_range</c>, <c>tag_too_long</c>, <c>card_not_found</c> or <c>printing_not_found</c>.</param>
/// <param name="Text">The text of the line.</param>
public sealed record DeckTextLineError(int Line, string Code, string Text);

/// <summary>A global tag of the user.</summary>
/// <param name="Id">The ID of the tag.</param>
/// <param name="Name">The name.</param>
/// <param name="LineCount">The number of deck lines with the tag.</param>
public sealed record TagResponse(Guid Id, string Name, int LineCount);

/// <summary>A tag after a change of its name.</summary>
/// <param name="Id">The ID of the tag. After a merge, it is the ID of the other tag.</param>
/// <param name="Name">The name.</param>
public sealed record RenamedTagResponse(Guid Id, string Name);

/// <summary>The new name of a tag. When a tag of the same kind has this name, the two tags become one tag.</summary>
/// <param name="Name">The new name.</param>
public sealed record RenameTagRequest(string? Name);

/// <summary>The data of a card that the deck page shows.</summary>
/// <param name="Name">The Oracle name.</param>
/// <param name="Layout">The layout.</param>
/// <param name="ManaValue">The mana value of the rules.</param>
/// <param name="CurveManaValue">The mana value in the mana curve (ADR 0025).</param>
/// <param name="ManaCost">The mana cost of the primary face.</param>
/// <param name="TypeLine">The type line.</param>
/// <param name="ColorIdentity">The color identity.</param>
/// <param name="CardTypes">The card types of the castable faces.</param>
/// <param name="IsLand">Whether the card is a land.</param>
/// <param name="DefaultPrintingId">The default printing.</param>
/// <param name="Faces">The faces of the card.</param>
public sealed record DeckCardResponse(
    string Name,
    string Layout,
    decimal ManaValue,
    decimal CurveManaValue,
    string? ManaCost,
    string? TypeLine,
    IReadOnlyList<string> ColorIdentity,
    IReadOnlyList<string> CardTypes,
    bool IsLand,
    Guid? DefaultPrintingId,
    IReadOnlyList<DeckCardFaceResponse> Faces);

/// <summary>A face of a card.</summary>
/// <param name="Name">The name of the face.</param>
/// <param name="ManaCost">The mana cost.</param>
/// <param name="TypeLine">The type line.</param>
public sealed record DeckCardFaceResponse(string Name, string? ManaCost, string? TypeLine);

/// <summary>A printing of a card.</summary>
/// <param name="SetCode">The set code.</param>
/// <param name="CollectorNumber">The collector number.</param>
/// <param name="Lang">The language.</param>
/// <param name="ImageUri">The address of the image of the first face.</param>
public sealed record DeckPrintingResponse(string SetCode, string CollectorNumber, string Lang, string? ImageUri);

/// <summary>A deck in the deck list.</summary>
/// <param name="Id">The ID of the deck.</param>
/// <param name="Name">The name.</param>
/// <param name="Format">The format.</param>
/// <param name="Visibility">The visibility.</param>
/// <param name="FolderId">The folder.</param>
/// <param name="Pinned">Whether the deck is at the top of the list.</param>
/// <param name="Archived">Whether the deck is archived.</param>
/// <param name="CardCount">The number of cards in the commander, companion and main zones.</param>
/// <param name="CommanderIds">The Oracle IDs of the commanders.</param>
/// <param name="CreatedAt">The time of the creation.</param>
/// <param name="UpdatedAt">The time of the last change.</param>
public sealed record DeckSummaryResponse(
    Guid Id,
    string Name,
    string Format,
    string Visibility,
    Guid? FolderId,
    bool Pinned,
    bool Archived,
    int CardCount,
    IReadOnlyList<Guid> CommanderIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
