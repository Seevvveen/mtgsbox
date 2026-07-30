#nullable enable

using System;

namespace Sandbox.Classes;

/// <summary>
/// Smooth local camera for the MTG table.
/// </summary>
public sealed class MtgTableCamera : Component
{
	[Property]
	public float Smoothing { get; set; } = 8f;

	private MtgTableAnchor? _anchor;

	protected override void OnUpdate()
	{
		_anchor ??= Scene.Get<MtgTableAnchor>();

		if ( _anchor is null )
			return;

		Transform target = _anchor.OverviewCamera();
		float amount =
			1f - MathF.Exp( -Smoothing * Time.Delta );
		WorldPosition = Vector3.Lerp(
			WorldPosition,
			target.Position,
			amount );
		WorldRotation = Rotation.Slerp(
			WorldRotation,
			target.Rotation,
			amount );
	}
}
