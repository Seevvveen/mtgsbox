global using Sandbox.Scryfall.Types;
global using System;
global using System.Text.Json.Serialization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Scryfall;


// Never Null
// Data Structures
// Readable

/// <summary>
/// Interact with the scryfall API
/// </summary>
public class ScryfallClient
{
	private const string BaseUrl = "https://api.scryfall.com";
	private readonly SemaphoreSlim _gate = new( 1, 1 );
	private TimeSince _sinceLast;
	/// <summary> Waits 100ms between api calls to limit api spam</summary>
	/// <example>await CheckDelay();</example>
	public async Task ApiDelay()
	{
		await _gate.WaitAsync();
		try
		{
			const float interval = 0.1f;
			if ( _sinceLast < interval )
				await Task.Delay( (int)((interval - _sinceLast) * 1000f) );
			_sinceLast = 0;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary> Foundational Api Request </summary>
	public async Task<T> FetchAsync<T>( string endpoint ) where T : class
	{
		await ApiDelay();
		var response = await Http.RequestJsonAsync<T>( $"{BaseUrl}/{endpoint}" )
			?? throw new InvalidOperationException( $"[Scryfall] Failed to deserialize response from {BaseUrl}/{endpoint}" );
		return response;
	}

	public async Task<T> FetchUrlAsync<T>( string Url ) where T : class
	{
		await ApiDelay();
		var response = await Http.RequestJsonAsync<T>( Url )
			?? throw new InvalidOperationException( $"[Scryfall] Failed to deserialize {Url}" );
		return response;
	}

	/// <summary></summary>
	public Task<Card> GetCardAsync( string id )
		=> FetchAsync<Card>( $"cards/{id}" );

	/// <summary></summary>
	public Task<ApiList<Card>> SearchCardsAsync( string query )
		=> FetchAsync<ApiList<Card>>( $"cards/search?q={Uri.EscapeDataString( query )}" );

	/// <summary></summary>
	public async Task<Stream> FetchStreamAsync( string url )
	{
		await ApiDelay();
		return await Http.RequestStreamAsync( url );
	}
}


