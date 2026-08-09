#nullable enable

using Sandbox.Classes.Zones;
using Sandbox.Framework.GameInfo;
using System;
namespace Sandbox.Framework;

public enum DeckApproval
{
	None,
	Unknown,
	Rejected,
	Passed
}

/// <summary>
///     A Syncronized Particpate slot in the match
/// </summary>
public sealed class PlayerSeat : Component
{
	[Sync] public int Index { get; set; }

	[Sync] public Guid   ParticipantId { get; set; } // Stable ID to find player/bot
	[Sync] public string PlayerName    { get; set; } = string.Empty;
	[Sync] public bool   IsConnected   { get; set; }
	[Sync] public bool   IsBot         { get; set; }

	[Sync] public bool   Ready        { get; set; }
	[Sync] public bool   DeckAccepted { get; set; }
	[Sync] public string DeckStatus   { get; set; }


	[Sync] public int           Life             { get; set; } = 20;
	[Sync] public int           Poison           { get; set; }
	[Sync] public PlayerOutcome Outcome          { get; set; }
	[Sync] public int           MulliganCount    { get; set; }
	[Sync] public bool          MulliganComplete { get; set; }


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
	// Derived State Helpers
	// 
	public bool IsOccupied
	{
		get { return ParticipantId != Guid.Empty; }
	}

	public bool IsLocal
	{
		get { return IsOccupied && Connection.Local is { } local && local.Id == ParticipantId; }
	}

	public bool IsEliminated
	{
		get { return Outcome is PlayerOutcome.Lost or PlayerOutcome.Conceded; }
	}

	public string DisplayName
	{
		get { return !string.IsNullOrWhiteSpace( PlayerName )? PlayerName : $"Player {Index + 1}"; }
	}


	public Guid GetZoneId( ZoneType type )
	{
		return type switch
			   {
				   ZoneType.Library     => LibraryZoneId,
				   ZoneType.Hand        => HandZoneId,
				   ZoneType.Battlefield => BattlefieldZoneId,
				   ZoneType.Graveyard   => GraveyardZoneId,
				   ZoneType.Exile       => ExileZoneId,
				   ZoneType.Command     => CommandZoneId,
				   _                    => Guid.Empty
			   };
	}


	public ZoneObject? FindZone( ZoneType type )
	{
		Guid id = GetZoneId( type );

		return id == Guid.Empty? null : ZoneObject.Find( id );
	}
}
