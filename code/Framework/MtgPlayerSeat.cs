#nullable enable

using System;

namespace Sandbox.Classes;

/// <summary>
/// Public, synchronized state for one player in an MTG match. Submitted deck
/// contents remain host-only in <see cref="MtgGameDirector"/>.
/// </summary>
public sealed class MtgPlayerSeat : Component
{
	[Sync]
	public int Index { get; set; }

	[Sync]
	public Guid PlayerId { get; set; }

	[Sync]
	public bool Connected { get; set; }

	[Sync]
	public bool IsBot { get; set; }

	[Sync]
	public string BotName { get; set; } = string.Empty;

	[Sync]
	public bool Ready { get; set; }

	[Sync]
	public bool DeckAccepted { get; set; }

	[Sync]
	public string DeckStatus { get; set; } = string.Empty;

	[Sync]
	public int Life { get; set; } = 20;

	[Sync]
	public int Poison { get; set; }

	[Sync]
	public MtgPlayerOutcome Outcome { get; set; }

	[Sync]
	public int MulliganCount { get; set; }

	[Sync]
	public bool MulliganComplete { get; set; }

	[Sync]
	public Guid LibraryZoneId { get; set; }

	[Sync]
	public Guid HandZoneId { get; set; }

	[Sync]
	public Guid BattlefieldZoneId { get; set; }

	[Sync]
	public Guid GraveyardZoneId { get; set; }

	[Sync]
	public Guid ExileZoneId { get; set; }

	[Sync]
	public Guid CommandZoneId { get; set; }

	public bool Occupied => PlayerId != Guid.Empty;
	public bool IsLocal =>
		Occupied && Connection.Local is { } local &&
		local.Id == PlayerId;
	public bool IsEliminated =>
		Outcome is MtgPlayerOutcome.Lost or
			MtgPlayerOutcome.Conceded;

	public string DisplayName =>
		IsBot && !string.IsNullOrWhiteSpace( BotName )
			? BotName
			: Connection.Find( PlayerId )?.DisplayName ??
		$"Player {Index + 1}";

	public ZoneObject? Zone( MtgZoneKind kind ) =>
		ZoneObject.Find( kind switch
		{
			MtgZoneKind.Library => LibraryZoneId,
			MtgZoneKind.Hand => HandZoneId,
			MtgZoneKind.Battlefield => BattlefieldZoneId,
			MtgZoneKind.Graveyard => GraveyardZoneId,
			MtgZoneKind.Exile => ExileZoneId,
			MtgZoneKind.Command => CommandZoneId,
			_ => Guid.Empty
		});
}
