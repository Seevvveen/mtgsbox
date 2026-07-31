#nullable enable

using Sandbox.Classes.Cards;
using System;
namespace Sandbox.Framework.Table;

/// <summary>
///     Shared table coordinate system used by game rules, player zones, and the
///     local overview camera.
/// </summary>
public sealed class TableAnchor : Component
{
	[Property] public float PlayerRadius { get; set; } = CardMesh.DefaultWidth * 2.4f;

	[Property] public float CameraHeight { get; set; } = CardMesh.DefaultWidth * 5f;

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
		float    angle        = 360f * index / count - 90f;
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
	public Transform OverviewCamera()
	{
		float   tilt = CameraHeight * MathF.Tan( CameraPitch.DegreeToRadian() );
		Vector3 eye  = WorldPosition + WorldRotation * new Vector3( 0f, -tilt, CameraHeight );

		return new Transform( eye, Rotation.LookAt( ( WorldPosition - eye ).Normal, WorldRotation.Forward ) );
	}
}
