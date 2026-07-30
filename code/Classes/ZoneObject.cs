#nullable enable

using System;
using System.Collections.Generic;

namespace Sandbox.Classes;

public enum MtgZoneKind
{
	Library,
	Hand,
	Battlefield,
	Graveyard,
	Exile,
	Command,
	Stack,
	Sideboard,
	Custom
}

public enum MtgZoneLayout
{
	Stack,
	Row,
	Fan,
	Grid,
	Freeform
}

public enum MtgZoneCardState
{
	ZoneDefault,
	Preserve,
	Concealed,
	OwnerOnly,
	Front,
	PrintedBack
}

/// <summary>
/// Authoritative MTG zone container. It owns card membership, ordering,
/// visibility policy, and card layout; SlotRenderer supplies its marker.
/// </summary>
public sealed class ZoneObject : Component
{
	[SkipHotload]
	private static readonly Dictionary<Guid, ZoneObject> Registry = [];

	[Sync]
	public Guid ZoneId { get; set; }

	[Sync]
	public int CardCount { get; set; }

	[Property]
	public MtgZoneKind ZoneKind { get; set; } = MtgZoneKind.Library;

	[Property]
	public MtgZoneLayout Layout { get; set; } = MtgZoneLayout.Stack;

	[Property]
	public bool UseRecommendedLayout { get; set; } = true;

	[Property]
	public float CardSpacing { get; set; } = 0.35f;

	[Property]
	public float StackSpacing { get; set; } = 0.06f;

	/// <summary>
	/// Minimum visible separation between cards in physical pile zones,
	/// expressed as a fraction of card width. This keeps piles readable when
	/// the shared procedural card size is changed.
	/// </summary>
	[Property]
	public float VisibleStackSpacingRatio { get; set; } = 0.0015f;

	[Property]
	public float BaseLift { get; set; } = 0.08f;

	[Property]
	public int GridColumns { get; set; } = 5;

	[Property]
	public int Capacity { get; set; }

	[Property]
	public bool ShowMarker { get; set; } = true;

	[Property]
	public int MarkerResolution { get; set; } = 512;

	[Property]
	public Vector3 TriggerSize { get; set; } = new(
		CardMesh.DefaultWidth,
		CardMesh.DefaultWidth / CardFaceRenderer.Aspect,
		1f );

	public IReadOnlyList<CardObject> Cards => _cards;
	public CardObject? TopCard =>
		_cards.Count == 0 ? null : _cards[^1];
	public MtgZoneLayout ActiveLayout =>
		UseRecommendedLayout
			? RecommendedLayout( ZoneKind )
			: Layout;

	private readonly List<CardObject> _cards = [];
	private ModelRenderer? _markerRenderer;
	private Guid _registeredZoneId;

	protected override void OnAwake()
	{
		if ( ZoneId == Guid.Empty && !GameObject.Network.IsProxy )
			ZoneId = Guid.NewGuid();

		RegisterCurrentZoneId();

		GameObject.Tags.Add( "mtg-zone" );
		RefreshConfiguration();
	}

	protected override void OnStart()
	{
		base.OnStart();

		if ( !GameObject.Network.IsProxy )
			RebuildMembership();
	}

	protected override void OnUpdate()
	{
		// Proxies can receive ZoneId after OnAwake. Keep the local lookup
		// registry current without synchronizing the card collection itself.
		if ( _registeredZoneId != ZoneId )
			RegisterCurrentZoneId();
	}

	protected override void OnDestroy()
	{
		if ( _registeredZoneId != Guid.Empty &&
			Registry.TryGetValue(
				_registeredZoneId,
				out ZoneObject? registered ) &&
			ReferenceEquals( registered, this ) )
		{
			Registry.Remove( _registeredZoneId );
		}

		base.OnDestroy();
	}

	public static ZoneObject? Find( Guid zoneId )
	{
		return zoneId != Guid.Empty &&
			Registry.TryGetValue( zoneId, out ZoneObject? zone )
				? zone
				: null;
	}

	public bool CanAccept( CardObject card )
	{
		ArgumentNullException.ThrowIfNull( card );
		return Capacity <= 0 ||
			_cards.Contains( card ) ||
			_cards.Count < Capacity;
	}

	public void AddCard(
		CardObject card,
		MtgZoneCardState state = MtgZoneCardState.ZoneDefault,
		int index = -1,
		bool animate = true )
	{
		ArgumentNullException.ThrowIfNull( card );
		RequireAuthority();

		if ( card.GameObject.Network.IsProxy )
		{
			throw new InvalidOperationException(
				"Zone authority must also own cards it moves." );
		}

		if ( !CanAccept( card ) )
		{
			throw new InvalidOperationException(
				$"{ZoneKind} zone is at capacity." );
		}

		if ( card.ZoneId != Guid.Empty &&
			card.ZoneId != ZoneId )
		{
			Find( card.ZoneId )?.RemoveCard(
				card,
				clearMembership: false,
				reflow: true );
		}

		_cards.Remove( card );
		int insertionIndex = index < 0
			? _cards.Count
			: index.Clamp( 0, _cards.Count );
		_cards.Insert( insertionIndex, card );
		card.ZoneId = ZoneId;

		ApplyCardState( card, state );
		ReindexAndLayout( animate );
	}

	public bool RemoveCard( CardObject card )
	{
		return RemoveCard(
			card,
			clearMembership: true,
			reflow: true );
	}

	public CardObject? DrawTop(
		MtgZoneCardState destinationState =
			MtgZoneCardState.Preserve )
	{
		RequireAuthority();

		if (_cards.Count == 0 )
			return null;

		CardObject card = _cards[^1];
		RemoveCard(
			card,
			clearMembership: true,
			reflow: true );
		ApplyCardState( card, destinationState );
		return card;
	}

	public void Shuffle()
	{
		RequireAuthority();

		for ( int index = _cards.Count - 1; index > 0; index-- )
		{
			int other = Game.Random.Next( index + 1 );
			(_cards[index], _cards[other]) =
				(_cards[other], _cards[index]);
		}

		foreach ( CardObject card in _cards )
			ApplyCardState( card, MtgZoneCardState.ZoneDefault );

		ReindexAndLayout( animate: true );
	}

	public void MoveCard(
		int fromIndex,
		int toIndex,
		bool animate = true )
	{
		RequireAuthority();

		if ( fromIndex < 0 || fromIndex >= _cards.Count )
			throw new ArgumentOutOfRangeException( nameof(fromIndex) );

		toIndex = toIndex.Clamp( 0, _cards.Count - 1 );
		CardObject card = _cards[fromIndex];
		_cards.RemoveAt( fromIndex );
		_cards.Insert( toIndex, card );
		ReindexAndLayout( animate );
	}

	public void Reflow( bool animate = true )
	{
		RequireAuthority();
		ReindexAndLayout( animate );
	}

	public void RefreshConfiguration()
	{
		var trigger = GetOrAddComponent<BoxCollider>();
		trigger.Scale = TriggerSize;
		trigger.IsTrigger = true;

		if ( Application.IsHeadless )
			return;

		// Counts are useful even when a game hides the decorative zone marker.
		GetOrAddComponent<ZoneCountIndicator>();

		if ( !ShowMarker )
		{
			if ( _markerRenderer is not null )
				_markerRenderer.Enabled = false;
			return;
		}

		_markerRenderer ??= GetOrAddComponent<ModelRenderer>();
		_markerRenderer.Enabled = true;
		SlotRenderer.BuildSlot(
			ZoneKind,
			MarkerResolution ).ApplyTo( _markerRenderer );
	}

	public void RebuildMembership()
	{
		RequireAuthority();
		_cards.Clear();

		foreach ( CardObject card
			in Scene.GetAllComponents<CardObject>() )
		{
			if ( card.ZoneId == ZoneId )
				_cards.Add( card );
		}

		_cards.Sort( (left, right) =>
			left.StackIndex.CompareTo( right.StackIndex ) );
		ReindexAndLayout( animate: false );
	}

	public Transform GetCardPose( int index )
	{
		index = Math.Max( index, 0 );
		Vector3 position = WorldPosition;
		Rotation rotation = WorldRotation;
		Vector3 right = rotation.Right;
		Vector3 forward = rotation.Forward;
		Vector3 normal = rotation.Up;
		position += normal * BaseLift;

		switch ( ActiveLayout )
		{
			case MtgZoneLayout.Stack:
				position += normal *
					(index * EffectiveStackSpacing());
				break;

			case MtgZoneLayout.Row:
				position += right * (index * (
					CardMesh.Width + CardSpacing));
				position += normal * (index * StackSpacing);
				break;

			case MtgZoneLayout.Fan:
			{
				float centered = index -
					MathF.Max( CardCount - 1, 0 ) * 0.5f;
				float angle = centered * 4f;
				position += right * (
					centered * (CardMesh.Width * 0.42f +
						CardSpacing));
				position -= forward *
					(MathF.Abs( centered ) * 0.12f);
				position += normal * (index * StackSpacing);
				rotation = Rotation.FromAxis(
					normal,
					angle ) * rotation;
				break;
			}

			case MtgZoneLayout.Grid:
			{
				int columns = Math.Max( GridColumns, 1 );
				int column = index % columns;
				int row = index / columns;
				position += right * (
					column * (CardMesh.Width + CardSpacing));
				position -= forward * (
					row * (CardMesh.Height + CardSpacing));
				position += normal * (index * StackSpacing);
				break;
			}

			case MtgZoneLayout.Freeform:
				break;
		}

		return new Transform( position, rotation );
	}

	private float EffectiveStackSpacing()
	{
		float spacing = MathF.Max( StackSpacing, 0f );

		if ( ZoneKind is MtgZoneKind.Library or
			MtgZoneKind.Graveyard or
			MtgZoneKind.Exile )
		{
			spacing = MathF.Max(
				spacing,
				CardMesh.Width *
					MathF.Max( VisibleStackSpacingRatio, 0f ) );
			spacing = MathF.Max(
				spacing,
				CardMesh.Thickness * 1.05f );
		}

		return spacing;
	}

	private bool RemoveCard(
		CardObject card,
		bool clearMembership,
		bool reflow )
	{
		RequireAuthority();

		if ( !_cards.Remove( card ) )
			return false;

		if ( clearMembership )
		{
			card.ZoneId = Guid.Empty;
			card.StackIndex = 0;
		}

		if ( reflow )
			ReindexAndLayout( animate: true );
		else
			CardCount = _cards.Count;

		return true;
	}

	private void ReindexAndLayout( bool animate )
	{
		CardCount = _cards.Count;

		for ( int index = 0; index < _cards.Count; index++ )
		{
			CardObject card = _cards[index];
			card.StackIndex = index;

			if ( ActiveLayout == MtgZoneLayout.Freeform )
				continue;

			Transform pose = GetCardPose( index );

			if ( animate )
				card.MoveTo( pose );
			else
				card.SnapTo( pose );
		}
	}

	private void ApplyCardState(
		CardObject card,
		MtgZoneCardState requested )
	{
		MtgZoneCardState state = requested ==
			MtgZoneCardState.ZoneDefault
				? DefaultCardState( ZoneKind )
				: requested;

		switch ( state )
		{
			case MtgZoneCardState.Preserve:
				break;

			case MtgZoneCardState.Concealed:
				card.Conceal();
				break;

			case MtgZoneCardState.OwnerOnly:
				card.Conceal();
				card.ShareIdentityWithOwner();
				break;

			case MtgZoneCardState.Front:
				card.Reveal( 0 );
				break;

			case MtgZoneCardState.PrintedBack:
				card.Reveal( 1 );
				break;
		}
	}

	private static MtgZoneCardState DefaultCardState(
		MtgZoneKind zone )
	{
		return zone switch
		{
			MtgZoneKind.Library =>
				MtgZoneCardState.Concealed,
			MtgZoneKind.Hand or MtgZoneKind.Sideboard =>
				MtgZoneCardState.OwnerOnly,
			MtgZoneKind.Battlefield =>
				MtgZoneCardState.Front,
			MtgZoneKind.Graveyard or
			MtgZoneKind.Exile or
			MtgZoneKind.Command or
			MtgZoneKind.Stack =>
				MtgZoneCardState.Front,
			_ => MtgZoneCardState.Preserve
			};
	}

	private static MtgZoneLayout RecommendedLayout(
		MtgZoneKind zone )
	{
		return zone switch
		{
			MtgZoneKind.Hand => MtgZoneLayout.Fan,
			MtgZoneKind.Battlefield => MtgZoneLayout.Freeform,
			_ => MtgZoneLayout.Stack
		};
	}

	private void RegisterCurrentZoneId()
	{
		if ( _registeredZoneId != Guid.Empty &&
			Registry.TryGetValue(
				_registeredZoneId,
				out ZoneObject? previous ) &&
			ReferenceEquals( previous, this ) )
		{
			Registry.Remove( _registeredZoneId );
		}

		_registeredZoneId = ZoneId;

		if ( ZoneId != Guid.Empty )
			Registry[ZoneId] = this;
	}

	private void RequireAuthority()
	{
		if ( GameObject.Network.IsProxy )
		{
			throw new InvalidOperationException(
				"Only zone authority can change zone contents." );
		}
	}
}
