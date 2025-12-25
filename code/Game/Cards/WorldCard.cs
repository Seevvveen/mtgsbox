using System.Threading.Tasks;

/// <summary>
/// Manages card data and coordinates child components.
/// Waits for CardIndexSystem once, then operates synchronously.
/// </summary>
public class WorldCard : Component, Component.ExecuteInEditor
{
	[Property, RequireComponent]
	public CardRenderer CardRenderer { get; private set; }

	[Property, RequireComponent]
	public PlaneCollider PlaneCollider { get; private set; }

	[Property, Change( nameof( OnIDChanged ) )]
	public string ID { get; set; }

	[Property, ReadOnly]
	public Card Card { get; private set; }

	private CardIndexSystem IndexSystem => Scene.GetSystem<CardIndexSystem>();

	public const float CardHeight = 512f;
	public const float CardAspectRatio = 63f / 88f;
	public static readonly float CardWidth = CardHeight * CardAspectRatio;
	public static readonly Vector2 CardSize = new( CardWidth, CardHeight );



	protected override void OnAwake()
	{
		PlaneCollider.Normal = Vector3.Forward;
		PlaneCollider.Scale = CardSize * Sandbox.UI.WorldPanel.ScreenToWorldScale;
		CardRenderer.WorldPanel.PanelSize = CardSize;
	}

	protected override async Task OnLoad()
	{
		// Wait once for system to be ready
		await IndexSystem.WhenReady;

		// Now all operations are synchronous
		if ( !string.IsNullOrEmpty( ID ) )
			UpdateCard( ID );
		else
			SetRandomCard();
	}

	private void OnIDChanged( string oldValue, string newValue )
	{
		// Only update if system is ready (avoid calls during initialization)
		if ( IndexSystem.IsReady )
			UpdateCard( newValue );
	}

	private void UpdateCard( string cardId )
	{
		Card = IndexSystem.GetCard( cardId ); // Throws if invalid
		CardRenderer.Card = Card;
	}

	[Button( "Random Card" )]
	public void SetRandomCard()
	{
		Card = IndexSystem.GetRandomCard();
		ID = Card.Id.ToString();
		CardRenderer.Card = Card;
	}
}
