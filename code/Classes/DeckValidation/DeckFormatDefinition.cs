#nullable enable

using System;
namespace Sandbox.Classes.DeckValidation;

/// <summary>
///     Data-driven deck construction rules for one format. The format code remains
///     a string so newly introduced formats do not require a game-code update.
/// </summary>
public sealed record DeckFormatDefinition
{
	public required string Code        { get; init; }
	public required string DisplayName { get; init; }

	/// <summary>
	///     Optional Scryfall legality key when it differs from Code.
	/// </summary>
	public string? CardLegalityCode { get; init; }

	public int                                 DefaultCopyLimit    { get; init; } = 4;
	public Dictionary<string, DeckSectionRule> Sections            { get; init; } = new Dictionary<string, DeckSectionRule>( StringComparer.OrdinalIgnoreCase );
	public HashSet<Guid>                       BannedOracleIds     { get; init; } = [ ];
	public HashSet<Guid>                       RestrictedOracleIds { get; init; } = [ ];
	public Dictionary<Guid, int>               CardCopyLimits      { get; init; } = [ ];


	public static DeckFormatDefinition Constructed( string code, string displayName, int minimumMainDeckSize = 60, int maximumSideboardSize = 15 )
	{
		return new DeckFormatDefinition { Code = code, DisplayName = displayName, Sections = new Dictionary<string, DeckSectionRule>( StringComparer.OrdinalIgnoreCase ) { [DeckSections.Main] = new DeckSectionRule { MinimumCards = minimumMainDeckSize, CountsTowardCopyLimit = true }, [DeckSections.Sideboard] = new DeckSectionRule { MaximumCards = maximumSideboardSize, CountsTowardCopyLimit = true } } };
	}
}

public sealed record DeckSectionRule
{
	public int  MinimumCards          { get; init; }
	public int? MaximumCards          { get; init; }
	public bool CountsTowardCopyLimit { get; init; }
}

public enum DeckValidationIssueCode
{
	InvalidQuantity,
	UnknownSection,
	SectionTooSmall,
	SectionTooLarge,
	CardNotFound,
	PrintingIdentityMismatch,
	MissingOracleIdentity,
	CardNotLegal,
	CardBanned,
	CardRestricted,
	TooManyCopies,
	FormatRule
}

public sealed record DeckValidationIssue
{
	public          DeckValidationIssueCode Code       { get; init; }
	public required string                  Message    { get; init; }
	public          string?                 Section    { get; init; }
	public          Guid?                   ScryfallId { get; init; }
	public          Guid?                   OracleId   { get; init; }
}

public sealed record DeckValidationReport
{
	public required string                    FormatCode { get; init; }
	public          List<DeckValidationIssue> Issues     { get; init; } = [ ];

	public bool IsLegal
	{
		get { return Issues.Count == 0; }
	}
}

public interface IDeckFormatRule
{
	void Validate( Deck deck, DeckFormatDefinition format, DeckValidationReport report );
}
