#nullable enable

using Sandbox.Classes.CardDatabase;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Classes.Database;

public sealed class Scryfall
{
	private const string Api = "https://api.scryfall.com";
	private const string CardMetaFile = "default-cards-meta.txt";
	private const string RulingsMetaFile = "rulings-meta.txt";

	public static Scryfall Client { get; } = new();

	private sealed class BulkDataDto
	{
		[JsonPropertyName( "object" )]
		public string Object { get; set; } = "";

		[JsonPropertyName( "id" )]
		public string Id { get; set; } = "";

		[JsonPropertyName( "type" )]
		public string Type { get; set; } = "";

		[JsonPropertyName( "updated_at" )]
		public DateTimeOffset UpdatedAt { get; set; }

		[JsonPropertyName( "name" )]
		public string Name { get; set; } = "";

		[JsonPropertyName( "description" )]
		public string Description { get; set; } = "";

		[JsonPropertyName( "compressed_size" )]
		public long CompressedSize { get; set; }

		[JsonPropertyName( "jsonl_download_uri" )]
		public string JsonlDownloadUri { get; set; } = "";

		[JsonPropertyName( "content_type" )]
		public string? ContentType { get; set; }

		[JsonPropertyName( "content_encoding" )]
		public string? ContentEncoding { get; set; }
	}

	public Task UpdateBulk(
		CancellationToken cancellationToken = default,
		bool force = false )
	{
		return UpdateBulkFile(
			"default-cards",
			DatabaseFileInfo.SourceFile,
			CardMetaFile,
			cancellationToken,
			force );
	}

	public Task UpdateRulings(
		CancellationToken cancellationToken = default,
		bool force = false )
	{
		return UpdateBulkFile(
			"rulings",
			DatabaseFileInfo.RulingsSourceFile,
			RulingsMetaFile,
			cancellationToken,
			force );
	}

	public Task UpdateSets(
		CancellationToken cancellationToken = default )
	{
		return DownloadApiFile(
			$"{Api}/sets",
			DatabaseFileInfo.SetSourceFile,
			cancellationToken );
	}

	public Task UpdateSymbology(
		CancellationToken cancellationToken = default )
	{
		return DownloadApiFile(
			$"{Api}/symbology",
			DatabaseFileInfo.SymbolSourceFile,
			cancellationToken );
	}

	private static async Task UpdateBulkFile(
		string bulkType,
		string destinationFile,
		string metadataFile,
		CancellationToken cancellationToken,
		bool force )
	{
		Dictionary<string, string> headers = CreateHeaders();

		BulkDataDto response =
			await Http.RequestJsonAsync<BulkDataDto>(
				requestUri: $"{Api}/bulk-data/{bulkType}",
				method: "GET",
				content: null,
				headers: headers,
				cancellationToken: cancellationToken );

		if ( !string.Equals(
			response.Object,
			"bulk_data",
			StringComparison.Ordinal ) )
		{
			throw new InvalidDataException(
				$"Expected a Scryfall bulk_data object for " +
				$"'{bulkType}', received '{response.Object}'." );
		}

		if ( !string.Equals(
			response.Type,
			bulkType.Replace( "-", "_" ),
			StringComparison.Ordinal ) )
		{
			throw new InvalidDataException(
				$"Scryfall returned bulk type '{response.Type}' for " +
				$"requested type '{bulkType}'." );
		}

		if ( string.IsNullOrWhiteSpace(
			response.JsonlDownloadUri ) )
		{
			throw new InvalidDataException(
				$"Scryfall bulk type '{bulkType}' has no download URI." );
		}

		if ( response.UpdatedAt == default )
		{
			throw new InvalidDataException(
				$"Scryfall bulk type '{bulkType}' has no update time." );
		}

		if ( response.CompressedSize <= 0 )
		{
			throw new InvalidDataException(
				$"Scryfall bulk type '{bulkType}' has invalid compressed " +
				$"size {response.CompressedSize}." );
		}

		Uri downloadUri =
			ValidateDownloadUri( response.JsonlDownloadUri, bulkType );

		DateTimeOffset? localUpdated =
			GetLocalUpdatedAt( metadataFile );

		if (
			!force &&
			localUpdated.HasValue &&
			localUpdated.Value >= response.UpdatedAt &&
			FileSystem.Data.FileExists( destinationFile ) &&
			FileSystem.Data.FileSize( destinationFile ) ==
				response.CompressedSize
		)
		{
			return;
		}

		byte[] data = await Http.RequestBytesAsync(
			requestUri: downloadUri.ToString(),
			method: "GET",
			content: null,
			headers: headers,
			cancellationToken: cancellationToken );

		if ( data.LongLength != response.CompressedSize )
		{
			throw new InvalidDataException(
				$"Downloaded Scryfall bulk type '{bulkType}' was " +
				$"{data.LongLength} bytes; expected " +
				$"{response.CompressedSize} bytes." );
		}

		if ( string.Equals(
			response.ContentEncoding,
			"gzip",
			StringComparison.OrdinalIgnoreCase ) &&
			(data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B) )
		{
			throw new InvalidDataException(
				$"Scryfall bulk type '{bulkType}' claims gzip encoding " +
				"but does not have a gzip header." );
		}

		WriteDataFile( destinationFile, data );
		SaveLocalUpdatedAt( metadataFile, response.UpdatedAt );
	}

	private static async Task DownloadApiFile(
		string requestUri,
		string destinationFile,
		CancellationToken cancellationToken )
	{
		byte[] data = await Http.RequestBytesAsync(
			requestUri: requestUri,
			method: "GET",
			content: null,
			headers: CreateHeaders(),
			cancellationToken: cancellationToken );

		ValidateListResponse( data, requestUri );
		WriteDataFile( destinationFile, data );
	}

	private static DateTimeOffset? GetLocalUpdatedAt(
		string metadataFile )
	{
		if ( !FileSystem.Data.FileExists( metadataFile ) )
			return null;

		string text = FileSystem.Data.ReadAllText( metadataFile );

		return DateTimeOffset.TryParse(
			text,
			out DateTimeOffset result )
			? result
			: null;
	}

	private static void SaveLocalUpdatedAt(
		string metadataFile,
		DateTimeOffset time )
	{
		FileSystem.Data.WriteAllText(
			metadataFile,
			time.ToString( "O" ) );
	}

	private static void WriteDataFile(
		string destinationFile,
		byte[] data )
	{
		if ( data.Length == 0 )
		{
			throw new InvalidDataException(
				$"Refusing to replace '{destinationFile}' with an " +
				"empty download." );
		}

		using Stream fileStream = FileSystem.Data.OpenWrite(
			destinationFile,
			FileMode.Create );

		fileStream.Write( data, 0, data.Length );
		fileStream.Flush();
	}

	private static Uri ValidateDownloadUri(
		string value,
		string bulkType )
	{
		if ( !Uri.TryCreate( value, UriKind.Absolute, out Uri? uri ) ||
			!string.Equals(
				uri.Scheme,
				Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase ) ||
			!IsScryfallHost( uri.Host ) )
		{
			throw new InvalidDataException(
				$"Scryfall bulk type '{bulkType}' returned an " +
				$"untrusted download URI '{value}'." );
		}

		return uri;
	}

	private static bool IsScryfallHost( string host )
	{
		return string.Equals(
				host,
				"scryfall.com",
				StringComparison.OrdinalIgnoreCase ) ||
			host.EndsWith(
				".scryfall.com",
				StringComparison.OrdinalIgnoreCase ) ||
			string.Equals(
				host,
				"scryfall.io",
				StringComparison.OrdinalIgnoreCase ) ||
			host.EndsWith(
				".scryfall.io",
				StringComparison.OrdinalIgnoreCase );
	}

	private static void ValidateListResponse(
		byte[] data,
		string requestUri )
	{
		if ( data.Length == 0 )
		{
			throw new InvalidDataException(
				$"Scryfall endpoint '{requestUri}' returned no data." );
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse( data );
			JsonElement root = document.RootElement;

			if ( root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty( "object", out JsonElement objectKind ) ||
				!string.Equals(
					objectKind.GetString(),
					"list",
					StringComparison.Ordinal ) ||
				!root.TryGetProperty( "data", out JsonElement listData ) ||
				listData.ValueKind != JsonValueKind.Array )
			{
				throw new InvalidDataException(
					$"Scryfall endpoint '{requestUri}' did not return " +
						"a list response." );
			}
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException(
				$"Scryfall endpoint '{requestUri}' returned invalid JSON.",
				exception );
		}
	}

	private static Dictionary<string, string> CreateHeaders()
	{
		return new Dictionary<string, string>
		{
			["Accept"] = "application/json",
			//["User-Agent"] = "mtgsbox/1.0" - Not Allowed to set user agent in sbox
		};
	}
}
