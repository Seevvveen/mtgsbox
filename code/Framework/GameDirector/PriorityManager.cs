#nullable enable

using System;
namespace Sandbox.Framework;

/// <summary>
///     Host-authoritative MTG priority cycle.
///     Priority begins with a selected player, rotates through eligible players,
///     tracks consecutive passes, and reports when every eligible player has passed.
/// </summary>
public sealed class PriorityManager : Component
{
	public PlayerRoster Roster
	{
		get { return Scene.Get<PlayerRoster>() ?? throw new InvalidOperationException( "The scene has no player roster." ); }
	}

	public TurnManager Turns
	{
		get { return Scene.Get<TurnManager>() ?? throw new InvalidOperationException( "The scene has no turn manager." ); }
	}

	public StackManager Stack
	{
		get { return Scene.Get<StackManager>() ?? throw new InvalidOperationException( "The scene has no stack manager." ); }
	}

	[Property] public float PriorityTimeLimit { get; set; } = 60.0f;

	[Sync] public Guid PriorityPlayerId { get; set; }

	[Sync] public int ConsecutivePassCount { get; set; }

	[Sync] public TimeUntil PriorityExpires { get; set; }

	public bool IsActive
	{
		get { return PriorityPlayerId != Guid.Empty; }
	}

	public PlayerSeat? PriorityPlayer
	{
		get { return Roster.SeatOf( PriorityPlayerId ); }
	}


	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		UpdateTimer();
	}


	/// <summary>
	///     Starts a new priority cycle with the supplied player.
	/// </summary>
	public void Begin( PlayerSeat firstPlayer )
	{
		if ( !Networking.IsHost )
			return;

		if ( !CanReceivePriority( firstPlayer ) )
			return;

		PriorityPlayerId     = firstPlayer.ParticipantId;
		ConsecutivePassCount = 0;

		ResetTimer();
	}


	/// <summary>
	///     Stops the current priority cycle.
	/// </summary>
	public void End()
	{
		if ( !Networking.IsHost )
			return;

		PriorityPlayerId     = Guid.Empty;
		ConsecutivePassCount = 0;
		PriorityExpires      = 0;
	}


	/// <summary>
	///     Restarts priority after a player places an object on the stack,
	///     activates an ability, or takes another priority action.
	///     The acting player receives priority again and previous passes are erased.
	/// </summary>
	public void ActionTaken( PlayerSeat player )
	{
		if ( !Networking.IsHost )
			return;

		if ( player.ParticipantId != PriorityPlayerId )
			return;

		PriorityPlayerId     = player.ParticipantId;
		ConsecutivePassCount = 0;

		ResetTimer();
	}


	/// <summary>
	///     Passes priority from the current player to the next eligible player.
	/// </summary>
	public void Pass( PlayerSeat player )
	{
		if ( !Networking.IsHost )
			return;

		if ( player.ParticipantId != PriorityPlayerId )
			return;

		if ( !CanReceivePriority( player ) )
			return;

		ConsecutivePassCount++;

		int eligibleCount = CountEligiblePlayers();

		if ( eligibleCount <= 0 )
		{
			End();

			return;
		}

		if ( ConsecutivePassCount >= eligibleCount )
		{
			HandleEveryonePassed();

			return;
		}

		PlayerSeat? nextPlayer = FindNextPlayer( player );

		if ( nextPlayer is null )
		{
			HandleEveryonePassed();

			return;
		}

		PriorityPlayerId = nextPlayer.ParticipantId;

		ResetTimer();
	}


	/// <summary>
	///     Returns true when the supplied player currently holds priority.
	/// </summary>
	public bool HasPriority( PlayerSeat player )
	{
		return IsActive && player.ParticipantId == PriorityPlayerId;
	}


	private void HandleEveryonePassed()
	{
		End();

		// TurnManager owns what happens next (resolve the stack and reopen
		// priority, or advance to the next phase) - don't duplicate that logic here.
		Turns.OnPriorityCycleCompleted();
	}


	private void UpdateTimer()
	{
		if ( !IsActive )
			return;

		if ( PriorityTimeLimit <= 0.0f )
			return;

		if ( !PriorityExpires )
			return;

		if ( PriorityPlayer is not { } player )
		{
			End();

			return;
		}

		Pass( player );
	}


	private void ResetTimer()
	{
		PriorityExpires = PriorityTimeLimit > 0.0f? PriorityTimeLimit : 0;
	}


	private PlayerSeat? FindNextPlayer( PlayerSeat current )
	{
		if ( current.Index < 0 )
			return null;

		for ( int offset = 1; offset <= Roster.Seats.Count; offset++ )
		{
			int index = ( current.Index + offset ) % Roster.Seats.Count;

			PlayerSeat candidate = Roster.Seats[index];

			if ( CanReceivePriority( candidate ) )
				return candidate;
		}

		return null;
	}


	private int CountEligiblePlayers()
	{
		int count = 0;

		foreach ( PlayerSeat seat in Roster.Seats )
		{
			if ( CanReceivePriority( seat ) )
				count++;
		}

		return count;
	}


	private static bool CanReceivePriority( PlayerSeat player )
	{
		return player.IsOccupied && !player.IsEliminated;
	}



	public void OnPriorityActionTaken( PlayerSeat player )
	{
		if ( !Networking.IsHost )
			return;

		if ( !HasPriority( player ) )
			return;

		ConsecutivePassCount = 0;
		PriorityPlayerId     = player.ParticipantId;
		ResetTimer();
	}
}
