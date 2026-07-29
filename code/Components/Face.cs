using Sandbox.UI;

namespace Sandbox.Components;

// Component Placed on WorldPanel GO
public class Face : PanelComponent
{
	private Image CardImg = new();
	private string _url;

	public void SetUrl(string url)
	{
		_url = url;
		CardImg.SetTexture(_url);
	}
	
	protected override void OnTreeFirstBuilt()
	{
		base.OnTreeFirstBuilt();
		CardImg.SetTexture(_url);
		CardImg.Parent = Panel;
	}
	
	protected override int BuildHash() => System.HashCode.Combine( _url );
}