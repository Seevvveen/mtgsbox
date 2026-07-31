#nullable enable

using System;
namespace Sandbox.Classes;

/// <summary>
///     Public, synchronized state for one player in an MTG match. Submitted deck
///     contents remain host-only in <see cref = "GameDirector"/>.
/// </summary>
public sealed class PlayerSeat : Component
{
	[Sync] public int Index { get; set; }

	[Sync] public Guid PlayerId { get; set; }

	[Sync] public bool Connected { get; set; }

	[Sync] public bool IsBot { get; set; }

	[Sync] public string BotName { get; set; } = string.Empty;

	[Sync] public bool Ready { get; set; }

	[Sync] public bool DeckAccepted { get; set; }

	[Sync] public string DeckStatus { get; set; } = string.Empty;

	[Sync] public int Life { get; set; } = 20;

	[Sync] public int Poison { get; set; }

	[Sync] public PlayerOutcome Outcome { get; set; }

	[Sync] public int MulliganCount { get; set; }

	[Sync] public bool MulliganComplete { get; set; }

	//
	// Zones
	// 
	[Sync] public Guid LibraryZoneId     { get; set; }
	[Sync] public Guid HandZoneId        { get; set; }
	[Sync] public Guid BattlefieldZoneId { get; set; }
	[Sync] public Guid GraveyardZoneId   { get; set; }
	[Sync] public Guid ExileZoneId       { get; set; }
	[Sync] public Guid CommandZoneId     { get; set; }


	//
	// Helpers: Occupied, IsLocal, IsEliminated, DisplayName
	// 
	public bool Occupied
	{
		get { return PlayerId != Guid.Empty; }
	}

	public bool IsLocal
	{
		get { return Occupied && Connection.Local is { } local && local.Id == PlayerId; }
	}

	public bool IsEliminated
	{
		get { return Outcome is PlayerOutcome.Lost or PlayerOutcome.Conceded; }
	}

	public string DisplayName
	{
		get { return IsBot && !string.IsNullOrWhiteSpace( BotName )? BotName : Connection.Find( PlayerId )?.DisplayName ?? $"Player {Index + 1}"; }
	}


	// Helper to Resolve players zones ie player.Zone(ZoneType.Hand) gets the hand
	public ZoneObject? Zone( ZoneType type )
	{
		return ZoneObject.Find(
							   type switch
							   {
								   ZoneType.Library     => LibraryZoneId,
								   ZoneType.Hand        => HandZoneId,
								   ZoneType.Battlefield => BattlefieldZoneId,
								   ZoneType.Graveyard   => GraveyardZoneId,
								   ZoneType.Exile       => ExileZoneId,
								   ZoneType.Command     => CommandZoneId,
								   _                    => Guid.Empty
							   }
							  );
	}
}
