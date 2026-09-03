#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Decals;
using System;

namespace Sandbox.Classes.Zones;

public enum ZoneType
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

public enum ZoneLayout
{
	Stack,
	Row,
	Fan,
	Grid,
	Freeform
}

internal readonly record struct FanLayout( float Across, float Depth, float Angle );

internal static class ZoneLayoutMath
{
	public static FanLayout Fan( int index, int cardCount, float cardWidth, float cardHeight, float cardSpacing )
	{
		float centered = Math.Max( index, 0 ) - MathF.Max( cardCount - 1, 0 ) * 0.5f;

		return new FanLayout(
			centered * (cardWidth * 0.42f + cardSpacing),
			centered * centered * cardHeight * 0.015f,
			centered * -4f
		);
	}
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
///     Common authoritative container for cards in an MTG zone. Concrete zones
///     choose their defaults and expose only operations meaningful to that zone.
/// </summary>
public abstract class ZoneObject : Component
{
	[SkipHotload] private static readonly Dictionary<Guid, ZoneObject> Registry = [ ];

	protected readonly List<CardObject> CardsInternal = [ ];

	private Vector2        _configuredSize;
	private int            _configuredMarkerResolution;
	private int            _configuredMaterialVersion;
	private bool           _configuredShowMarker;
	private float          _configuredTriggerHeight;
	private ZoneType       _configuredType;
	private ModelRenderer? _markerRenderer;
	private Guid           _registeredZoneId;

	[Sync] public Guid ZoneId { get; set; }
	[Sync] public Guid OwnerPlayerId { get; set; }
	[Sync] public int CardCount { get; protected set; }

	[Property][Sync] public string Role { get; set; } = string.Empty;
	[Property] public ZoneLayout Layout { get; set; } = ZoneLayout.Stack;
	[Property] public bool UseDefaultLayout { get; set; } = true;
	[Property] public float CardSpacing { get; set; } = 0.35f;
	[Property] public float StackSpacing { get; set; } = 0.06f;
	[Property] public float VisibleStackSpacingRatio { get; set; } = 0.0015f;
	[Property] public float BaseLift { get; set; } = 0.08f;
	[Property] public int GridColumns { get; set; } = 5;
	[Property] public int Capacity { get; set; }
	[Property] public bool ShowMarker { get; set; } = true;
	[Property] public int MarkerResolution { get; set; } = 512;
	/// <summary>
	///     Optional marker and trigger dimensions. Leave either axis at zero to
	///     use this zone type's default dimensions.
	/// </summary>
	[Property] public Vector2 Size { get; set; }
	[Property] public float TriggerHeight { get; set; } = 1f;

	public IReadOnlyList<CardObject> Cards => CardsInternal;
	public abstract ZoneType Type { get; }
	public virtual ZoneLayout DefaultLayout => ZoneLayout.Stack;
	public virtual MtgZoneCardState DefaultCardState => MtgZoneCardState.Front;
	public virtual Vector2 DefaultSize => new( CardMesh.Width, CardMesh.Height );
	public ZoneLayout ActiveLayout => UseDefaultLayout? DefaultLayout : Layout;
	public Vector2 ActiveSize => new(
		Size.x > 0f? Size.x : DefaultSize.x,
		Size.y > 0f? Size.y : DefaultSize.y
	);

	protected virtual bool EnforcePhysicalStackSpacing => false;


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
		if ( _registeredZoneId != ZoneId )
			RegisterCurrentZoneId();

		if ( _configuredSize != ActiveSize ||
			 _configuredMarkerResolution != MarkerResolution ||
			 _configuredMaterialVersion != CardMaterialFactory.CacheVersion ||
			 _configuredShowMarker != ShowMarker ||
			 _configuredTriggerHeight != TriggerHeight ||
			 _configuredType != Type )
			RefreshConfiguration();

		if ( GameObject.Network.IsProxy )
			RefreshProxyMembership();
	}


	protected override void OnDestroy()
	{
		if ( _registeredZoneId != Guid.Empty && Registry.TryGetValue( _registeredZoneId, out ZoneObject? registered ) && ReferenceEquals( registered, this ) )
			Registry.Remove( _registeredZoneId );

		base.OnDestroy();
	}


	public static ZoneObject? Find( Guid zoneId )
	{
		return zoneId != Guid.Empty && Registry.TryGetValue( zoneId, out ZoneObject? zone )? zone : null;
	}


	public virtual bool CanAccept( CardObject card )
	{
		ArgumentNullException.ThrowIfNull( card );

		return Capacity <= 0 || CardsInternal.Contains( card ) || CardsInternal.Count < Capacity;
	}


	public void AddCard( CardObject card, MtgZoneCardState state = MtgZoneCardState.ZoneDefault, int index = -1, bool animate = true )
	{
		Add( card, state, index, animate );
	}


	public bool RemoveCard( CardObject card, bool animate = true )
	{
		return Remove( card, clearMembership: true, reflow: true, animate );
	}


	protected void Add( CardObject card, MtgZoneCardState state = MtgZoneCardState.ZoneDefault, int index = -1, bool animate = true )
	{
		ArgumentNullException.ThrowIfNull( card );
		RequireAuthority();

		if ( card.GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Zone authority must also own cards it moves." );

		if ( !CanAccept( card ) )
			throw new InvalidOperationException( $"{Type} zone is at capacity." );

		if ( card.ZoneId != Guid.Empty && card.ZoneId != ZoneId )
			Find( card.ZoneId )?.Remove( card, clearMembership: false, reflow: true, animate );

		CardsInternal.Remove( card );
		int insertionIndex = index < 0? CardsInternal.Count : index.Clamp( 0, CardsInternal.Count );
		CardsInternal.Insert( insertionIndex, card );
		card.ZoneId = ZoneId;

		ApplyCardState( card, state );
		ReindexAndLayout( animate );
	}


	protected bool Remove( CardObject card, bool clearMembership = true, bool reflow = true, bool animate = true )
	{
		ArgumentNullException.ThrowIfNull( card );
		RequireAuthority();

		if ( !CardsInternal.Remove( card ) )
			return false;

		if ( clearMembership )
		{
			card.ZoneId     = Guid.Empty;
			card.StackIndex = 0;
		}

		if ( reflow )
			ReindexAndLayout( animate );
		else
			CardCount = CardsInternal.Count;

		return true;
	}


	protected void Move( int fromIndex, int toIndex, bool animate = true )
	{
		RequireAuthority();

		if ( fromIndex < 0 || fromIndex >= CardsInternal.Count )
			throw new ArgumentOutOfRangeException( nameof(fromIndex) );

		toIndex = toIndex.Clamp( 0, CardsInternal.Count - 1 );
		CardObject card = CardsInternal[fromIndex];
		CardsInternal.RemoveAt( fromIndex );
		CardsInternal.Insert( toIndex, card );
		ReindexAndLayout( animate );
	}


	public void Reflow( bool animate = true )
	{
		RequireAuthority();
		ReindexAndLayout( animate );
	}


	public void RefreshConfiguration()
	{
		Vector2 size = ActiveSize;
		_configuredSize             = size;
		_configuredMarkerResolution = MarkerResolution;
		_configuredMaterialVersion  = CardMaterialFactory.CacheVersion;
		_configuredShowMarker       = ShowMarker;
		_configuredTriggerHeight    = TriggerHeight;
		_configuredType             = Type;

		BoxCollider trigger = GetOrAddComponent<BoxCollider>();
		trigger.Scale     = new Vector3( size.x, size.y, MathF.Max( TriggerHeight, 0.1f ) );
		trigger.IsTrigger = true;

		if ( Application.IsHeadless )
			return;

		GetOrAddComponent<ZoneCountIndicator>();

		if ( !ShowMarker )
		{
			if ( _markerRenderer is not null )
				_markerRenderer.Enabled = false;

			return;
		}

		_markerRenderer         ??= GetOrAddComponent<ModelRenderer>();
		_markerRenderer.Enabled =   true;
		SlotRenderer.BuildSlot( Type, size, MarkerResolution ).ApplyTo( _markerRenderer );
	}


	public void RebuildMembership()
	{
		RequireAuthority();
		CardsInternal.Clear();

		foreach ( CardObject card in Scene.GetAllComponents<CardObject>() )
		{
			if ( card.ZoneId == ZoneId )
				CardsInternal.Add( card );
		}

		CardsInternal.Sort( ( left, right ) => left.StackIndex.CompareTo( right.StackIndex ) );
		ReindexAndLayout( animate: false );
	}


	public Transform GetCardPose( int index )
	{
		index = Math.Max( index, 0 );
		Vector3  position = WorldPosition;
		Rotation rotation = WorldRotation;
		Vector3  right    = rotation.Right;
		Vector3  forward  = rotation.Forward;
		Vector3  normal   = rotation.Up;
		position += normal * BaseLift;

		switch ( ActiveLayout )
		{
			case ZoneLayout.Stack: position += normal * (index * EffectiveStackSpacing()); break;

			case ZoneLayout.Row:
				position += right  * (index * (CardMesh.Width + CardSpacing));
				position += normal * (index * StackSpacing);
				break;

			case ZoneLayout.Fan:
			{
				FanLayout fan = ZoneLayoutMath.Fan( index, CardCount, CardMesh.Width, CardMesh.Height, CardSpacing );
				// CardMesh is built in the local XY plane: Forward is its width axis and
				// Right is its depth-on-table axis. Using Right for spacing collapses a
				// hand into a line toward the player instead of spreading it sideways.
				position += forward * fan.Across;
				position += right   * fan.Depth;
				position += normal  * (index * StackSpacing);
				rotation = Rotation.FromAxis( normal, fan.Angle ) * rotation;
				break;
			}

			case ZoneLayout.Grid:
			{
				int columns = Math.Max( GridColumns, 1 );
				int column  = index % columns;
				int row     = index / columns;
				position += right   * (column * (CardMesh.Width + CardSpacing));
				position -= forward * (row * (CardMesh.Height + CardSpacing));
				position += normal  * (index * StackSpacing);
				break;
			}

			case ZoneLayout.Freeform: break;
		}

		return new Transform( position, rotation );
	}


	private void RefreshProxyMembership()
	{
		CardsInternal.Clear();

		foreach ( CardObject card in Scene.GetAllComponents<CardObject>() )
		{
			if ( card.ZoneId == ZoneId )
				CardsInternal.Add( card );
		}

		CardsInternal.Sort( ( left, right ) => left.StackIndex.CompareTo( right.StackIndex ) );
	}


	private float EffectiveStackSpacing()
	{
		float spacing = MathF.Max( StackSpacing, 0f );

		if ( EnforcePhysicalStackSpacing )
		{
			spacing = MathF.Max( spacing, CardMesh.Width * MathF.Max( VisibleStackSpacingRatio, 0f ) );
			spacing = MathF.Max( spacing, CardMesh.Thickness * 1.05f );
		}

		return spacing;
	}


	private void ReindexAndLayout( bool animate )
	{
		CardCount = CardsInternal.Count;

		for ( int index = 0; index < CardsInternal.Count; index++ )
		{
			CardObject card = CardsInternal[index];
			card.StackIndex = index;

			if ( ActiveLayout == ZoneLayout.Freeform )
				continue;

			Transform pose = GetCardPose( index );

			if ( animate )
				card.MoveTo( pose );
			else
				card.SnapTo( pose );
		}
	}


	private void ApplyCardState( CardObject card, MtgZoneCardState requested )
	{
		MtgZoneCardState state = requested == MtgZoneCardState.ZoneDefault? DefaultCardState : requested;

		switch ( state )
		{
			case MtgZoneCardState.Preserve: break;
			case MtgZoneCardState.Concealed: card.Conceal(); break;
			case MtgZoneCardState.OwnerOnly:
				card.Conceal();
				card.ShareIdentityWithOwner();
				break;
			case MtgZoneCardState.Front: card.Reveal(); break;
			case MtgZoneCardState.PrintedBack: card.Reveal( 1 ); break;
			case MtgZoneCardState.ZoneDefault: throw new InvalidOperationException( "Zone default state was not resolved." );
		}
	}


	private void RegisterCurrentZoneId()
	{
		if ( _registeredZoneId != Guid.Empty && Registry.TryGetValue( _registeredZoneId, out ZoneObject? previous ) && ReferenceEquals( previous, this ) )
			Registry.Remove( _registeredZoneId );

		_registeredZoneId = ZoneId;

		if ( ZoneId != Guid.Empty )
			Registry[ZoneId] = this;
	}


	protected void RequireAuthority()
	{
		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Only zone authority can change zone contents." );
	}
}
