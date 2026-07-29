using System;
using Sandbox.Classes.Database;
using Sandbox.Classes.Database.Types;

namespace Sandbox.Classes.CardDatabase;



/// <summary>
/// Controls the Lifecycle of the CardDatabase - When its built (when its open?)
/// </summary>
public class DatabaseManager : GameObjectSystem<DatabaseManager>, ISceneStartup
{
	public DatabaseManager(Scene scene) : base(scene)
	{
	}

	async void ISceneStartup.OnHostInitialize()
	{
		await Scryfall.Client.UpdateBulk();
		DatabaseBuilder.BuildDatabase();
		
		
		
		
		// HARNEESS
		Database.CardDatabase.Initialize();
		Guid testId = Guid.Parse("a471b306-4941-4e46-a0cb-d92895c16f8a");

		NormalizedCard card = Database.CardDatabase.GetCard( testId );
		
		if ( card is null )
			Log.Error( "Card was not found." );
		else
			Log.Info($"Loaded card: {card.Gameplay.Name} ({card.Gameplay.ScryfallId})");
		
		Database.CardDatabase.Shutdown();
	}
	
}