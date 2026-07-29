using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.CardFrames;
using Sandbox.Classes.Cards.CardTypes;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.Legality;
using Sandbox.Classes.Cards.ManaSymbols;
using System;
namespace Sandbox.Classes.Database.Types;

public sealed record NormalizedCard
{
	public required CardGameplayData Gameplay { get; init; }
	public required CardPresentationData Presentation { get; init; }
}

public sealed record CardGameplayData
{
	public Guid ScryfallId { get; init; }
	public Guid? OracleId { get; init; }
	public CardLayout Layout { get; init; }
	public CardFace[] Faces { get; init; } = [];
	public decimal ManaValue { get; init; }
	public ColorSet ColorIdentity { get; init; }
	public ColorSet? Colors { get; init; }
	public CardDefense Defense { get; init; }
	public HandModifer HandModifer { get; init; }
	public CardKeywords Keywords { get; init; }
	public FormatLegalities Legalities { get; init; }
	public LifeModifer LifeModifer { get; init; }
	public CardLoyalty Loyalty { get; init; }
	public ManaCost ManaCost { get; init; }
	public string Name { get; init; } = "";
	public string OracleText { get; init; } = "";
	public CardPower Power { get; init; }
	public ProducedManaSet? ProducedMana { get; init; }
	public CardToughness Toughness { get; init; }
	public CardType[] Types { get; init; } = [];
}

public sealed record CardPresentationData
{
	public BorderColor BorderColor { get; init; }
	public Guid? CardBack { get; init; }
	public CardFinish[] Finishes { get; init; } = [];
	public FrameEffect[] FrameEffects { get; init; } = [];
	public string? FlavorName { get; init; }
	public string? FlavorText { get; init; }
	public CardFrame Frame { get; init; }
	public bool FullArt { get; init; }
	public CardImages Images { get; init; }
	public bool Oversized { get; init; }
	public string? PrintedName { get; init; }
	public string? PrintedText { get; init; }
	public string? PrintedTypeLine { get; init; }
	public CardRarity Rarity { get; init; }
}

