#nullable enable

using Sandbox.Classes.CardDatabase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Classes.Database;

public sealed class Scryfall
{
	private const string Api = "https://api.scryfall.com";
	private const string CardMetaFile = "oracle-meta.txt";
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

	public Task UpdateBulk()
	{
		return UpdateBulkFile(
			"oracle-cards",
			DatabaseFileInfo.SourceFile,
			CardMetaFile );
	}

	public Task UpdateRulings()
	{
		return UpdateBulkFile(
			"rulings",
			DatabaseFileInfo.RulingsSourceFile,
			RulingsMetaFile );
	}

	public Task UpdateSets()
	{
		return DownloadApiFile(
			$"{Api}/sets",
			DatabaseFileInfo.SetSourceFile );
	}

	public Task UpdateSymbology()
	{
		return DownloadApiFile(
			$"{Api}/symbology",
			DatabaseFileInfo.SymbolSourceFile );
	}

	private static async Task UpdateBulkFile(
		string bulkType,
		string destinationFile,
		string metadataFile )
	{
		Dictionary<string, string> headers = CreateHeaders();

		BulkDataDto response =
			await Http.RequestJsonAsync<BulkDataDto>(
				requestUri: $"{Api}/bulk-data/{bulkType}",
				method: "GET",
				content: null,
				headers: headers,
				cancellationToken: CancellationToken.None );

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

		DateTimeOffset? localUpdated =
			GetLocalUpdatedAt( metadataFile );

		if (
			localUpdated.HasValue &&
			localUpdated.Value >= response.UpdatedAt &&
			FileSystem.Data.FileExists( destinationFile )
		)
		{
			return;
		}

		byte[] data = await Http.RequestBytesAsync(
			requestUri: response.JsonlDownloadUri,
			method: "GET",
			content: null,
			headers: headers,
			cancellationToken: CancellationToken.None );

		WriteDataFile( destinationFile, data );
		SaveLocalUpdatedAt( metadataFile, response.UpdatedAt );
	}

	private static async Task DownloadApiFile(
		string requestUri,
		string destinationFile )
	{
		byte[] data = await Http.RequestBytesAsync(
			requestUri: requestUri,
			method: "GET",
			content: null,
			headers: CreateHeaders(),
			cancellationToken: CancellationToken.None );

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
		using Stream fileStream = FileSystem.Data.OpenWrite(
			destinationFile,
			FileMode.Create );

		fileStream.Write( data, 0, data.Length );
		fileStream.Flush();
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
