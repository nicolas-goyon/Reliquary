namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The limits of the online games, in the section <c>Playtest</c>. All the API replicas must have the same values.
/// </summary>
public sealed class GameOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Playtest";

    /// <summary>Gets or sets the maximum number of games that one account creates in one day (UTC). The default is 20.</summary>
    public int GamesPerAccountPerDay { get; set; } = 20;

    /// <summary>Gets or sets the maximum number of open games of one host at the same time. The default is 3.</summary>
    public int OpenGamesPerAccount { get; set; } = 3;

    /// <summary>Gets or sets the maximum number of join requests from one IP address in one minute. The default is 20.</summary>
    public int JoinsPerIpPerMinute { get; set; } = 20;

    /// <summary>Gets or sets the minimum number of seats of a game. The default is 2.</summary>
    public int MinSeats { get; set; } = 2;

    /// <summary>Gets or sets the maximum number of seats of a game. The default is 4.</summary>
    public int MaxSeats { get; set; } = 4;

    /// <summary>Gets or sets the maximum size of a deck text, in UTF-8 bytes. The default is 10 KB.</summary>
    public int MaxDeckTextBytes { get; set; } = 10 * 1024;

    /// <summary>Gets or sets the maximum number of lines of a deck text. The default is 250.</summary>
    public int MaxDeckTextLines { get; set; } = 250;

    /// <summary>Gets or sets the maximum number of cards of a deck in a game. The default is 250.</summary>
    public int MaxDeckCards { get; set; } = 250;

    /// <summary>Gets or sets the hard limit of the lifetime of a game, from its creation. The default is 8 hours.</summary>
    public TimeSpan GameLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Gets or sets the lifetime of an invite link. The game start and the end of the game also stop the link. The default is 24 hours.</summary>
    public TimeSpan InviteLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets the number of actions that one connection to the table hub can send in one second, on average. The default is 10.</summary>
    public double ActionsPerSecond { get; set; } = 10;

    /// <summary>Gets or sets the number of actions that one connection to the table hub can send in a burst. The default is 30.</summary>
    public double ActionBurst { get; set; } = 30;

    /// <summary>Gets or sets the lifetime of a game token. The end of the game also stops the token. The default is 8 hours.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
}
