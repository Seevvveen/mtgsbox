/// <summary>
/// Hold a Card Image and render it to screen
/// </summary>
/// <summary>
/// Renders a Card image in world space using declarative UI
/// </summary>
public sealed class CardRenderer : PanelComponent
{
	[RequireComponent, Hide]
	public Sandbox.WorldPanel WorldPanel { get; set; }

	[Property, RequireComponent, ReadOnly]
	public Card Card { get; set; }

	protected override void BuildRenderTree( RenderTreeBuilder builder )
	{
		builder.OpenElement( 0, "image" );
		builder.AddAttribute( 1, "src", Card?.ImageUris.Normal.ToString() );
		builder.CloseElement();
	}

	protected override int BuildHash()
	{
		return HashCode.Combine( Card?.Id, Card?.ImageUris.Normal );
	}
}
