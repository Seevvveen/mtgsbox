using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.ManaSymbols;
namespace Sandbox.Classes.Database.Types;

public sealed record CardSymbolDefinition
{
	public required SymbolIdentifier Id { get; init; }
	public required string SvgUri { get; init; }
	public required string English { get; init; }

	public bool RepresentsMana { get; init; }
	public bool AppearsInManaCosts { get; init; }
	public bool Hybrid { get; init; }
	public bool Phyrexian { get; init; }
	public bool Funny { get; init; }

	public decimal? ManaValue { get; init; }
	public ColorSet Colors { get; init; }
}
