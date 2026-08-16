#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Zones;
using Sandbox.Framework;
using System;
namespace Sandbox.Framework.Table;

/// <summary>
///     Shared table coordinate system used by game rules, player zones, and the
///     local overview camera.
/// </summary>
public sealed class TableAnchor : Component
{
	[Property] public float PlayerRadius { get; set; } = 500f;

	[Property] public float CameraHeight { get; set; } = 1000;

	[Property] public float CameraPitch { get; set; } = 5f;


	/// <summary>
	///     computes a player seat transform arranged in a circle around a center point, facing inward.
	/// </summary>
	/// <param name = "index"> </param>
	/// <param name = "count"> </param>
	/// <returns> </returns>
	public Transform PlayerSpot( int index, int count )
	{
		count = Math.Max( count, 1 );
		float    angle        = 360f * index / count - 90;
		Vector3  local        = Rotation.FromYaw( angle ) * new Vector3( PlayerRadius, 0f, 0f );
		Vector3  position     = WorldPosition + WorldRotation * local;
		Vector3  towardCenter = ( WorldPosition - position ).WithZ( 0f ).Normal;
		Rotation rotation     = Rotation.LookAt( towardCenter, WorldRotation.Up );

		return new Transform( position, rotation );
	}


	/// <summary>
	///     This computes a top-down / angled overview camera transform that looks at the center point.
	/// </summary>
	/// <returns> </returns>
	public Transform OverviewCamera( Seat? viewer = null )
	{
		float   tilt       = CameraHeight * MathF.Tan( CameraPitch.DegreeToRadian() );
		Vector3 screenUp   = viewer is null? WorldRotation.Forward : ( WorldPosition - viewer.WorldPosition ).WithZ( 0f ).Normal;
		Vector3 eyeOffset  = viewer is null? WorldRotation.Backward : -screenUp;
		Vector3 eye        = WorldPosition + eyeOffset * tilt + WorldRotation.Up * CameraHeight;

		return new Transform( eye, Rotation.LookAt( ( WorldPosition - eye ).Normal, screenUp ) );
	}
}
