#nullable enable

using System;

namespace Sandbox.Classes;

/// <summary>
/// Local mouse interaction for the in-world MTG cards. Attach this component
/// to the local camera (or another local-only scene object).
/// </summary>
public sealed class CardHover : Component
{
	[Property]
	public float TraceDistance { get; set; } = 10000f;

	[Property]
	public float DragHeight { get; set; } = 6f;

	[Property]
	public float FollowSpeed { get; set; } = 18f;

	[Property]
	public float ThrowMinimumSpeed { get; set; } = 80f;

	[Property]
	public float ThrowVelocityScale { get; set; } = 1f;

	[Property]
	public float ThrowSpinScale { get; set; } = 0.05f;

	[Property]
	public float DragThresholdPixels { get; set; } = 6f;

	public CardObject? Hovered => _hovered;
	public CardObject? Dragged => _dragged;
	public ZoneObject? DropTarget => _dropZone;
	public bool IsDragging => _dragged is not null;

	private CardObject? _hovered;
	private CardObject? _pending;
	private CardObject? _dragged;
	private ZoneObject? _dropZone;
	private CardObject? _dropHighlight;

	private Vector2 _pressPosition;
	private Transform _dragOrigin;
	private Rotation _dragRotation;
	private Vector3 _dragPosition;
	private Vector3 _dragVelocity;

	protected override void OnUpdate()
	{
		CameraComponent? camera = Scene.Camera;

		if ( camera is null )
			return;

		Ray ray = camera.ScreenPixelToRay( Mouse.Position );

		if ( _dragged is not null )
		{
			UpdateDrag( ray );
			return;
		}

		if ( _pending is not null )
		{
			UpdatePending();
			return;
		}

		SetHovered( CardUnder( ray ) );

		if ( Input.Pressed( "attack1" ) &&
			_hovered is not null )
		{
			_pending = _hovered;
			_pressPosition = Mouse.Position;
		}
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
			pending.Pulse();
			_pending = null;
			return;
		}

		if ( Vector2.DistanceBetween(
			Mouse.Position,
			_pressPosition ) < DragThresholdPixels )
		{
			return;
		}

		_pending = null;
		BeginDrag( pending );
	}

	private void BeginDrag( CardObject card )
	{
		if ( !card.IsValid() )
			return;

		_dragged = card;
		_dragOrigin = card.RestPose;
		_dragRotation = card.WorldRotation;
		_dragPosition = card.WorldPosition;
		_dragVelocity = Vector3.Zero;

		SetHovered( null );
		card.CancelThrow();
		card.GameObject.Tags.Set( "dragging", true );
		card.SetHover( 0f );
	}

	private void UpdateDrag( Ray ray )
	{
		CardObject? card = _dragged;

		if ( !card.IsValid() )
		{
			ClearDragState();
			return;
		}

		float planeHeight = _dragOrigin.Position.z + DragHeight;
		Vector3 target = new Plane(
			new Vector3( 0f, 0f, planeHeight ),
			Vector3.Up ).Trace( ray ) ?? _dragPosition;
		float interpolation =
			1f - MathF.Exp( -FollowSpeed * Time.Delta );
		Vector3 previous = _dragPosition;
		_dragPosition = Vector3.Lerp(
			_dragPosition,
			target,
			interpolation );
		_dragVelocity = (_dragPosition - previous) /
			MathF.Max( Time.Delta, 0.0001f );

		card.WorldPosition = _dragPosition;
		card.WorldRotation = _dragRotation;

		SetDropZone( ZoneUnder( ray ) );
		card.Highlight(
			_dropZone is null
				? Color.White
				: new Color( 0.55f, 1f, 0.55f ),
			_dropZone is null ? 0f : 0.55f );

		if ( Input.Released( "attack1" ) )
			EndDrag( ray );
	}

	private void EndDrag( Ray ray )
	{
		CardObject? card = _dragged;

		if ( !card.IsValid() )
		{
			ClearDragState();
			return;
		}

		ZoneObject? zone = ZoneUnder( ray );

		if ( zone is not null && zone.CanAccept( card ) )
		{
			Transform dropPose = DropPose( zone, ray, card );
			card.PlaceInZone( zone.ZoneId, dropPose );
		}
		else if ( _dragVelocity.Length >= ThrowMinimumSpeed )
		{
			Vector3 planarVelocity = _dragVelocity.WithZ( 0f ) *
				ThrowVelocityScale;
			Vector3 spin = Vector3.Cross(
				Vector3.Up,
				planarVelocity ) * ThrowSpinScale;
			card.Throw( planarVelocity, spin );
		}
		else
		{
			card.MoveTo( _dragOrigin );
		}

		ClearDragState();
	}

	private Transform DropPose(
		ZoneObject zone,
		Ray ray,
		CardObject card )
	{
		if ( zone.ActiveLayout != MtgZoneLayout.Freeform )
			return zone.GetCardPose( zone.CardCount );

		Vector3 normal = zone.WorldRotation.Up;
		Vector3 planePoint =
			zone.WorldPosition + normal * zone.BaseLift;
		Vector3 position =
			new Plane( planePoint, normal ).Trace( ray ) ??
			card.WorldPosition;
		return new Transform( position, zone.WorldRotation );
	}

	private CardObject? CardUnder( Ray ray )
	{
		foreach ( SceneTraceResult hit in Scene.Trace
			.Ray( ray, TraceDistance )
			.WithoutTags( "dragging" )
			.RunAll() )
		{
			CardObject? card =
				hit.GameObject?.Components.Get<CardObject>();

			if ( card is not null )
				return card;
		}

		return null;
	}

	private ZoneObject? ZoneUnder( Ray ray )
	{
		foreach ( SceneTraceResult hit in Scene.Trace
			.Ray( ray, TraceDistance )
			.WithoutTags( "dragging" )
			.RunAll() )
		{
			ZoneObject? zone =
				hit.GameObject?.Components.Get<ZoneObject>();

			if ( zone is not null )
				return zone;

			CardObject? card =
				hit.GameObject?.Components.Get<CardObject>();
			zone = card is null
				? null
				: ZoneObject.Find( card.ZoneId );

			if ( zone is not null )
				return zone;
		}

		return null;
	}

	private void SetHovered( CardObject? card )
	{
		if ( !card.IsValid() )
			card = null;

		if ( ReferenceEquals( card, _hovered ) )
			return;

		_hovered?.SetHover( 0f );
		_hovered = card;
		_hovered?.SetHover( 1f );
	}

	private void SetDropZone( ZoneObject? zone )
	{
		if ( ReferenceEquals( zone, _dropZone ) )
			return;

		_dropHighlight?.SetHover( 0f );
		_dropHighlight = null;
		_dropZone = zone;

		if ( zone?.TopCard is CardObject top &&
			!ReferenceEquals( top, _dragged ) )
		{
			_dropHighlight = top;
			top.SetHover( 1f );
		}
	}

	private void CancelInteraction()
	{
		_pending = null;
		SetHovered( null );

		if (_dragged.IsValid() )
			_dragged.MoveTo( _dragOrigin );

		ClearDragState();
	}

	private void ClearDragState()
	{
		if ( _dragged.IsValid() )
		{
			_dragged.GameObject.Tags.Set( "dragging", false );
			_dragged.ClearHighlight();
		}

		_dropHighlight?.SetHover( 0f );
		_dropHighlight = null;
		_dropZone = null;
		_dragged = null;
		_dragVelocity = Vector3.Zero;
	}
}
