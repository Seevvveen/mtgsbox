#nullable enable

using System;

namespace Sandbox.Classes;

/// <summary>
/// Keeps a zone's public card count displayed beside its marker.
/// </summary>
public sealed class ZoneCountIndicator : Component
{
	[Property]
	public bool ShowZero { get; set; } = true;

	[Property]
	public Color Accent { get; set; } =
		new( 0.42f, 0.72f, 1f );

	private ZoneObject? _zone;
	private InWorldValueRenderer? _values;
	private int _lastCount = int.MinValue;
	private MtgZoneKind _lastKind;

	protected override void OnAwake()
	{
		_zone = GetComponent<ZoneObject>();
		_values = GetOrAddComponent<InWorldValueRenderer>();
	}

	protected override void OnUpdate()
	{
		if ( _zone is null || _values is null )
			return;

		if ( !ShowZero && _zone.CardCount == 0 )
		{
			_values.Remove( "zone-count" );
			_lastCount = 0;
			return;
		}

		if ( _lastCount == _zone.CardCount &&
			_lastKind == _zone.ZoneKind )
		{
			return;
		}

		_lastCount = _zone.CardCount;
		_lastKind = _zone.ZoneKind;
		_values.SetValue(
			"zone-count",
			$"{_zone.ZoneKind.ToString().ToUpperInvariant()}: " +
				$"{_zone.CardCount}",
			new Vector3(
				0f,
				-CardMesh.Height * 0.58f,
				MathF.Max( _zone.BaseLift, 0.1f ) ),
			Accent,
			CardMesh.Width );
	}
}

/// <summary>
/// Optional status and quick-action host for one card.
/// </summary>
public sealed class CardValueIndicators : Component
{
	[Property]
	public bool ShowFlipAction { get; set; } = true;

	private CardObject? _card;
	private InWorldValueRenderer? _values;
	private bool _flipVisible;

	protected override void OnAwake()
	{
		_card = GetComponent<CardObject>();
		_values = GetOrAddComponent<InWorldValueRenderer>();
	}

	protected override void OnUpdate()
	{
		if ( _card is null || _values is null )
			return;

		bool shouldShow = ShowFlipAction &&
			_card.HasPrintedBack &&
			!_card.IsConcealed;

		if ( shouldShow == _flipVisible )
			return;

		_flipVisible = shouldShow;

		if ( !shouldShow )
		{
			_values.Remove( "card-flip" );
			return;
		}

		_values.SetAction(
			"card-flip",
			"FLIP",
			new Vector3(
				CardMesh.Width * 0.62f,
				0f,
				CardMesh.Thickness ),
			new Color( 0.72f, 0.48f, 1f ),
			_card.FlipPrintedFace,
			CardMesh.Width * 0.48f );
	}

	public void SetStatus(
		string key,
		string label,
		Color accent,
		int row = 0 )
	{
		_values?.SetValue(
			$"status:{key}",
			label,
			new Vector3(
				-CardMesh.Width * 0.62f,
				row * CardMesh.Height * 0.13f,
				CardMesh.Thickness ),
			accent,
			CardMesh.Width * 0.5f );
	}

	public void RemoveStatus( string key ) =>
		_values?.Remove( $"status:{key}" );

	public void SetQuickAction(
		string key,
		string label,
		Action<CardObject> callback,
		Color accent,
		int row = 0 )
	{
		ArgumentNullException.ThrowIfNull( callback );

		if ( _card is not CardObject card )
			return;

		_values?.SetAction(
			$"action:{key}",
			label,
			new Vector3(
				CardMesh.Width * 0.62f,
				-row * CardMesh.Height * 0.13f,
				CardMesh.Thickness ),
			accent,
			() => callback( card ),
			CardMesh.Width * 0.5f );
	}

	public void RemoveQuickAction( string key ) =>
		_values?.Remove( $"action:{key}" );
}
