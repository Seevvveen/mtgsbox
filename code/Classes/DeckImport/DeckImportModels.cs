#nullable enable

using System;
namespace Sandbox.Classes.DeckImport;

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
	AmbiguousCard,
	UnsupportedWebsite,
	InvalidUrl,
	RequestFailed,
	InvalidResponse
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

public readonly record struct DeckCardQuery
{
	public          Guid?   ScryfallId      { get; init; }
	public required string  Name            { get; init; }
	public          string? SetCode         { get; init; }
	public          string? CollectorNumber { get; init; }
}

public sealed record DeckCardResolution
{
	public DeckCardReference? Card       { get; init; }
	public int                MatchCount { get; init; }

	public bool IsResolved
	{
		get { return Card is not null; }
	}

	public bool IsAmbiguous
	{
		get { return MatchCount > 1; }
	}
}

public interface IDeckCardResolver
{
	DeckCardResolution Resolve( DeckCardQuery query );
}
