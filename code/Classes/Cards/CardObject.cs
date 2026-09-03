#nullable enable

using Sandbox.Classes.Database.Types;
using Sandbox.Classes.Decals;
using Sandbox.Classes.Zones;
using System;
using System.Threading.Tasks;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Classes.Cards;

internal static class CardVisibilityPolicy
{
	public static Guid KnownPrintingId( Guid revealedPrintingId, Guid privatePrintingId )
	{
		return revealedPrintingId != Guid.Empty? revealedPrintingId : privatePrintingId;
	}

	public static bool IsPrivateView( Guid knownPrintingId, Guid revealedPrintingId )
	{
		return knownPrintingId != Guid.Empty && revealedPrintingId == Guid.Empty;
	}
}

/// <summary>
///     Runtime representation of one physical card in the world.
///     Hidden card identities are kept authority-side and are only sent publicly
///     when revealed. A concealed client can render the standard card back without
///     receiving the underlying Scryfall printing ID.
/// </summary>
public sealed class CardObject : Component
{
	private const float MoveSpeed      = 12f;
	private const float FlipSpeed      = 540f;
	private const float FlipLiftMax    = 4f;
	private const float HoverLift      = 1.5f;
	private const float PulseDuration  = 0.5f;
	private const float PulseRise      = 2.5f;
	private const float ThrowMaxTime   = 2f;
	private const float ThrowRestTime  = 1f;
	private const float ThrowRestSpeed = 8f;

	private Guid    _authoritativePrintingId;
	private Vector3 _easedPosition;
	private Guid    _failedPrintingId;
	private float   _flip;
	private bool    _glow;
	private float   _highlightAmount;
	private Color   _highlightTint = Color.White;

	private float _hover;
	private float _hoverTarget;
	private GameObject? _localDragPreview;
	private ModelRenderer? _localDragRenderer;
	private bool _localDragGrabObserved;
	private Guid _localDragOriginZoneId;
	private bool _localDragReleased;
	private float _localDragReleaseAge;
	private bool _rendererEnabledBeforeDrag;
	private bool  _moving;
	private Guid  _privatePrintingId;
	private float _pulseAge = -1f;
	private bool? _renderedConcealed;
	private int   _renderedMaterialVersion;
	private Guid  _renderedPrintingId;
	private bool  _renderedPrivateView;

	private ModelRenderer? _renderer;
	private int            _renderGeneration;
	private string?        _requestedVisualKey;
	private float          _restAge;

	private Vector3    _targetPosition;
	private Rotation   _targetRotation;
	private float      _throwAge;
	private Transform? _throwHome;

	private bool _thrown;
	/// <summary>
	///     Printing identity currently known to every client. Empty while hidden.
	/// </summary>
	[Sync] public Guid RevealedPrintingId { get; set; }

	/// <summary>
	///     Public physical face: -1 concealed, 0 front, 1 printed back.
	/// </summary>
	[Sync] public int FaceIndex { get; set; } = -1;

	/// <summary>
	///     Optional ordering value for a deck, pile, or hand system.
	/// </summary>
	[Sync] public int StackIndex { get; set; }

	/// <summary>
	///     Stable identity of the zone currently containing this card.
	/// </summary>
	[Sync] public Guid ZoneId { get; set; }

	[Sync] public Guid OwnerPlayerId { get; set; }

	[Sync] public Guid ControllerPlayerId { get; set; }

	[Sync] public Guid GrabbedByPlayerId { get; set; }

	[Sync] public bool IsDeclaredCommander { get; set; }

	[Sync] public bool Tapped { get; set; }

	/// <summary>
	///     The printing known to this local peer, if it is entitled to know it.
	/// </summary>
	public Guid KnownPrintingId
	{
		get { return CardVisibilityPolicy.KnownPrintingId( RevealedPrintingId, _privatePrintingId ); }
	}

	public bool IsConcealed
	{
		get { return FaceIndex < 0; }
	}

	public bool HasPrintedBack { get; private set; }

	public Transform RestPose
	{
		get { return new Transform( _targetPosition, _targetRotation ); }
	}


	protected override void OnAwake()
	{
		BoxCollider? collider = GetOrAddComponent<BoxCollider>();
		collider.Scale = new Vector3( CardMesh.Width, CardMesh.Height, CardMesh.Thickness );

		_easedPosition  = _targetPosition = WorldPosition;
		_targetRotation = WorldRotation;
		_flip           = FaceIndex == 0? 0f : 180f;

		if ( Application.IsHeadless )
			return;

		_renderer       = GetOrAddComponent<ModelRenderer>();
		_renderer.Model = CardMesh.Shared;
		GetOrAddComponent<CardValueIndicators>();
		BeginVisualRefresh();
	}


	protected override void OnDestroy()
	{
		DestroyLocalDragPreview();
		_renderGeneration++;
		base.OnDestroy();
	}


	public void BeginLocalDragPreview()
	{
		if ( Application.IsHeadless || _renderer is null || _localDragPreview is not null )
			return;

		_localDragPreview = new GameObject( true, "Local Card Drag Preview" );
		_localDragPreview.Tags.Add( "dragging" );
		_localDragRenderer = _localDragPreview.Components.Create<ModelRenderer>();
		_rendererEnabledBeforeDrag = _renderer.Enabled;
		_renderer.Enabled = false;
		_localDragOriginZoneId = ZoneId;
		_localDragGrabObserved = false;
		_localDragReleased = false;
		_localDragReleaseAge = 0f;
		CopyLocalDragAppearance();
	}


	public void UpdateLocalDragPreview( Transform pose )
	{
		if ( _localDragPreview is null )
			return;

		_localDragPreview.WorldPosition = pose.Position;
		_localDragPreview.WorldRotation = pose.Rotation;
		_localDragPreview.LocalScale = WorldScale;
		CopyLocalDragAppearance();
	}


	public void ReleaseLocalDragPreview()
	{
		if ( _localDragPreview is null )
			return;

		_localDragReleased = true;
		_localDragReleaseAge = 0f;
	}


	/// <summary>
	///     Authority-side initialization. This does not reveal the identity.
	/// </summary>
	public void SetCard( Guid printingId )
	{
		if ( printingId == Guid.Empty )
			throw new ArgumentException( "Printing ID cannot be empty.", nameof(printingId) );

		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Only the card authority can assign its hidden identity." );

		_authoritativePrintingId = printingId;
		_privatePrintingId       = OwnerPlayerId == Connection.Local.Id? printingId : Guid.Empty;
		RevealedPrintingId       = Guid.Empty;
		FaceIndex                = -1;
		BeginVisualRefresh();
	}


	/// <summary>
	///     Makes the card identity and selected physical face public.
	/// </summary>
	public void Reveal( int faceIndex = 0 )
	{
		if ( faceIndex is < 0 or > 1 )
			throw new ArgumentOutOfRangeException( nameof(faceIndex) );

		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Only the card authority can reveal it." );

		if ( _authoritativePrintingId == Guid.Empty )
			throw new InvalidOperationException( "Assign a printing before revealing the card." );

		if ( faceIndex == 1 )
			RequirePrintedBack( _authoritativePrintingId );

		RevealedPrintingId = _authoritativePrintingId;
		FaceIndex          = faceIndex;
		RememberIdentityForAllPlayers();
	}


	/// <summary>
	///     Shows the other printed face of a public double-faced card.
	/// </summary>
	public void FlipPrintedFace()
	{
		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Printed faces must be changed by card authority." );

		if ( RevealedPrintingId == Guid.Empty )
			throw new InvalidOperationException( "A concealed card cannot transform publicly." );

		RequirePrintedBack( RevealedPrintingId );

		FaceIndex = FaceIndex == 1? 0 : 1;
	}


	public void SetTapped( bool tapped )
	{
		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Only card authority can change tapped state." );

		Tapped = tapped;
	}


	private static void RequirePrintedBack( Guid printingId )
	{
		NormalizedCard? card;

		try
		{
			card = RuntimeCardDatabase.GetCard( printingId );
		}
		catch ( InvalidOperationException exception )
		{
			throw new InvalidOperationException( "The card database must be ready before selecting a " + "printed back face.", exception );
		}

		if ( card is null || !CardFaceRenderer.HasPrintedBack( card ) )
			throw new InvalidOperationException( "This printing has no second printed face." );
	}


	/// <summary>
	///     Conceals the card and removes its identity from future public state.
	/// </summary>
	public void Conceal()
	{
		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Only the card authority can conceal it." );

		FaceIndex          = -1;
		RevealedPrintingId = Guid.Empty;
	}


	/// <summary>
	///     Sends hidden identity to only the network owner, for private hands.
	///     World rendering remains concealed.
	/// </summary>
	public void ShareIdentityWithOwner()
	{
		ShareIdentityWith( OwnerPlayerId );
	}


	/// <summary>
	///     Remembers this card's identity for one player. Knowledge survives the
	///     card becoming concealed again, matching tabletop revealed-card memory.
	/// </summary>
	public void ShareIdentityWith( Guid playerId )
	{
		if ( GameObject.Network.IsProxy || _authoritativePrintingId == Guid.Empty || playerId == Guid.Empty )
			return;

		if ( !Networking.IsActive )
		{
			if ( playerId == Connection.Local.Id )
				ReceivePrivateIdentity( _authoritativePrintingId );

			return;
		}

		using ( Rpc.FilterInclude( connection => connection.Id == playerId ) )
			ReceivePrivateIdentity( _authoritativePrintingId );
	}


	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ReceivePrivateIdentity( Guid printingId )
	{
		_privatePrintingId = printingId;
	}


	private void RememberIdentityForAllPlayers()
	{
		if ( _authoritativePrintingId == Guid.Empty )
			return;

		_privatePrintingId = _authoritativePrintingId;
		ReceivePrivateIdentity( _authoritativePrintingId );
	}


	public bool TryGetKnownCard( out NormalizedCard? card )
	{
		Guid printingId = KnownPrintingId;

		if ( printingId == Guid.Empty )
		{
			card = null;

			return false;
		}

		try
		{
			card = RuntimeCardDatabase.GetCard( printingId );

			return card is not null;
		}
		catch ( InvalidOperationException )
		{
			card = null;

			return false;
		}
	}


	public void MoveTo( Transform pose )
	{
		if ( GameObject.Network.IsProxy )
		{
			MoveToOwner( pose );

			return;
		}

		_easedPosition  = WorldPosition;
		_targetPosition = pose.Position;
		_targetRotation = pose.Rotation;
		_moving         = true;
	}


	[Rpc.Owner]
	private void MoveToOwner( Transform pose )
	{
		MoveTo( pose );
	}


	public void SnapTo( Transform pose )
	{
		if ( GameObject.Network.IsProxy )
		{
			SnapToOwner( pose );

			return;
		}

		_easedPosition  = _targetPosition = pose.Position;
		_targetRotation = pose.Rotation;
		_moving         = false;
		WorldPosition   = _easedPosition;
		WorldRotation   = FaceRotation();
	}


	[Rpc.Owner]
	private void SnapToOwner( Transform pose )
	{
		SnapTo( pose );
	}


	/// <summary>
	///     Requests an authoritative move into an MTG zone. Non-freeform zones
	///     choose their own layout pose; freeform zones keep the supplied drop
	///     position and align the card to the zone.
	/// </summary>
	public void PlaceInZone( Guid zoneId, Transform freeformPose )
	{
		if ( GameObject.Network.IsProxy )
			throw new InvalidOperationException( "Zone placement must be performed by card authority." );

		ZoneObject? zone = ZoneObject.Find( zoneId );

		if ( zone is null || !zone.CanAccept( this ) )
		{
			MoveTo( RestPose );
			Pulse();

			return;
		}

		bool freeform = zone.ActiveLayout == ZoneLayout.Freeform;
		zone.AddCard( this, animate: !freeform );

		if ( freeform )
			MoveTo( freeformPose );
	}


	public void Throw( Vector3 velocity, Vector3 angularVelocity )
	{
		if ( GameObject.Network.IsProxy )
		{
			ThrowOwner( velocity, angularVelocity );

			return;
		}

		_throwHome = new Transform( _targetPosition, _targetRotation );

		Rigidbody? rigidbody = GetOrAddComponent<Rigidbody>();
		rigidbody.MotionEnabled   = true;
		rigidbody.Gravity         = true;
		rigidbody.EnhancedCcd     = true;
		rigidbody.Velocity        = velocity;
		rigidbody.AngularVelocity = angularVelocity;

		_thrown   = true;
		_throwAge = 0f;
		_restAge  = 0f;
	}


	[Rpc.Owner]
	private void ThrowOwner( Vector3 velocity, Vector3 angularVelocity )
	{
		Throw( velocity, angularVelocity );
	}


	public void CancelThrow()
	{
		if ( _thrown )
			StripThrow();
	}


	public void SetHover( float amount )
	{
		_hoverTarget = amount.Clamp( 0f, 1f );

		if ( !_thrown )
			LocalScale = 1f + 0.04f * _hoverTarget;
	}


	public void Pulse()
	{
		_pulseAge = 0f;
	}


	public void SetGlow( bool enabled )
	{
		_glow = enabled;
	}


	public void Highlight( Color tint, float amount )
	{
		_highlightTint   = tint;
		_highlightAmount = amount.Clamp( 0f, 1f );
	}


	public void ClearHighlight()
	{
		_highlightTint   = Color.White;
		_highlightAmount = 0f;
		_hoverTarget     = 0f;

		if ( !_thrown )
			LocalScale = 1f;
	}


	protected override void OnUpdate()
	{
		RefreshVisualIfChanged();
		UpdateEmphasis();
		UpdateLocalDragPreviewState();

		if ( GameObject.Network.IsProxy )
			return;

		if ( GameObject.Tags.Has( "dragging" ) )
			return;

		if ( _thrown )
		{
			UpdateThrow();

			return;
		}

		if ( _moving )
		{
			float interpolation = 1f - MathF.Exp( -MoveSpeed * Time.Delta );
			_easedPosition = Vector3.Lerp( _easedPosition, _targetPosition, interpolation );

			if ( Vector3.DistanceBetween( _easedPosition, _targetPosition ) < 0.05f )
			{
				_easedPosition = _targetPosition;
				_moving        = false;
			}
		}
		else
			_easedPosition = _targetPosition;

		float flipTarget = LocallyVisibleFaceIndex() == 0? 0f : 180f;
		_flip  = _flip.Approach( flipTarget, FlipSpeed     * Time.Delta );
		_hover = _hover.Approach( _hoverTarget, Time.Delta * 10f );

		float lift = MathF.Sin( _flip.DegreeToRadian() ) * FlipLiftMax + PulseAmount() * PulseRise + _hover * HoverLift;
		WorldPosition = _easedPosition + Vector3.Up * lift;
		WorldRotation = FaceRotation();
	}


	private void UpdateLocalDragPreviewState()
	{
		if ( _localDragPreview is null )
			return;

		CopyLocalDragAppearance();

		if ( GrabbedByPlayerId == Connection.Local.Id )
			_localDragGrabObserved = true;

		if ( !_localDragReleased )
			return;

		_localDragReleaseAge += Time.Delta;
		bool authoritativeMoveArrived = ZoneId != _localDragOriginZoneId;
		bool authoritativeReleaseArrived = _localDragGrabObserved && GrabbedByPlayerId == Guid.Empty;
		bool authoritativePoseArrived = Vector3.DistanceBetween( WorldPosition, _localDragPreview.WorldPosition ) < 0.5f;

		if ( authoritativeMoveArrived || authoritativeReleaseArrived || authoritativePoseArrived || _localDragReleaseAge >= 2f )
			DestroyLocalDragPreview();
	}


	private void CopyLocalDragAppearance()
	{
		if ( _renderer is null || _localDragRenderer is null )
			return;

		_localDragRenderer.Model = _renderer.Model;
		_localDragRenderer.MaterialOverride = _renderer.MaterialOverride;
		_localDragRenderer.Tint = _renderer.Tint;
	}


	private void DestroyLocalDragPreview()
	{
		if ( _localDragPreview is null )
			return;

		if ( _renderer is not null )
			_renderer.Enabled = _rendererEnabledBeforeDrag;

		_localDragPreview?.Destroy();
		_localDragPreview = null;
		_localDragRenderer = null;
		_localDragGrabObserved = false;
		_localDragOriginZoneId = Guid.Empty;
		_localDragReleased = false;
		_localDragReleaseAge = 0f;
	}


	private void RefreshVisualIfChanged()
	{
		if ( Application.IsHeadless || _renderer is null )
			return;

		Guid   printingId = KnownPrintingId;
		bool   concealed  = printingId == Guid.Empty;
		bool   privateView = CardVisibilityPolicy.IsPrivateView( printingId, RevealedPrintingId );
		string desiredKey = concealed
			? $"material:{CardMaterialFactory.CacheVersion}|concealed"
			: $"material:{CardMaterialFactory.CacheVersion}|{printingId:N}|{( privateView? "private" : "public" )}";

		if ( _renderedMaterialVersion == CardMaterialFactory.CacheVersion && _renderedConcealed == concealed &&
			( concealed || _renderedPrintingId == printingId && _renderedPrivateView == privateView ) )
			return;

		if ( string.Equals( _requestedVisualKey, desiredKey, StringComparison.Ordinal ) )
			return;

		BeginVisualRefresh();
	}


	private void BeginVisualRefresh()
	{
		if ( Application.IsHeadless || _renderer is null )
			return;

		int  generation = ++_renderGeneration;
		Guid printingId = KnownPrintingId;
		bool concealed  = printingId == Guid.Empty;
		bool privateView = CardVisibilityPolicy.IsPrivateView( printingId, RevealedPrintingId );
		_requestedVisualKey = concealed
			? $"material:{CardMaterialFactory.CacheVersion}|concealed"
			: $"material:{CardMaterialFactory.CacheVersion}|{printingId:N}|{( privateView? "private" : "public" )}";

		if ( concealed )
		{
			_ = ApplyConcealedAsync( generation );

			return;
		}

		NormalizedCard? card;

		try
		{
			card = RuntimeCardDatabase.GetCard( printingId );
		}
		catch ( InvalidOperationException )
		{
			_requestedVisualKey = null;

			return;
		}

		if ( card is null )
		{
			if ( _failedPrintingId != printingId )
			{
				_failedPrintingId = printingId;
				Log.Warning( $"CardObject could not resolve printing " + $"'{printingId}'." );
			}

			_requestedVisualKey = null;

			return;
		}

		_ = ApplyCardAsync( card, generation, privateView );
	}


	private async Task ApplyConcealedAsync( int generation )
	{
		try
		{
			CardTextures textures = await CardFaceRenderer.BuildConcealedAsync();

			if ( generation != _renderGeneration || _renderer is null )
				return;

			textures.ApplyTo( _renderer );
			HasPrintedBack      = false;
			_renderedPrintingId = Guid.Empty;
			_renderedConcealed  = true;
			_renderedMaterialVersion = CardMaterialFactory.CacheVersion;
			_renderedPrivateView = false;
			_requestedVisualKey = null;
		}
		catch ( Exception exception )
		{
			if ( generation == _renderGeneration )
			{
				_requestedVisualKey = null;
				Log.Warning( $"Unable to render a concealed card: " + $"{exception.Message}" );
			}
		}
	}


	private async Task ApplyCardAsync( NormalizedCard card, int generation, bool privateView )
	{
		try
		{
			CardTextures textures = await CardFaceRenderer.BuildCardAsync( card, privateView: privateView );

			if ( generation != _renderGeneration || _renderer is null )
				return;

			textures.ApplyTo( _renderer );
			HasPrintedBack      = textures.HasPrintedBack;
			_renderedPrintingId = card.Gameplay.ScryfallId;
			_renderedConcealed  = false;
			_renderedMaterialVersion = CardMaterialFactory.CacheVersion;
			_renderedPrivateView = privateView;
			_failedPrintingId   = Guid.Empty;
			_requestedVisualKey = null;
		}
		catch ( Exception exception )
		{
			if ( generation == _renderGeneration )
			{
				_requestedVisualKey = null;
				Log.Warning( $"Unable to render card printing " + $"'{card.Gameplay.ScryfallId}': " + $"{exception.Message}" );
			}
		}
	}


	private void UpdateThrow()
	{
		_throwAge += Time.Delta;
		Rigidbody? rigidbody = GetComponent<Rigidbody>();
		bool       atRest    = rigidbody is null || rigidbody.Velocity.Length < ThrowRestSpeed && rigidbody.AngularVelocity.Length < ThrowRestSpeed;
		_restAge = atRest? _restAge + Time.Delta : 0f;

		if ( _restAge < ThrowRestTime && _throwAge < ThrowMaxTime )
			return;

		StripThrow();

		if ( _throwHome is Transform home )
			MoveTo( home );
	}


	private void StripThrow()
	{
		if ( GetComponent<Rigidbody>() is Rigidbody rigidbody )
		{
			rigidbody.MotionEnabled = false;
			rigidbody.Destroy();
		}

		_thrown    = false;
		LocalScale = 1f;
	}


	private void UpdateEmphasis()
	{
		if ( _renderer is null )
			return;

		if ( _pulseAge >= 0f )
		{
			_pulseAge += Time.Delta;

			if ( _pulseAge >= PulseDuration )
				_pulseAge = -1f;
		}

		float pulse  = PulseAmount();
		float glow   = _glow? 0.3f + 0.2f * ( 0.5f + 0.5f * MathF.Sin( Time.Now * 4f ) ) : 0f;
		float amount = MathF.Max( MathF.Max( pulse, glow ), _highlightAmount );
		Color tint   = _highlightAmount > 0f? _highlightTint : new Color( 1f, 0.78f, 0.25f );
		_renderer.Tint = Color.Lerp( Color.White, tint, amount );
	}


	private float PulseAmount()
	{
		return _pulseAge < 0f? 0f : MathF.Sin( _pulseAge / PulseDuration * MathF.PI );
	}


	private int LocallyVisibleFaceIndex()
	{
		if ( RevealedPrintingId != Guid.Empty )
			return FaceIndex;

		return _privatePrintingId != Guid.Empty? 0 : -1;
	}


	private Rotation FaceRotation()
	{
		Rotation physicalRotation = Tapped? Rotation.FromAxis( _targetRotation.Up, 90f ) * _targetRotation : _targetRotation;

		return Rotation.FromAxis( physicalRotation.Right, _flip ) * physicalRotation;
	}
}
