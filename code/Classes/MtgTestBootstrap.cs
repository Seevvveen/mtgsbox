#nullable enable

using Sandbox.Classes.CardDatabase;
using Sandbox.Classes.Database.Types;
using System;
using System.Threading.Tasks;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Classes;

/// <summary>
///     Starts a complete two-player test match using the real MTG director. The
///     local connection plays against a fake player named Sparky.
/// </summary>
public sealed class MtgTestBootstrap : Component
{
	private           GameObject? _matchObject;
	[Property] public bool        RunOnStart { get; set; } = true;

	[Property] public string FakePlayerName { get; set; } = "Sparky";

	[Property] public float CardWidth { get; set; } = 63f;

	[Property] public float CardThicknessRatio { get; set; } = CardMesh.DefaultThicknessRatio;

	public bool          HasBootstrapped { get; private set; }
	public GameDirector? Director        { get; private set; }


	protected override void OnStart()
	{
		if ( !RunOnStart || Networking.IsActive && !Networking.IsHost )
			return;

		_ = RunBootstrapAsync();
	}


	public async Task RunBootstrapAsync()
	{
		if ( HasBootstrapped )
			return;

		if ( !await WaitForDatabaseAsync() )
			return;

		if ( Connection.Local is not Connection local )
		{
			Log.Warning( "MTG bootstrap needs a local connection." );

			return;
		}

		NormalizedCard island    = RequireCard( "Island" );
		NormalizedCard mountain  = RequireCard( "Mountain" );
		Deck           localDeck = BuildDeck( "Local Test Deck", island, mountain );
		Deck           botDeck   = BuildDeck( $"{FakePlayerName}'s Test Deck", mountain, island );

		CardMesh.SetSize( CardWidth );
		CardMesh.SetThicknessRatio( CardThicknessRatio );

		_matchObject = new GameObject( GameObject, true, "Bootstrap MTG Match" ) { WorldPosition = WorldPosition, WorldRotation = WorldRotation };
		_matchObject.Components.Create<MtgBootstrapRulesEngine>();
		Director = _matchObject.Components.Create<GameDirector>();

		if ( Networking.IsActive )
			_matchObject.NetworkSpawn();

		if ( !Director.SeatPlayer( local ) )
		{
			Log.Warning( "MTG bootstrap could not seat the local player." );
			ClearBootstrap();

			return;
		}

		PlayerSeat? localSeat = Director.SeatOf( local.Id );
		PlayerSeat? botSeat   = Director.AddBotPlayer( FakePlayerName );

		if ( localSeat is null || botSeat is null )
		{
			Log.Warning( "MTG bootstrap could not create both players." );
			ClearBootstrap();

			return;
		}

		bool localAccepted = Director.AcceptDeckAuthority( localSeat, localDeck );
		bool botAccepted   = Director.AcceptDeckAuthority( botSeat, botDeck );

		if ( !localAccepted || !botAccepted )
		{
			Log.Warning( "MTG bootstrap deck validation failed: " + $"local='{localSeat.DeckStatus}', " + $"bot='{botSeat.DeckStatus}'." );
			ClearBootstrap();

			return;
		}

		Director.SetReadyAuthority( localSeat, true );
		Director.SetReadyAuthority( botSeat, true );
		Director.StartMatchAuthority();

		if ( Director.State != GameState.Mulligan )
		{
			Log.Warning( "MTG bootstrap did not reach the mulligan step: " + Director.StatusText );
			ClearBootstrap();

			return;
		}

		Director.KeepOpeningHandAuthority( localSeat );
		Director.KeepOpeningHandAuthority( botSeat );

		if ( Scene.Camera is CameraComponent camera )
		{
			camera.GameObject.Components.GetOrCreate<CardHover>();
		}

		HasBootstrapped = true;
		Log.Info( $"MTG bootstrap started turn {Director.TurnNumber}: " + $"{localSeat.DisplayName} versus " + $"{botSeat.DisplayName}. Both players have shuffled " + "60-card decks and seven-card opening hands." );
	}


	public void ClearBootstrap()
	{
		if ( _matchObject.IsValid() )
			_matchObject.Destroy();

		_matchObject    = null;
		Director        = null;
		HasBootstrapped = false;
	}


	private async Task<bool> WaitForDatabaseAsync()
	{
		DatabaseManager? manager = Scene.Get<DatabaseManager>();

		if ( manager is not null )
		{
			DatabaseStartupState state = await manager.Completion;

			if ( state == DatabaseStartupState.Ready )
				return true;

			Log.Warning( "MTG bootstrap stopped because the database " + $"entered state '{state}'." );

			return false;
		}

		if ( RuntimeCardDatabase.IsOpen )
			return true;

		Log.Warning( "MTG bootstrap needs a ready DatabaseManager." );

		return false;
	}


	private static Deck BuildDeck( string name, NormalizedCard first, NormalizedCard second ) { return new Deck { Name = name, FormatCode = "bootstrap", Entries = [ new DeckEntry { Section = DeckSections.Main, Quantity = 30, Card = Reference( first ) }, new DeckEntry { Section = DeckSections.Main, Quantity = 30, Card = Reference( second ) } ] }; }


	private static DeckCardReference Reference( NormalizedCard card )
	{
		return new DeckCardReference
			   {
				   ScryfallId      = card.Gameplay.ScryfallId,
				   OracleId        = card.Gameplay.OracleId,
				   Name            = card.Gameplay.Name,
				   SetCode         = card.Set.Code,
				   CollectorNumber = card.Presentation.CollectorNumber
			   };
	}


	private static NormalizedCard RequireCard( string name )
	{
		NormalizedCard[] matches = RuntimeCardDatabase.FindByName( name );

		foreach ( NormalizedCard card in matches )
		{
			if ( string.Equals( card.Source.Language, "en", StringComparison.OrdinalIgnoreCase ) && !card.Presentation.Digital )
				return card;
		}

		if ( matches.Length > 0 )
			return matches[0];

		throw new InvalidOperationException( $"Bootstrap card '{name}' is not in the database." );
	}
}
