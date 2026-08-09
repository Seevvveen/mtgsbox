#nullable enable

namespace Sandbox.Classes.Deck.Import;

public enum DeckImportIssueSeverity
{
	Warning,
	Error
}

public enum DeckImportIssueCode
{
	InvalidLine,
	InvalidQuantity,
	CardNotFound,
	AmbiguousCard
}

public sealed record DeckImportIssue
{
	public          DeckImportIssueSeverity Severity   { get; init; }
	public          DeckImportIssueCode     Code       { get; init; }
	public          int?                    LineNumber { get; init; }
	public          string?                 RawText    { get; init; }
	public required string                  Message    { get; init; }
}

public sealed record DeckImportResult
{
	public required Deck                  Deck   { get; init; }
	public          List<DeckImportIssue> Issues { get; init; } = [ ];

	public bool HasErrors
	{
		get { return Issues.Exists( issue => issue.Severity == DeckImportIssueSeverity.Error ); }
	}
}

public sealed record DeckImportOptions
{
	public string      DeckName   { get; init; } = "Imported Deck";
	public string?     FormatCode { get; init; }
	public DeckSource? Source     { get; init; }
}
