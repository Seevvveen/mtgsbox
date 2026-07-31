#nullable enable

using Sandbox.Classes.Cards.ManaSymbols;
using System;
using System.Text;
namespace Sandbox.Classes;

/// <summary>
///     Owns keyed, projected in-world values for any game component.
/// </summary>
public sealed class InWorldValueRenderer : Component
{
	private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>( StringComparer.Ordinal );


	public void SetValue( string key, string text, Vector3 localOffset, Color accent, float width = 0f )
	{
		SetEntry(
				 key,
				 text,
				 localOffset,
				 InWorldValueStyle.Default.WithAccent( accent ),
				 width,
				 null
				);
	}


	public void SetAction( string key, string text, Vector3 localOffset, Color accent, Action callback, float width = 0f )
	{
		ArgumentNullException.ThrowIfNull( callback );

		SetEntry(
				 key,
				 text,
				 localOffset,
				 InWorldValueStyle.Default.WithAccent( accent ),
				 width,
				 callback
				);
	}


	public void SetTurn( string key, string playerName, bool active, Vector3 localOffset )
	{
		if ( !active )
		{
			Remove( key );

			return;
		}

		SetValue( key, string.IsNullOrWhiteSpace( playerName )? "YOUR TURN" : $"{playerName}: TURN", localOffset, new Color( 1f, 0.72f, 0.18f ), CardMesh.Width * 1.25f );
	}


	public void SetManaPool( string key, IReadOnlyDictionary<ManaType, int> mana, Vector3 localOffset )
	{
		ArgumentNullException.ThrowIfNull( mana );
		StringBuilder text = new StringBuilder();
		AppendMana( text, mana, ManaType.White, "W" );
		AppendMana( text, mana, ManaType.Blue, "U" );
		AppendMana( text, mana, ManaType.Black, "B" );
		AppendMana( text, mana, ManaType.Red, "R" );
		AppendMana( text, mana, ManaType.Green, "G" );
		AppendMana( text, mana, ManaType.Colorless, "C" );

		if ( text.Length == 0 )
		{
			Remove( key );

			return;
		}

		SetValue( key, text.ToString(), localOffset, new Color( 0.52f, 0.76f, 1f ), CardMesh.Width * 1.2f );
	}


	public bool Remove( string key )
	{
		if ( !_entries.Remove( key, out Entry? entry ) )
			return false;

		if ( entry.GameObject.IsValid() )
			entry.GameObject.Destroy();

		return true;
	}


	public void Clear()
	{
		foreach ( Entry entry in _entries.Values )
		{
			if ( entry.GameObject.IsValid() )
				entry.GameObject.Destroy();
		}

		_entries.Clear();
	}


	protected override void OnUpdate()
	{
		if ( Application.IsHeadless )
			return;

		foreach ( Entry entry in _entries.Values )
			Position( entry );
	}


	protected override void OnDestroy()
	{
		Clear();
		base.OnDestroy();
	}


	private void SetEntry( string key, string text, Vector3 localOffset, InWorldValueStyle style, float width, Action? callback )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( key );

		if ( string.IsNullOrWhiteSpace( text ) )
		{
			Remove( key );

			return;
		}

		width = width > 0f? width : CardMesh.Width * 0.8f;

		if ( !_entries.TryGetValue( key, out Entry? entry ) )
		{
			GameObject child = new GameObject( GameObject, true, $"World Value: {key}" );
			Decal?     decal = child.Components.Create<Decal>();
			decal.Depth            = MathF.Max( CardMesh.Thickness * 4f, 2f );
			decal.Rotation         = 0f;
			decal.AttenuationAngle = 0f;
			decal.LifeTime         = 0f;
			decal.ColorTint        = Color.White;
			decal.SortLayer        = 4;
			entry                  = new Entry { GameObject = child, Decal = decal, LocalOffset = localOffset };
			_entries.Add( key, entry );
		}

		entry.LocalOffset = localOffset;
		entry.Decal.Size  = new Vector2( width, width / InWorldValueTextureRenderer.Aspect );

		Texture texture = InWorldValueTextureRenderer.BuildLabel( text, style );

		if ( entry.Texture != texture )
		{
			entry.Decal.Decals = [ new DecalDefinition { ColorTexture = texture, Width = 1f, Height = 1f, ColorMix = 1f } ];
			entry.Texture      = texture;
		}

		if ( callback is null )
		{
			entry.Action?.Destroy();
			entry.Action = null;
		}
		else
		{
			entry.Action ??= entry.GameObject.Components.Create<InWorldActionButton>();
			entry.Action.Configure( key, callback );
			BoxCollider? collider = entry.GameObject.Components.GetOrCreate<BoxCollider>();
			collider.IsTrigger = true;
			collider.Scale     = new Vector3( width, width / InWorldValueTextureRenderer.Aspect, MathF.Max( CardMesh.Thickness, 1f ) );
		}

		Position( entry );
	}


	private void Position( Entry entry )
	{
		entry.GameObject.WorldPosition = WorldPosition + WorldRotation * entry.LocalOffset;
		entry.GameObject.WorldRotation = Rotation.LookAt( -WorldRotation.Up, WorldRotation.Left );
	}


	private static void AppendMana( StringBuilder text, IReadOnlyDictionary<ManaType, int> mana, ManaType type, string symbol )
	{
		if ( !mana.TryGetValue( type, out int amount ) || amount <= 0 )
			return;

		if ( text.Length > 0 )
			text.Append( "  " );

		text.Append( symbol );
		text.Append( ':' );
		text.Append( amount );
	}


	private sealed class Entry
	{
		public required GameObject           GameObject  { get; init; }
		public required Decal                Decal       { get; init; }
		public          InWorldActionButton? Action      { get; set; }
		public          Texture?             Texture     { get; set; }
		public          Vector3              LocalOffset { get; set; }
	}
}

public sealed class InWorldActionButton : Component
{
	private Action? _callback;
	public  string  ActionId { get; private set; } = string.Empty;


	internal void Configure( string actionId, Action callback )
	{
		ActionId  = actionId;
		_callback = callback;
	}


	public void Invoke() { _callback?.Invoke(); }
}
