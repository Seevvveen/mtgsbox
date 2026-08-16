#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace Sandbox.Classes.Database;

public sealed class Scryfall
{
	private const string Api             = "https://api.scryfall.com";
	private const string CardMetaFile    = "default-cards-meta.txt";
	private const string RulingsMetaFile = "rulings-meta.txt";

	public static Scryfall Client { get; } = new Scryfall();


	public Task UpdateBulk( CancellationToken cancellationToken = default(CancellationToken), bool force = false )
	{
		return UpdateBulkFile( "default-cards", DatabaseFileInfo.SourceFile, CardMetaFile, cancellationToken, force );
	}


	public Task UpdateRulings( CancellationToken cancellationToken = default(CancellationToken), bool force = false )
	{
		return UpdateBulkFile( "rulings", DatabaseFileInfo.RulingsSourceFile, RulingsMetaFile, cancellationToken, force );
	}


	public Task UpdateSets( CancellationToken cancellationToken = default(CancellationToken) )
	{
		return DownloadApiFile( $"{Api}/sets", DatabaseFileInfo.SetSourceFile, cancellationToken );
	}


	public Task UpdateSymbology( CancellationToken cancellationToken = default(CancellationToken) )
	{
		return DownloadApiFile( $"{Api}/symbology", DatabaseFileInfo.SymbolSourceFile, cancellationToken );
	}


	public DatabaseSourceSnapshot ReadLocalBulkMetadata( string bulkType )
	{
		string metadataFile = bulkType switch
		{
			"default-cards" => CardMetaFile,
			"rulings"       => RulingsMetaFile,
			_               => throw new ArgumentOutOfRangeException( nameof(bulkType), bulkType, "Unknown Scryfall bulk type." )
		};

		return ReadMetadata( metadataFile, bulkType );
	}


	public async Task DownloadDefaultCardsSnapshot( DatabaseSourceSnapshot snapshot, CancellationToken cancellationToken = default(CancellationToken) )
	{
		if ( !string.Equals( snapshot.BulkType, "default-cards", StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Expected a default-cards snapshot, received '{snapshot.BulkType}'." );

		Uri uri = ValidateDownloadUri( snapshot.DownloadUri, snapshot.BulkType );
		byte[] data = await Http.RequestBytesAsync( uri.ToString(), "GET", null, CreateHeaders(), cancellationToken );

		if ( snapshot.CompressedSize > 0 && data.LongLength != snapshot.CompressedSize )
			throw new InvalidDataException( $"Host card source was {data.LongLength} bytes; expected {snapshot.CompressedSize}." );

		string checksum = Convert.ToHexString( SHA256.HashData( data ) );

		if ( !string.IsNullOrWhiteSpace( snapshot.SourceChecksum ) && !string.Equals( checksum, snapshot.SourceChecksum, StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Host card source checksum mismatch. Expected {snapshot.SourceChecksum}; downloaded {checksum}." );

		WriteDataFile( DatabaseFileInfo.SourceFile, data );
		SaveMetadata( CardMetaFile, snapshot with { CompressedSize = data.LongLength, SourceChecksum = checksum } );
	}


	private static async Task UpdateBulkFile( string bulkType, string destinationFile, string metadataFile, CancellationToken cancellationToken, bool force )
	{
		Dictionary<string, string> headers = CreateHeaders();

		BulkDataDto response = await Http.RequestJsonAsync<BulkDataDto>( $"{Api}/bulk-data/{bulkType}", "GET", null, headers, cancellationToken );

		if ( !string.Equals( response.Object, "bulk_data", StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Expected a Scryfall bulk_data object for " + $"'{bulkType}', received '{response.Object}'." );

		if ( !string.Equals( response.Type, bulkType.Replace( "-", "_" ), StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Scryfall returned bulk type '{response.Type}' for " + $"requested type '{bulkType}'." );

		if ( string.IsNullOrWhiteSpace( response.JsonlDownloadUri ) )
			throw new InvalidDataException( $"Scryfall bulk type '{bulkType}' has no download URI." );

		if ( response.UpdatedAt == default(DateTimeOffset) )
			throw new InvalidDataException( $"Scryfall bulk type '{bulkType}' has no update time." );

		if ( response.CompressedSize <= 0 )
			throw new InvalidDataException( $"Scryfall bulk type '{bulkType}' has invalid compressed " + $"size {response.CompressedSize}." );

		Uri downloadUri = ValidateDownloadUri( response.JsonlDownloadUri, bulkType );
		DatabaseSourceSnapshot snapshot = new DatabaseSourceSnapshot
		{
			BulkType = bulkType,
			UpdatedAt = response.UpdatedAt,
			DownloadUri = downloadUri.ToString(),
			CompressedSize = response.CompressedSize
		};

		DateTimeOffset? localUpdated = GetLocalUpdatedAt( metadataFile );

		if ( !force && localUpdated.HasValue && localUpdated.Value >= response.UpdatedAt && FileSystem.Data.FileExists( destinationFile ) && FileSystem.Data.FileSize( destinationFile ) == response.CompressedSize )
		{
			DatabaseSourceSnapshot existing = ReadMetadata( metadataFile, bulkType );
			SaveMetadata( metadataFile, snapshot with { SourceChecksum = existing.SourceChecksum } );
			return;
		}

		byte[] data = await Http.RequestBytesAsync( downloadUri.ToString(), "GET", null, headers, cancellationToken );

		if ( data.LongLength != response.CompressedSize )
			throw new InvalidDataException( $"Downloaded Scryfall bulk type '{bulkType}' was " + $"{data.LongLength} bytes; expected " + $"{response.CompressedSize} bytes." );

		if ( string.Equals( response.ContentEncoding, "gzip", StringComparison.OrdinalIgnoreCase ) && ( data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B ) )
			throw new InvalidDataException( $"Scryfall bulk type '{bulkType}' claims gzip encoding " + "but does not have a gzip header." );

		WriteDataFile( destinationFile, data );
		SaveMetadata( metadataFile, snapshot with { SourceChecksum = Convert.ToHexString( SHA256.HashData( data ) ) } );
	}


	private static async Task DownloadApiFile( string requestUri, string destinationFile, CancellationToken cancellationToken )
	{
		byte[] data = await Http.RequestBytesAsync( requestUri, "GET", null, CreateHeaders(), cancellationToken );

		ValidateListResponse( data, requestUri );
		WriteDataFile( destinationFile, data );
	}


	private static DateTimeOffset? GetLocalUpdatedAt( string metadataFile )
	{
		if ( !FileSystem.Data.FileExists( metadataFile ) )
			return null;

		return ReadMetadata( metadataFile, string.Empty ).UpdatedAt;
	}


	private static DatabaseSourceSnapshot ReadMetadata( string metadataFile, string bulkType )
	{
		if ( !FileSystem.Data.FileExists( metadataFile ) )
			return new DatabaseSourceSnapshot { BulkType = bulkType };

		string text = FileSystem.Data.ReadAllText( metadataFile );

		try
		{
			DatabaseSourceSnapshot? metadata = JsonSerializer.Deserialize<DatabaseSourceSnapshot>( text, DatabaseFileInfo.DatabaseJsonOptions );
			if ( metadata is not null )
				return metadata;
		}
		catch ( JsonException )
		{
			// Versions before v7 stored only the timestamp as plain text.
		}

		return DateTimeOffset.TryParse( text, out DateTimeOffset updatedAt )
			? new DatabaseSourceSnapshot { BulkType = bulkType, UpdatedAt = updatedAt }
			: new DatabaseSourceSnapshot { BulkType = bulkType };
	}


	private static void SaveMetadata( string metadataFile, DatabaseSourceSnapshot metadata )
	{
		FileSystem.Data.WriteAllText( metadataFile, JsonSerializer.Serialize( metadata, DatabaseFileInfo.DatabaseJsonOptions ) );
	}


	private static void WriteDataFile( string destinationFile, byte[] data )
	{
		if ( data.Length == 0 )
			throw new InvalidDataException( $"Refusing to replace '{destinationFile}' with an " + "empty download." );

		using Stream fileStream = FileSystem.Data.OpenWrite( destinationFile );

		fileStream.Write( data, 0, data.Length );
		fileStream.Flush();
	}


	private static Uri ValidateDownloadUri( string value, string bulkType )
	{
		if ( !Uri.TryCreate( value, UriKind.Absolute, out Uri? uri ) || !string.Equals( uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase ) || !IsScryfallHost( uri.Host ) )
			throw new InvalidDataException( $"Scryfall bulk type '{bulkType}' returned an " + $"untrusted download URI '{value}'." );

		return uri;
	}


	private static bool IsScryfallHost( string host )
	{
		return string.Equals( host, "scryfall.com", StringComparison.OrdinalIgnoreCase ) || host.EndsWith( ".scryfall.com", StringComparison.OrdinalIgnoreCase ) || string.Equals( host, "scryfall.io", StringComparison.OrdinalIgnoreCase ) || host.EndsWith( ".scryfall.io", StringComparison.OrdinalIgnoreCase );
	}


	private static void ValidateListResponse( byte[] data, string requestUri )
	{
		if ( data.Length == 0 )
			throw new InvalidDataException( $"Scryfall endpoint '{requestUri}' returned no data." );

		try
		{
			using JsonDocument document = JsonDocument.Parse( data );
			JsonElement        root     = document.RootElement;

			if ( root.ValueKind != JsonValueKind.Object || !root.TryGetProperty( "object", out JsonElement objectKind ) || !string.Equals( objectKind.GetString(), "list", StringComparison.Ordinal ) || !root.TryGetProperty( "data", out JsonElement listData ) || listData.ValueKind != JsonValueKind.Array )
				throw new InvalidDataException( $"Scryfall endpoint '{requestUri}' did not return " + "a list response." );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( $"Scryfall endpoint '{requestUri}' returned invalid JSON.", exception );
		}
	}


	private static Dictionary<string, string> CreateHeaders()
	{
		return new Dictionary<string, string>
			   {
				   ["Accept"] = "application/json"

				   //["User-Agent"] = "mtgsbox/1.0" - Not Allowed to set user agent in sbox
			   };
	}


	private sealed class BulkDataDto
	{
		[JsonPropertyName( "object" )] public string Object { get; set; } = "";

		[JsonPropertyName( "id" )] public string Id { get; set; } = "";

		[JsonPropertyName( "type" )] public string Type { get; set; } = "";

		[JsonPropertyName( "updated_at" )] public DateTimeOffset UpdatedAt { get; set; }

		[JsonPropertyName( "name" )] public string Name { get; set; } = "";

		[JsonPropertyName( "description" )] public string Description { get; set; } = "";

		[JsonPropertyName( "compressed_size" )] public long CompressedSize { get; set; }

		[JsonPropertyName( "jsonl_download_uri" )] public string JsonlDownloadUri { get; set; } = "";

		[JsonPropertyName( "content_type" )] public string? ContentType { get; set; }

		[JsonPropertyName( "content_encoding" )] public string? ContentEncoding { get; set; }
	}
}
