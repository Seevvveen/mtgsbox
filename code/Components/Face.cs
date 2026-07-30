#nullable enable

using System;
using System.Threading.Tasks;
using Sandbox.UI;

namespace Sandbox.Components;

/// <summary>
/// Local card-face presentation for a world panel.
/// </summary>
public sealed class Face : PanelComponent
{
	private Image? _cardImage;
	private string? _url;
	private int _textureGeneration;

	public void SetUrl( string url )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( url );

		_url = url;
		BeginTextureLoad();
	}

	/// <summary>
	/// Removes previously known face art when this client may no longer see it.
	/// </summary>
	public void Conceal()
	{
		_textureGeneration++;
		_url = null;

		if ( _cardImage is { IsValid: true } )
			_cardImage.Texture = null;
	}

	protected override void OnTreeFirstBuilt()
	{
		base.OnTreeFirstBuilt();

		if ( Application.IsHeadless )
			return;

		_cardImage = new Image
		{
			Parent = Panel
		};

		BeginTextureLoad();
	}

	protected override int BuildHash()
	{
		return HashCode.Combine( _url );
	}

	private void BeginTextureLoad()
	{
		if ( Application.IsHeadless ||
			_cardImage is not { IsValid: true } ||
			string.IsNullOrWhiteSpace( _url ) )
		{
			return;
		}

		_cardImage.Texture = null;

		string url = _url;
		int generation = ++_textureGeneration;
		_ = LoadTextureAsync( url, generation );
	}

	private async Task LoadTextureAsync(
		string url,
		int generation )
	{
		try
		{
			Texture texture = await Texture.LoadAsync(
				url,
				warnOnMissing: false );

			if ( generation != _textureGeneration ||
				!string.Equals(
					_url,
					url,
					StringComparison.Ordinal ) ||
				_cardImage is not { IsValid: true } )
			{
				return;
			}

			_cardImage.Texture = texture;
		}
		catch ( Exception exception )
		{
			if ( generation == _textureGeneration )
			{
				Log.Warning(
					$"Unable to load card face '{url}': " +
					$"{exception.Message}" );
			}
		}
	}
}
