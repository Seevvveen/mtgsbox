#nullable enable

using Sandbox.Classes.Decals;
using Sandbox.Classes.Zones;
using Sandbox.Framework;
using System;
namespace Sandbox.Classes.Cards;

/// <summary>
///     Local mouse interaction for the in-world MTG cards. Attach this component
///     to the local camera (or another local-only scene object).
/// </summary>
public sealed class CardHover : Component
{
	private Transform   _dragOrigin;
	private Vector3     _dragPosition;
	private Rotation    _dragRotation;
	private Vector3     _dragVelocity;
	private CardObject? _dropHighlight;

	private CardObject? _pending;
	private CardObject? _selected;

	private           Vector2 _pressPosition;
	[Property] public float   TraceDistance { get; set; } = 10000f;

	[Property] public float DragHeight { get; set; } = 6f;

	[Property] public float FollowSpeed { get; set; } = 18f;

	[Property] public float ThrowMinimumSpeed { get; set; } = 80f;

	[Property] public float ThrowVelocityScale { get; set; } = 1f;

	[Property] public float ThrowSpinScale { get; set; } = 0.05f;

	[Property] public float DragThresholdPixels { get; set; } = 6f;

	public CardObject? Hovered { get; private set; }

	public CardObject? Dragged { get; private set; }

	public ZoneObject? DropTarget { get; private set; }

	public bool IsDragging
	{
		get { return Dragged is not null; }
	}


	protected override void OnUpdate()
	{
		SynchronizeSelection();

		CameraComponent? camera = Scene.Camera;

		if ( camera is null )
			return;

		Ray ray = camera.ScreenPixelToRay( Mouse.Position );

		if ( Dragged is not null )
		{
			UpdateDrag( ray );

			return;
		}

		if ( _pending is not null )
		{
			UpdatePending();

			return;
		}

		InWorldActionButton? action = ActionUnder( ray );

		if ( action is not null && Input.Pressed( "attack1" ) )
		{
			SetHovered( null );
			action.Invoke();

			return;
		}

		SetHovered( CardUnder( ray ) );

		if ( Input.Pressed( "attack1" ) && Hovered is not null )
		{
			_pending       = Hovered;
			_pressPosition = Mouse.Position;
		}
		else if ( Input.Pressed( "attack1" ) )
		{
			RequestSelection( null );
		}

		if ( Input.Pressed( "attack2" ) && Hovered is not null )
			RequestTap( Hovered );
	}


	protected override void OnDisabled()
	{
		CancelInteraction();
		base.OnDisabled();
	}


	protected override void OnDestroy()
	{
		CancelInteraction();
		base.OnDestroy();
	}


	private void UpdatePending()
	{
		if ( _pending is not CardObject pending )
			return;

		if ( !Input.Down( "attack1" ) )
		{
			RequestSelection( pending );
			_pending = null;

			return;
		}

		if ( Vector2.DistanceBetween( Mouse.Position, _pressPosition ) < DragThresholdPixels )
			return;

		_pending = null;
		BeginDrag( pending );
	}


	private void BeginDrag( CardObject card )
	{
		if ( !card.IsValid() )
			return;

		Match? match = Scene.Get<Match>();

		if ( match is not null && match.PriorityPlayerId != Connection.Local.Id )
		{
			card.Pulse();

			return;
		}

		if ( match is null && card.GameObject.Network.IsProxy )
		{
			card.Pulse();

			return;
		}

		Dragged       = card;
		_dragOrigin   = card.RestPose;
		_dragRotation = card.WorldRotation;
		_dragPosition = card.WorldPosition;
		_dragVelocity = Vector3.Zero;

		SetHovered( null );
		card.CancelThrow();
		card.GameObject.Tags.Set( "dragging", true );
		card.SetHover( 0f );

		if ( card.GameObject.Network.IsProxy )
			card.BeginLocalDragPreview();

		if ( match is not null )
			match.RequestGrabCard( card );
	}


	private void UpdateDrag( Ray ray )
	{
		CardObject? card = Dragged;

		if ( !card.IsValid() )
		{
			ClearDragState();

			return;
		}

		float   planeHeight   = _dragOrigin.Position.z + DragHeight;
		Vector3 target        = new Plane( new Vector3( 0f, 0f, planeHeight ), Vector3.Up ).Trace( ray ) ?? _dragPosition;
		float   interpolation = 1f - MathF.Exp( -FollowSpeed * Time.Delta );
		Vector3 previous      = _dragPosition;
		_dragPosition = Vector3.Lerp( _dragPosition, target, interpolation );
		_dragVelocity = ( _dragPosition - previous ) / MathF.Max( Time.Delta, 0.0001f );

		Transform previewPose = new Transform( _dragPosition, _dragRotation );

		if ( card.GameObject.Network.IsProxy )
			card.UpdateLocalDragPreview( previewPose );
		else
		{
			card.WorldPosition = _dragPosition;
			card.WorldRotation = _dragRotation;
		}

		SetDropZone( ZoneUnder( ray ) );
		card.Highlight( DropTarget is null? Color.White : new Color( 0.55f, 1f, 0.55f ), DropTarget is null? 0f : 0.55f );

		if ( Input.Released( "attack1" ) )
			EndDrag( ray );
	}


	private void EndDrag( Ray ray )
	{
		CardObject? card = Dragged;

		if ( !card.IsValid() )
		{
			ClearDragState();

			return;
		}

		ZoneObject? zone = ZoneUnder( ray );

		if ( zone is not null && zone.CanAccept( card ) )
		{
			Transform     dropPose = DropPose( zone, ray, card );
			Match? match = Scene.Get<Match>();

			if ( card.GameObject.Network.IsProxy )
				card.UpdateLocalDragPreview( dropPose );

			if ( match is not null )
				match.RequestMoveCard( card, zone.ZoneId, dropPose );
			else
				card.PlaceInZone( zone.ZoneId, dropPose );
		}
		else if ( _dragVelocity.Length >= ThrowMinimumSpeed )
		{
			Vector3       planarVelocity = _dragVelocity.WithZ( 0f )                   * ThrowVelocityScale;
			Vector3       spin           = Vector3.Cross( Vector3.Up, planarVelocity ) * ThrowSpinScale;
			Match? match = Scene.Get<Match>();

			if ( match is not null )
				match.RequestThrowCard( card, planarVelocity, spin );
			else
				card.Throw( planarVelocity, spin );
		}
		else
		{
			Match? match = Scene.Get<Match>();

			if ( match is not null )
				match.ReleaseGrab( card );
			else
				card.MoveTo( _dragOrigin );
		}

		ClearDragState();
	}


	private Transform DropPose( ZoneObject zone, Ray ray, CardObject card )
	{
		if ( zone.ActiveLayout != ZoneLayout.Freeform )
			return zone.GetCardPose( zone.CardCount );

		Vector3 normal     = zone.WorldRotation.Up;
		Vector3 planePoint = zone.WorldPosition + normal * zone.BaseLift;
		Vector3 position   = new Plane( planePoint, normal ).Trace( ray ) ?? _dragPosition;

		return new Transform( position, zone.WorldRotation );
	}


	private CardObject? CardUnder( Ray ray )
	{
		foreach ( SceneTraceResult hit in Scene.Trace.Ray( ray, TraceDistance ).WithoutTags( "dragging" ).RunAll() )
		{
			CardObject? card = hit.GameObject?.Components.Get<CardObject>();

			if ( card is not null )
				return card;
		}

		return null;
	}


	private InWorldActionButton? ActionUnder( Ray ray )
	{
		foreach ( SceneTraceResult hit in Scene.Trace.Ray( ray, TraceDistance ).RunAll() )
		{
			InWorldActionButton? action = hit.GameObject?.Components.Get<InWorldActionButton>();

			if ( action is not null )
				return action;
		}

		return null;
	}


	private ZoneObject? ZoneUnder( Ray ray )
	{
		foreach ( SceneTraceResult hit in Scene.Trace.Ray( ray, TraceDistance ).WithoutTags( "dragging" ).RunAll() )
		{
			ZoneObject? zone = hit.GameObject?.Components.Get<ZoneObject>();

			if ( zone is not null )
				return zone;

			CardObject? card = hit.GameObject?.Components.Get<CardObject>();
			zone = card is null? null : ZoneObject.Find( card.ZoneId );

			if ( zone is not null )
				return zone;
		}

		ZoneObject? smallestZone = null;
		float       smallestArea = float.MaxValue;

		foreach ( ZoneObject zone in Scene.GetAllComponents<ZoneObject>() )
		{
			Vector3 normal = zone.WorldRotation.Up;
			Vector3? point = new Plane( zone.WorldPosition, normal ).Trace( ray );

			if ( point is not Vector3 intersection )
				continue;

			Vector3 delta = intersection - zone.WorldPosition;
			Vector2 size  = zone.ActiveSize;

			if ( MathF.Abs( Vector3.Dot( delta, zone.WorldRotation.Forward ) ) > size.x * 0.5f ||
				 MathF.Abs( Vector3.Dot( delta, zone.WorldRotation.Right ) ) > size.y * 0.5f )
				continue;

			float area = size.x * size.y;

			if ( area >= smallestArea )
				continue;

			smallestZone = zone;
			smallestArea = area;
		}

		return smallestZone;
	}


	private void SetHovered( CardObject? card )
	{
		if ( !card.IsValid() )
			card = null;

		if ( ReferenceEquals( card, Hovered ) )
			return;

		Hovered?.SetHover( 0f );
		Hovered = card;
		Hovered?.SetHover( 1f );
	}


	private void RequestSelection( CardObject? card )
	{
		if ( !card.IsValid() )
			card = null;

		Match? match = Scene.Get<Match>();

		if ( match is not null )
			match.RequestSelectCard( card );
		else
			ApplySelection( card );
	}


	private void SynchronizeSelection()
	{
		Seat? localSeat = Scene.GetAllComponents<Seat>().FirstOrDefault( seat => seat.IsLocal );
		ApplySelection( localSeat?.SelectedCard );
	}


	private void ApplySelection( CardObject? card )
	{
		if ( !card.IsValid() )
			card = null;

		if ( ReferenceEquals( card, _selected ) )
			return;

		_selected?.SetGlow( false );
		_selected = card;
		_selected?.SetGlow( true );
	}


	private void RequestTap( CardObject card )
	{
		Match? match = Scene.Get<Match>();

		if ( match is not null )
		{
			if ( match.PriorityPlayerId == Connection.Local.Id )
				match.RequestTapCard( card );
			else
				card.Pulse();
		}
		else if ( !card.GameObject.Network.IsProxy )
			card.SetTapped( !card.Tapped );
	}


	private void SetDropZone( ZoneObject? zone )
	{
		if ( ReferenceEquals( zone, DropTarget ) )
			return;

		_dropHighlight?.SetHover( 0f );
		_dropHighlight = null;
		DropTarget     = zone;

		CardObject? top = zone is not null && zone.Cards.Count > 0? zone.Cards[^1] : null;

		if ( top is not null && !ReferenceEquals( top, Dragged ) )
		{
			_dropHighlight = top;
			top.SetHover( 1f );
		}
	}


	private void CancelInteraction()
	{
		_pending = null;
		SetHovered( null );
		RequestSelection( null );

		if ( Dragged.IsValid() )
		{
			Match? match = Scene.Get<Match>();

			if ( match is not null )
				match.ReleaseGrab( Dragged );
			else
				Dragged.MoveTo( _dragOrigin );
		}

		ClearDragState();
	}


	private void ClearDragState()
	{
		if ( Dragged.IsValid() )
		{
			Dragged.ReleaseLocalDragPreview();
			Dragged.GameObject.Tags.Set( "dragging", false );
			Dragged.ClearHighlight();
		}

		_dropHighlight?.SetHover( 0f );
		_dropHighlight = null;
		DropTarget     = null;
		Dragged        = null;
		_dragVelocity  = Vector3.Zero;
	}
}
