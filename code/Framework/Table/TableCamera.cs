#nullable enable

using System;
using Sandbox.Framework;
namespace Sandbox.Framework.Table;

/// <summary>
///     Smooth local camera for the MTG table.
/// </summary>
public sealed class TableCamera : Component
{
	private           TableAnchor? _anchor;
	private           CameraComponent? _camera;
	[Property] public float        Smoothing { get; set; } = 8f;
	[Property] public bool         Orthographic { get; set; } = true;
	[Property] public float        ViewHeight { get; set; } = 2000f;


	protected override void OnUpdate()
	{
		_anchor ??= Scene.Get<TableAnchor>();
		_camera ??= GetComponent<CameraComponent>();

		if ( _anchor is null )
			return;

		if ( _camera is not null )
		{
			_camera.Orthographic       = Orthographic;
			_camera.OrthographicHeight = MathF.Max( ViewHeight, 1f );
		}

		Seat?     localSeat = Scene.GetAllComponents<Seat>().FirstOrDefault( seat => seat.IsLocal );
		Transform target    = _anchor.OverviewCamera( localSeat );
		float     amount = 1f - MathF.Exp( -Smoothing * Time.Delta );
		WorldPosition = Vector3.Lerp( WorldPosition, target.Position, amount );
		WorldRotation = Rotation.Slerp( WorldRotation, target.Rotation, amount );
	}
}
