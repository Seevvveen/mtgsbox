using System.Threading.Tasks;

public class WorldCard : Component, ICardProvider
{
	//Card
	[Property] string CardID { get; set; } = "56ebc372-aabd-4174-a943-c7bf59e5028d";
	public Card Card { get; set; }

	//Composite
	public CardIndexSystem IndexSystem { get; set; }
	[RequireComponent] public CardRenderer CardRenderer { get; private set; }
	[RequireComponent] public PlaneCollider PlaneCollider { get; private set; }

	// Ready signal
	private TaskCompletionSource<bool> _readySignal = new();
	public Task WhenReady => _readySignal.Task;
	public bool IsReady { get; private set; }

	protected override async Task OnLoad()
	{
		IndexSystem = Scene.GetSystem<CardIndexSystem>();

		await IndexSystem.WhenReady;

		Card = IndexSystem.GetCard( CardID );

		if ( Card == null )
		{
			Log.Error( "[WorldCard] Failed to find card in index" );
			IsReady = false;
			_readySignal.TrySetResult( false );
			return;
		}

		//Log.Info( "[WorldCard] Ready" );
		IsReady = true;
		_readySignal.TrySetResult( true );
	}

	protected override void OnAwake()
	{
	}


	protected override void OnStart()
	{
	}


	protected override void OnDestroy()
	{
		CardRenderer?.Destroy();
		PlaneCollider?.Destroy();
	}

}
