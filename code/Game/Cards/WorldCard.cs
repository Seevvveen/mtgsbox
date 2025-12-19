using System.Threading.Tasks;

public class WorldCard : Component, ICardProvider
{

	public CardIndexSystem IndexSystem { get; private set; }

	private string _privateCardId { get; set; } = "7a977e2d-a2bc-42d1-be7d-36a822c6a66e";

	// Card
	[Property]
	public string CardID
	{
		get { return _privateCardId; }
		set
		{
			_privateCardId = value;
			Card = UpdateCard();
		}
	}




	//"56ebc372-aabd-4174-a943-c7bf59e5028d";
	public Card? Card { get; private set; }

	// Composite


	[RequireComponent] public CardRenderer CardRenderer { get; private set; }
	[RequireComponent] public PlaneCollider PlaneCollider { get; private set; }

	// Ready signal
	private readonly AsyncGate _readyGate = new();
	public Task WhenReady => _readyGate.WhenReady;
	public bool IsReady => _readyGate.IsSucceeded;
	public bool HasFailed => _readyGate.IsFailed;
	public Exception Error => _readyGate.Error;

	protected override async Task OnLoad()
	{
		// Run initialization through the gate for proper state tracking
		await _readyGate.RunAsync( InitializeAsync );
	}

	private async Task InitializeAsync()
	{
		IndexSystem = Scene.GetSystem<CardIndexSystem>();
		await IndexSystem.WhenReady;

		if ( !IndexSystem.IsReady )
		{
			throw new Exception( "CardIndexSystem not ready after WhenReady" );
		}

		Card = IndexSystem.GetCard( CardID );
		if ( Card == null )
		{
			throw new Exception( $"Failed to find card '{CardID}' in index" );
		}

		Log.Info( $"[WorldCard] Loaded card: {Card.Name ?? CardID}" );
	}

	protected Card UpdateCard()
	{
		if ( Card?.Id.ToString() != CardID )
		{
			Card = IndexSystem.GetCard( CardID );
			CardRenderer.SetCard( Card );
			return Card;
		}
		else
		{
			return Card;
		}

	}


	protected override void OnAwake()
	{
		// Component awakening - runs before OnLoad
	}

	protected override void OnStart()
	{
		// Component starting - runs after OnLoad completes
		if ( !IsReady )
		{
			Log.Warning( $"[WorldCard] Started without being ready. Error: {Error?.Message}" );
		}
	}

	protected override void OnDestroy()
	{
		CardRenderer?.Destroy();
		PlaneCollider?.Destroy();
	}

	// ========== CONVENIENCE METHODS ==========

	/// <summary>
	/// Safely get card data only after ready.
	/// </summary>
	public async Task<Card> GetCardAsync()
	{
		await WhenReady;
		if ( !IsReady || Card == null )
		{
			throw Error ?? new Exception( "WorldCard failed to load" );
		}
		return Card;
	}

	/// <summary>
	/// Try to get card without throwing. Returns null if not ready.
	/// </summary>
	public Card? TryGetCard()
	{
		return IsReady ? Card : null;
	}
}
