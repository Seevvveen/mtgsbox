#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Zones;
using System;

namespace Sandbox.Framework;

public enum SeatOutcome
{
	None,
	Win,
	Loss
}

/// <summary>
///     A player at the table and the owner of that player's zones.
/// </summary>
public sealed class Seat : Component
{
	[Property, Sync] public int Index { get; set; }
	[Sync] public Guid Player { get; set; }
	[Sync] public string PlayerName { get; set; } = "Player";
	[Sync] public bool IsConnected { get; set; }
	/// <summary>Optional format-defined team or shared-turn group.</summary>
	[Sync] public Guid ParticipantGroupId { get; set; }
	[Sync] public bool Ready { get; set; }
	[Sync] public bool HasSubmittedDeck { get; set; }
	[Sync] public string DeckName { get; set; } = string.Empty;
	[Sync] public int DeckCardCount { get; set; }
	[Sync] public string DeckError { get; set; } = string.Empty;
	[Sync] public string ActionError { get; set; } = string.Empty;
	[Sync] public CardObject? SelectedCard { get; internal set; }
	[Sync] public bool Eliminated { get; set; }
	[Sync] public bool IsBot { get; set; }
	[Sync] public SeatOutcome Outcome { get; set; }

	[Sync] public LibraryZone? Library { get; set; }
	[Sync] public HandZone? Hand { get; set; }
	[Sync] public BattlefieldZone? Battlefield { get; set; }
	[Sync] public GraveyardZone? Graveyard { get; set; }
	[Sync] public ExileZone? Exile { get; set; }
	[Sync] public CommandZone? Command { get; set; }
	[Sync] public SideboardZone? Sideboard { get; set; }

	public bool Occupied => Player != Guid.Empty;
	public bool IsLocal => Occupied && Player == Connection.Local.Id;

	/// <summary>
	///     The authoritative deck is intentionally kept on the host rather than
	///     replicated to every client. The synchronized fields above expose only
	///     the information needed by the lobby UI.
	/// </summary>
	public Deck? SubmittedDeck { get; internal set; }


	/// <summary>
	///     Creates all player-owned zones as children of this seat. The shared
	///     stack is intentionally match-owned and is not created here.
	/// </summary>
	public void CreateZones()
	{
		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Only seat authority can create player zones." );

		if ( Player == Guid.Empty )
			throw new InvalidOperationException( "Assign a player before creating their zones." );

		//HARDCODED :D
		Battlefield ??= CreateZone<BattlefieldZone>( "Battlefield", new Vector3( 0, 0, 0 ) );
		Library     ??= CreateZone<LibraryZone>( "Library", new Vector3( 0, 370, 0f ) );
		Command     ??= CreateZone<CommandZone>( "Command", new Vector3( 125, 370, 0f ) );
		Graveyard   ??= CreateZone<GraveyardZone>( "Graveyard", new Vector3( -125, 370, 0f ) );
		Exile       ??= CreateZone<ExileZone>( "Exile", new Vector3( -125, 500, 0f ) );
		Hand        ??= CreateZone<HandZone>( "Hand", new Vector3( -315, 0f, 0f ) );
		Sideboard   ??= CreateZone<SideboardZone>( "Sideboard", new Vector3( 0, -400, 0f ) );
	}


	private T CreateZone<T>( string role, Vector3 localOffset ) where T : ZoneObject, new()
	{
		GameObject zoneObject = new( GameObject, true, $"Player {Index + 1} {role}" );
		zoneObject.LocalPosition = localOffset;
		zoneObject.LocalRotation = Rotation.FromYaw( -90f );

		T zone = zoneObject.Components.Create<T>();
		zone.OwnerPlayerId = Player;
		zone.Role          = role;

		return zone;
	}
}
