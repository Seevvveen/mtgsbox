#nullable enable

using Sandbox.Classes.CardDatabase;
using Sandbox.Classes.Database.Types;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Classes;

/// <summary>
/// Small, disposable scene bootstrap for exercising card rendering, hidden
/// information, MTG zones, layouts, shuffling, drawing, and double-faced cards.
/// Add this component to an empty scene object and enter play mode.
/// </summary>
public sealed class MtgTestBootstrap : Component
{
	[Property]
	public bool RunOnStart { get; set; } = true;

	[Property]
	public int LibraryCardCount { get; set; } = 12;

	[Property]
	public float ZoneSpacing { get; set; } = 70.5f;

	[Property]
	public float CardWidth { get; set; } = 630f;

	public bool HasBootstrapped { get; private set; }

	private readonly List<GameObject> _spawnedObjects = [];

	protected override void OnStart()
	{
		if ( !RunOnStart ||
			Networking.IsActive && !Networking.IsHost )
		{
			return;
		}

		_ = RunBootstrapAsync();
	}

	public async Task RunBootstrapAsync()
	{
		if ( HasBootstrapped )
			return;

		DatabaseManager? manager = Scene.Get<DatabaseManager>();

		if ( manager is not null )
		{
			DatabaseStartupState state = await manager.Completion;

			if ( state != DatabaseStartupState.Ready )
			{
				Log.Warning(
					"MTG test bootstrap stopped because the card " +
					$"database entered state '{state}'." );
				return;
			}
		}
		else if ( !RuntimeCardDatabase.IsOpen )
		{
			Log.Warning(
				"MTG test bootstrap needs a ready DatabaseManager " +
				"in the scene." );
			return;
		}

		HasBootstrapped = true;
		CardMesh.SetSize( CardWidth );

		ZoneObject library = CreateZone(
			"Test Library",
			MtgZoneKind.Library,
			new Vector3( -ZoneSpacing * 1.5f, 2f, 0f ) );
		ZoneObject graveyard = CreateZone(
			"Test Graveyard",
			MtgZoneKind.Graveyard,
			new Vector3( -ZoneSpacing * 0.5f, 2f, 0f ) );
		ZoneObject exile = CreateZone(
			"Test Exile",
			MtgZoneKind.Exile,
			new Vector3( ZoneSpacing * 0.5f, 2f, 0f ) );
		ZoneObject stack = CreateZone(
			"Test Stack",
			MtgZoneKind.Stack,
			new Vector3( ZoneSpacing * 1.5f, 2f, 0f ) );
		ZoneObject hand = CreateZone(
			"Test Hand",
			MtgZoneKind.Hand,
			new Vector3( 0f, -ZoneSpacing, 0f ) );
		ZoneObject battlefield = CreateZone(
			"Test Battlefield",
			MtgZoneKind.Battlefield,
			new Vector3( 0f, ZoneSpacing, 0f ) );

		NormalizedCard island = RequireCard( "Island" );
		NormalizedCard mountain = RequireCard( "Mountain" );
		NormalizedCard lightningBolt =
			RequireCard( "Lightning Bolt" );
		NormalizedCard opt = RequireCard( "Opt" );

		for ( int index = 0;
			index < Math.Max( LibraryCardCount, 1 );
			index++ )
		{
			NormalizedCard definition = index % 2 == 0
				? island
				: mountain;
			CardObject card = CreateCard(
				$"Library Card {index + 1}",
				definition );
			library.AddCard(
				card,
				MtgZoneCardState.Concealed,
				animate: false );
		}

		library.Shuffle();

		for ( int index = 0; index < 4; index++ )
		{
			CardObject? drawn = library.DrawTop();

			if ( drawn is not null )
				hand.AddCard( drawn, MtgZoneCardState.OwnerOnly );
		}

		CardObject permanent = CreateCard(
			"Public Lightning Bolt",
			lightningBolt );
		permanent.SnapTo( new Transform(
			battlefield.WorldPosition,
			battlefield.WorldRotation ) );
		battlefield.AddCard(
			permanent,
			MtgZoneCardState.Front );
		permanent.MoveTo( new Transform(
			battlefield.WorldPosition +
				battlefield.WorldRotation.Right * -4f,
			battlefield.WorldRotation ) );

		graveyard.AddCard(
			CreateCard( "Graveyard Opt", opt ),
			MtgZoneCardState.Front );
		exile.AddCard(
			CreateCard( "Exiled Lightning Bolt", lightningBolt ),
			MtgZoneCardState.Front );
		stack.AddCard(
			CreateCard( "Stack Opt", opt ),
			MtgZoneCardState.Front );

		NormalizedCard? doubleFaced = FindFirstAvailable(
			"Delver of Secrets // Insectile Aberration",
			"Brutal Cathar // Moonrage Brute",
			"Fable of the Mirror-Breaker // " +
				"Reflection of Kiki-Jiki" );

		if ( doubleFaced is not null &&
			CardFaceRenderer.HasPrintedBack( doubleFaced ) )
		{
			CardObject transformed = CreateCard(
				"Double-Faced Printed Back",
				doubleFaced );
			battlefield.AddCard(
				transformed,
				MtgZoneCardState.PrintedBack );
			transformed.MoveTo( new Transform(
				battlefield.WorldPosition +
					battlefield.WorldRotation.Right * 4f,
				battlefield.WorldRotation ) );
		}
		else
		{
			Log.Warning(
				"MTG test bootstrap could not find a known " +
				"double-faced printing." );
		}

		Log.Info(
			"MTG test bootstrap ready: concealed shuffled library, " +
			"private fanned hand, public battlefield, graveyard, " +
			"exile, stack, and double-faced back test." );
	}

	public void ClearBootstrap()
	{
		for ( int index = _spawnedObjects.Count - 1;
			index >= 0;
			index-- )
		{
			GameObject gameObject = _spawnedObjects[index];

			if ( gameObject.IsValid() )
				gameObject.Destroy();
		}

		_spawnedObjects.Clear();
		HasBootstrapped = false;
	}

	private ZoneObject CreateZone(
		string name,
		MtgZoneKind kind,
		Vector3 localOffset )
	{
		GameObject zoneObject = CreateObject(
			name,
			WorldPosition +
				WorldRotation.Right * localOffset.x +
				WorldRotation.Forward * localOffset.y +
				WorldRotation.Up * localOffset.z );
		zoneObject.WorldRotation = WorldRotation;

		ZoneObject zone =
			zoneObject.Components.Create<ZoneObject>();
		zone.ZoneKind = kind;
		zone.UseRecommendedLayout = true;
		zone.TriggerSize = new Vector3(
			CardMesh.Width,
			CardMesh.Height,
			1f );

		if ( kind == MtgZoneKind.Battlefield )
		{
			zone.TriggerSize = new Vector3(
				CardMesh.Width * 3.5f,
				CardMesh.Height * 2.5f,
				1f );
		}

		zone.RefreshConfiguration();
		SpawnNetworkedIfNeeded( zoneObject );
		return zone;
	}

	private CardObject CreateCard(
		string name,
		NormalizedCard definition )
	{
		GameObject cardObject = CreateObject(
			name,
			WorldPosition + WorldRotation.Up * 2f );
		cardObject.WorldRotation = WorldRotation;

		CardObject card =
			cardObject.Components.Create<CardObject>();
		card.SetCard( definition.Gameplay.ScryfallId );
		SpawnNetworkedIfNeeded( cardObject );
		return card;
	}

	private GameObject CreateObject(
		string name,
		Vector3 position )
	{
		var gameObject = new GameObject(
			GameObject,
			true,
			name )
		{
			WorldPosition = position
		};
		_spawnedObjects.Add( gameObject );
		return gameObject;
	}

	private static void SpawnNetworkedIfNeeded(
		GameObject gameObject )
	{
		if ( Networking.IsActive )
			gameObject.NetworkSpawn();
	}

	private static NormalizedCard RequireCard( string name )
	{
		return FindFirstAvailable( name )
			?? throw new InvalidOperationException(
				$"Test card '{name}' is not in the local database." );
	}

	private static NormalizedCard? FindFirstAvailable(
		params string[] names )
	{
		foreach ( string name in names )
		{
			NormalizedCard[] matches =
				RuntimeCardDatabase.FindByName( name );

			foreach ( NormalizedCard card in matches )
			{
				if ( string.Equals(
					card.Source.Language,
					"en",
					StringComparison.OrdinalIgnoreCase ) &&
					!card.Presentation.Digital )
				{
					return card;
				}
			}

			if ( matches.Length > 0 )
				return matches[0];
		}

		return null;
	}
}
