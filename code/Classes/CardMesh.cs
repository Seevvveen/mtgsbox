using System;
namespace Sandbox.Classes;

/// <summary>
///     Builds the two-sided card model in code (no .vmdl asset needed). Each face is a rounded
///     rectangle (a triangle fan around the centre) so the card has real rounded corners like a
///     physical card. The top face (front) samples the LEFT half of the atlas, the bottom face
///     (back) the RIGHT half. Each card overrides the material per-instance.
///     The size lives here as shared static state on purpose: there is one mesh shared by every card, so
///     there's one size. The active game sets it (<see cref = "SetSize"/>) when it enables. This would only
///     be wrong with two differently-sized games live at once - impossible today (one active game), so it
///     stays a global rather than threading a size through every layout/collider call site.
/// </summary>
public static class CardMesh
{
	public const float DefaultWidth          = 63; // comfortable tabletop scale
	public const float DefaultThickness      = 1f;
	public const float DefaultThicknessRatio = DefaultThickness / DefaultWidth;

	private const int CornerSegments = 6; // smoothness of each rounded corner

	private const           float   EdgeBevel = 0.45f;                             // how much the rim normal tilts toward the faces (soft rounded-edge highlight)
	private static readonly Vector4 RimUv     = new Vector4( 0.004f, 0.5f, 0, 0 ); // sample the face-colour border for the edge

	private static Model _shared;

	/// <summary>
	///     Current card width. Games can shrink/grow cards via <see cref = "SetSize"/>; layout reads this.
	/// </summary>
	public static float Width { get; private set; } = DefaultWidth;

	public static float Height
	{
		get { return Width / CardFaceRenderer.Aspect; }
	}

	public static float ThicknessRatio { get; private set; } = DefaultThicknessRatio;

	public static float Thickness
	{
		get { return Width * ThicknessRatio; }
	}

	private static float CornerRadius
	{
		get { return Width * 0.08f; }
	}

	private static float HalfThickness
	{
		get { return Thickness * 0.5f; }
	}

	public static Model Shared
	{
		get { return _shared ??= Build(); }
	}


	/// <summary>
	///     Drop the cached model so it rebuilds (e.g. after changing the size while iterating).
	/// </summary>
	public static void Invalidate() { _shared = null; }


	/// <summary>
	///     Set the card world size (all cards share one mesh). Rebuilds on the next access.
	/// </summary>
	public static void SetSize( float width )
	{
		width = MathF.Max( width, 0.1f );

		if ( MathF.Abs( width - Width ) < 0.001f )
			return;

		Width   = width;
		_shared = null; // rebuild at the new size
	}


	/// <summary>
	///     Sets thickness as a proportion of width. The actual thickness is
	///     recalculated automatically whenever the card width changes.
	/// </summary>
	public static void SetThicknessRatio( float ratio )
	{
		ratio = MathF.Max( ratio, 0.0001f );

		if ( MathF.Abs( ratio - ThicknessRatio ) < 0.00001f )
			return;

		ThicknessRatio = ratio;
		_shared        = null;
	}


	private static Model Build()
	{
		Material placeholder = CardMaterialFactory.Create( "mtgsbox_card_placeholder", Texture.White, false );
		Mesh     mesh        = new Mesh( placeholder );

		float         hw      = Width / 2f, hh = Height / 2f;
		float         r       = MathF.Min( CornerRadius, MathF.Min( hw, hh ) );
		List<Vector2> outline = RoundedRectOutline( hw, hh, r );

		List<Vertex> verts   = new List<Vertex>();
		List<int>    indices = new List<int>();

		AddFan(
			   verts,
			   indices,
			   outline,
			   HalfThickness,
			   Vector3.Up,
			   new Vector3( 1, 0, 0 ),
			   true,
			   hw,
			   hh
			  );

		AddFan(
			   verts,
			   indices,
			   outline,
			   -HalfThickness,
			   Vector3.Down,
			   new Vector3( -1, 0, 0 ),
			   false,
			   hw,
			   hh
			  );

		AddRim( verts, indices, outline, HalfThickness, EdgeBevel );

		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( Width, Height, Thickness ) );

		return Model.Builder.AddMesh( mesh ).Create();
	}


	// Rounded-rectangle outline, counter-clockwise as seen from +Z.
	private static List<Vector2> RoundedRectOutline( float hw, float hh, float r )
	{
		(Vector2 center, float startDeg)[] corners = new (Vector2 center, float startDeg)[]
													 {
														 ( new Vector2( hw                      - r, hh - r ), 0f ), // top-right
														 ( new Vector2( -( hw         - r ), hh - r ), 90f ),        // top-left
														 ( new Vector2( -( hw         - r ), -( hh - r ) ), 180f ),  // bottom-left
														 ( new Vector2( hw - r, -( hh - r ) ), 270f )                // bottom-right
													 };

		List<Vector2> pts = new List<Vector2>();

		foreach ( ( Vector2 center, float startDeg ) in corners )
		{
			for ( int j = 0; j <= CornerSegments; j++ )
			{
				float a = ( startDeg + 90f * j / CornerSegments ).DegreeToRadian();
				pts.Add( center + new Vector2( MathF.Cos( a ), MathF.Sin( a ) ) * r );
			}
		}

		return pts;
	}


	// Triangle fan around the face centre. Back faces flip winding so the normal points the other way.
	private static void AddFan( List<Vertex> verts, List<int> indices, List<Vector2> outline, float z, Vector3 normal, Vector3 tangent, bool front, float hw, float hh )
	{
		int center = verts.Count;

		verts.Add(
				  MakeVertex(
							 Vector2.Zero,
							 z,
							 normal,
							 tangent,
							 front,
							 hw,
							 hh
							)
				 );

		int first = verts.Count;

		foreach ( Vector2 p in outline )
			verts.Add(
					  MakeVertex(
								 p,
								 z,
								 normal,
								 tangent,
								 front,
								 hw,
								 hh
								)
					 );

		int n = outline.Count;

		for ( int i = 0; i < n; i++ )
		{
			int a = first + i;
			int b = first + ( i + 1 ) % n;

			if ( front )
			{
				indices.Add( center );
				indices.Add( a );
				indices.Add( b );
			}
			else
			{
				indices.Add( center );
				indices.Add( b );
				indices.Add( a );
			}
		}
	}


	// Connects the front and back outlines with a side wall so the card has real thickness. The rim
	// normals tilt slightly toward each face (a soft chamfer) so the sun catches the edge rather than
	// it reading as a flat decal. The whole rim samples the face-colour border of the texture.
	private static void AddRim( List<Vertex> verts, List<int> indices, List<Vector2> outline, float t, float bevel )
	{
		int n   = outline.Count;
		int top = verts.Count;

		for ( int i = 0; i < n; i++ )
			verts.Add( RimVertex( outline, i, n, t, bevel ) );

		int bot = verts.Count;

		for ( int i = 0; i < n; i++ )
			verts.Add( RimVertex( outline, i, n, -t, bevel ) );

		for ( int i = 0; i < n; i++ )
		{
			int a = i, b = ( i + 1 ) % n;

			// Outward-facing winding (verified against a +Y edge): (topA, botA, topB), (topB, botA, botB).
			indices.Add( top + a );
			indices.Add( bot + a );
			indices.Add( top + b );
			indices.Add( top + b );
			indices.Add( bot + a );
			indices.Add( bot + b );
		}
	}


	private static Vertex RimVertex( List<Vector2> outline, int i, int n, float z, float bevel )
	{
		Vector2 p       = outline[i];
		Vector2 prev    = outline[( i - 1 + n ) % n];
		Vector2 next    = outline[( i     + 1 ) % n];
		Vector2 tan     = next - prev;                         // CCW travel direction
		Vector2 outward = new Vector2( tan.y, -tan.x ).Normal; // outward = to the right of CCW travel
		Vector3 normal  = new Vector3( outward.x, outward.y, z > 0? bevel : -bevel ).Normal;

		return new Vertex( new Vector3( p.x, p.y, z ), normal, new Vector3( outward.x, outward.y, 0 ), RimUv );
	}


	private static Vertex MakeVertex( Vector2 p, float z, Vector3 normal, Vector3 tangent, bool front, float hw, float hh )
	{
		float u = ( p.x + hw ) / ( 2f * hw ) * 0.5f;        // front → [0 .. 0.5]
		float v = ( hh - p.y )               / ( 2f * hh ); // v=0 at the top (+Y)

		if ( !front )
			u = 1f - u; // back → [0.5 .. 1]

		return new Vertex( new Vector3( p.x, p.y, z ), normal, tangent, new Vector4( u, v, 0, 0 ) );
	}
}
