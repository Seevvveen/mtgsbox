using System;
using Sandbox.BulkCardStuff;

namespace Sandbox.Classes.CardDatabase;



/// <summary>
/// Controls the Lifecycle of the CardDatabase - When its built (when its open?)
/// </summary>
public class CardDatabaseManager : GameObjectSystem<CardDatabaseManager>, ISceneStartup
{
	public CardDatabaseManager(Scene scene) : base(scene)
	{
	}

	async void ISceneStartup.OnHostInitialize()
	{
		await Scryfall.Client.UpdateBulk();
		CardDatabaseBuilder.BuildDatabase();
		
		// HARNEESS
		CardDatabase.Initialize();
		Guid testId = Guid.Parse("a471b306-4941-4e46-a0cb-d92895c16f8a");

		CardDefinition? card = CardDatabase.GetCard( testId );
		
		if ( card is null )
			Log.Error( "Card was not found." );
		else
			Log.Info($"Loaded card: {card.Name} ({card.ScryfallId})");
		
		CardDatabase.Shutdown();
	}
	
}