using System;
namespace Sandbox.Framework;

/// <summary>
///     Manages Connections and Associated Seat Spawning
/// </summary>
public class PlayerRoster : Component, Component.INetworkListener
{
	public GameDirector Director
	{
		get { return Scene.Get<GameDirector>() ?? throw new InvalidOperationException( "The scene has no MTG game director." ); }
	}

	public Connection[] Connections { get; set; }

	public IReadOnlyList<PlayerSeat> Seats
	{
		get { return Scene.GetAllComponents<PlayerSeat>().OrderBy( seat => seat.Index ).ToArray(); }
	}

	public PlayerSeat? LocalSeat
	{
		get { return Seats.FirstOrDefault( seat => seat.IsLocal ); }
	}

	
	
	

	void INetworkListener.OnActive( Connection channel )
	{
		if ( Director.Format is null )
		{
			CreateSpectator();
			Log.Error( "Created Spectator due to no Format" );

			return;
		}

		if ( Connections.Length < Director.Format.MaximumPlayers )
		{
			if ( SeatPlayer( channel ) )
				Director.OnPlayerJoined( channel );
		}
		else
		{
			CreateSpectator();

			//Director.OnSpectatorJoined()
		}
	}


	void INetworkListener.OnDisconnected( Connection channel )
	{
		if ( SeatOf( channel.Id ) is not PlayerSeat seat )
			return;

		seat.IsConnected = false;
		seat.Ready       = false;
		Director.OnPlayerDisconnected( channel );
	}

	
	
	
	

	protected override void OnStart()
	{
		foreach ( Connection connection in Connection.All )
			SeatPlayer( connection );
	}


	public void CreateSpectator()
	{
		Log.Info( "Spectator created" );
	}


	public bool SeatPlayer( Connection connection )
	{
		if ( !Networking.IsHost )
			return false;

		if ( SeatOf( connection.Id ) is { } existing )
		{
			existing.IsConnected = true;

			return true;
		}

		//First free seat index
		int index = Enumerable.Range( 0, Director.Format?.MaximumPlayers ?? 2 ).FirstOrDefault( s => Seats.All( seat => seat.Index != s ) );

		GameObject seatObject = new GameObject( GameObject, true, $"Seat {index}" );
		PlayerSeat seat       = seatObject.Components.Create<PlayerSeat>();

		seat.Index         = index;
		seat.ParticipantId = connection.Id;
		seat.IsConnected   = true;
		seat.Life          = Director.Format?.StartingLife ?? 20;

		if ( Networking.IsActive )
			seatObject.NetworkSpawn();

		return true;
	}
	

	public PlayerSeat? SeatOf( Guid id )
	{
		return Seats.FirstOrDefault( seat => seat.ParticipantId == id );
	}


	public PlayerSeat? GetNextPlayer( Guid currentParticipantId, Func<PlayerSeat, bool>? predicate = null )
	{
		IReadOnlyList<PlayerSeat> seats = Seats;

		if ( seats.Count == 0 )
			return null;

		int currentIndex = -1;

		for ( int index = 0; index < seats.Count; index++ )
		{
			if ( seats[index].ParticipantId != currentParticipantId )
				continue;

			currentIndex = index;

			break;
		}

		if ( currentIndex < 0 )
			return null;

		predicate ??= static seat => seat.IsOccupied && !seat.IsEliminated;

		for ( int offset = 1; offset <= seats.Count; offset++ )
		{
			int        index     = ( currentIndex + offset ) % seats.Count;
			PlayerSeat candidate = seats[index];

			if ( predicate( candidate ) )
				return candidate;
		}

		return null;
	}
}
