#nullable enable

using System;
namespace Sandbox.Framework;

/// <summary>
///     The type of object placed onto the MTG stack.
/// </summary>
public enum StackObjectType
{
	Spell,
	ActivatedAbility,
	TriggeredAbility
}

/// <summary>
///     Public synchronized representation of one object on the MTG stack.
///     The authoritative effect data should remain in the rules system.
///     This component primarily exposes ordering, ownership, targeting,
///     and presentation information.
/// </summary>
public sealed class StackObject : Component
{
	[Sync] public Guid            StackObjectId      { get; set; }
	[Sync] public int             Sequence           { get; set; } //Increasing sequence used to maintain stack order.
	[Sync] public StackObjectType Type               { get; set; }
	[Sync] public Guid            ControllerPlayerId { get; set; }
	[Sync] public Guid            SourceCardId       { get; set; }
	[Sync] public string          DisplayName        { get; set; } = string.Empty;
	[Sync] public bool            IsResolving        { get; set; }
}

/// <summary>
///     Host-authoritative manager for objects on the MTG stack.
///     Objects are stored as networked child components. The object with the
///     highest sequence number is the top of the stack.
/// </summary>
public sealed class StackManager : Component
{
	private int _nextSequence;

	public PriorityManager Priority
	{
		get { return Scene.Get<PriorityManager>() ?? throw new InvalidOperationException( "The scene has no priority manager." ); }
	}

	public IReadOnlyList<StackObject> Objects
	{
		get { return GameObject.GetComponentsInChildren<StackObject>().OrderBy( item => item.Sequence ).ToArray(); }
	}

	public bool HasObjects
	{
		get { return Objects.Count > 0; }
	}

	public int Count
	{
		get { return Objects.Count; }
	}

	public StackObject? Top
	{
		get { return Objects.LastOrDefault(); }
	}


	protected override void OnStart()
	{
		if ( !Networking.IsHost )
			return;

		RebuildSequenceCounter();
	}



	// Places a spell onto the top of the stack.
	public StackObject? PushSpell( PlayerSeat controller, Guid sourceCardId, string displayName )
	{
		return Push( controller, StackObjectType.Spell, sourceCardId, displayName );
	}


	// Places an activated ability onto the top of the stack.
	public StackObject? PushActivatedAbility( PlayerSeat controller, Guid sourceCardId, string displayName )
	{
		return Push( controller, StackObjectType.ActivatedAbility, sourceCardId, displayName );
	}


	//Places a triggered ability onto the top of the stack.
	public StackObject? PushTriggeredAbility( PlayerSeat controller, Guid sourceCardId, string displayName )
	{
		return Push( controller, StackObjectType.TriggeredAbility, sourceCardId, displayName );
	}


	/// <summary>
	///     Places a new object on top of the stack.
	///     This should only be called after costs, modes, targets, and other
	///     casting or activation requirements have been validated.
	/// </summary>
	public StackObject? Push( PlayerSeat controller, StackObjectType type, Guid sourceCardId, string displayName )
	{
		if ( !Networking.IsHost )
			return null;

		if ( !CanAddObject( controller ) )
			return null;

		GameObject stackGameObject = new GameObject( GameObject, true, $"Stack Object {_nextSequence}" );

		StackObject stackObject = stackGameObject.Components.Create<StackObject>();

		stackObject.StackObjectId      = Guid.NewGuid();
		stackObject.Sequence           = _nextSequence++;
		stackObject.Type               = type;
		stackObject.ControllerPlayerId = controller.ParticipantId;
		stackObject.SourceCardId       = sourceCardId;
		stackObject.DisplayName        = displayName;
		stackObject.IsResolving        = false;

		if ( Networking.IsActive )
			stackGameObject.NetworkSpawn();

		/*
		 * Casting a spell or activating an ability resets consecutive passes.
		 * The acting player receives priority again after completing the action.
		 */
		Priority.OnPriorityActionTaken( controller );

		OnObjectAdded( stackObject );

		return stackObject;
	}


	public bool ResolveTop()
	{
		if ( !Networking.IsHost )
			return false;

		StackObject? stackObject = Top;

		if ( stackObject is null )
			return false;

		stackObject.IsResolving = true;

		bool resolved = ResolveObject( stackObject );

		if ( resolved )
		{
			OnObjectResolved( stackObject );
			RemoveObject( stackObject );

			return true;
		}

		stackObject.IsResolving = false;

		return false;
	}


	public bool Counter( Guid stackObjectId )
	{
		if ( !Networking.IsHost )
			return false;

		StackObject? stackObject = Find( stackObjectId );

		if ( stackObject is null )
			return false;

		OnObjectCountered( stackObject );
		RemoveObject( stackObject );

		return true;
	}


	public bool Remove( Guid stackObjectId )
	{
		if ( !Networking.IsHost )
			return false;

		StackObject? stackObject = Find( stackObjectId );

		if ( stackObject is null )
			return false;

		RemoveObject( stackObject );

		return true;
	}


	public StackObject? Find( Guid stackObjectId )
	{
		if ( stackObjectId == Guid.Empty )
			return null;

		return Objects.FirstOrDefault( item => item.StackObjectId == stackObjectId );
	}


	public void Clear()
	{
		if ( !Networking.IsHost )
			return;

		foreach ( StackObject stackObject in Objects )
			RemoveObject( stackObject );

		_nextSequence = 0;
	}


	private bool CanAddObject( PlayerSeat controller )
	{
		if ( !controller.IsOccupied )
			return false;

		if ( controller.IsEliminated )
			return false;

		/*
		 * Normal spells and activated abilities require priority.
		 * Triggered abilities may be added by the rules engine separately.
		 */
		return Priority.HasPriority( controller );
	}


	private void RemoveObject( StackObject stackObject )
	{
		stackObject.GameObject.Destroy();
	}


	private void RebuildSequenceCounter()
	{
		int highestSequence = -1;

		foreach ( StackObject stackObject in Objects )
		{
			if ( stackObject.Sequence > highestSequence )
				highestSequence = stackObject.Sequence;
		}

		_nextSequence = highestSequence + 1;
	}


	/// <summary>
	///     Executes the authoritative effect represented by the stack object.
	///     Replace this method with a call into your rules/effect engine.
	/// </summary>
	private bool ResolveObject( StackObject stackObject )
	{
		switch ( stackObject.Type )
		{
			case StackObjectType.Spell: return ResolveSpell( stackObject );

			case StackObjectType.ActivatedAbility: return ResolveActivatedAbility( stackObject );

			case StackObjectType.TriggeredAbility: return ResolveTriggeredAbility( stackObject );

			default: throw new ArgumentOutOfRangeException( nameof(stackObject.Type), stackObject.Type, null );
		}
	}


	private bool ResolveSpell( StackObject stackObject )
	{
		/*
		 * Future implementation:
		 *
		 * 1. Find the authoritative spell/card instance.
		 * 2. Confirm all targets are still legal.
		 * 3. Counter the spell if every required target is illegal.
		 * 4. Execute the spell instructions.
		 * 5. Move permanent spells to the battlefield.
		 * 6. Move instant/sorcery spells to their owner's graveyard.
		 */

		Log.Info( $"Resolving spell: {stackObject.DisplayName}" );

		return true;
	}


	private bool ResolveActivatedAbility( StackObject stackObject )
	{
		/*
		 * Future implementation:
		 *
		 * 1. Find the stored ability definition.
		 * 2. Recheck targets.
		 * 3. Execute the ability's effects.
		 *
		 * The source permanent does not need to remain on the battlefield
		 * unless the ability explicitly requires source information.
		 */

		Log.Info( $"Resolving activated ability: {stackObject.DisplayName}" );

		return true;
	}


	private bool ResolveTriggeredAbility( StackObject stackObject )
	{
		/*
		 * Future implementation:
		 *
		 * 1. Find the queued trigger instance.
		 * 2. Recheck targets.
		 * 3. Execute the trigger's effects.
		 */

		Log.Info( $"Resolving triggered ability: {stackObject.DisplayName}" );

		return true;
	}


	private void OnObjectAdded( StackObject stackObject )
	{
		Log.Info( $"Added {stackObject.DisplayName} to the stack." );
	}


	private void OnObjectResolved( StackObject stackObject )
	{
		Log.Info( $"Resolved {stackObject.DisplayName}." );
	}


	private void OnObjectCountered( StackObject stackObject )
	{
		Log.Info( $"Countered {stackObject.DisplayName}." );
	}
}
