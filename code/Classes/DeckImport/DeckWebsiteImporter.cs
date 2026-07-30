#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Classes.DeckImport;

/// <summary>
/// Imports public deck URLs supported by the reference TTS importer.
/// Moxfield and Archidekt use their JSON endpoints; legacy sites are
/// transformed to their text or CSV export endpoints.
/// </summary>
public sealed class DeckWebsiteImporter
{
	private const string UserAgent = "mtgsbox-deck-importer/1.0";
	private readonly DeckTextImporter _textImporter;

	public DeckWebsiteImporter( IDeckCardResolver resolver )
	{
		_textImporter = new DeckTextImporter( resolver );
	}

	public async Task<DeckImportResult> ImportAsync(
		string url,
		DeckImportOptions? options = null,
		CancellationToken cancellationToken = default )
	{
		options ??= new DeckImportOptions();

		if ( !Uri.TryCreate( url, UriKind.Absolute, out Uri? uri ) ||
			!string.Equals(
				uri.Scheme,
				Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase ) )
		{
			return Failure(
				options,
				url,
				DeckImportIssueCode.InvalidUrl,
				"Deck URL must be a valid HTTPS URL." );
		}

		try
		{
			string host = uri.Host.ToLowerInvariant();

			if ( IsHost( host, "moxfield.com" ) )
			{
				return await ImportMoxfieldAsync(
					uri,
					options,
					cancellationToken );
			}

			if ( IsHost( host, "archidekt.com" ) )
			{
				return await ImportArchidektAsync(
					uri,
					options,
					cancellationToken );
			}

			if ( IsHost( host, "scryfall.com" ) )
			{
				return Failure(
					options,
					url,
					DeckImportIssueCode.UnsupportedWebsite,
					"Scryfall deck pages are not supported. Use a pasted " +
					"list, Moxfield, or Archidekt." );
			}

			LegacyDeckRequest? request = CreateLegacyRequest( uri );
			if ( request is null )
			{
				return Failure(
					options,
					url,
					DeckImportIssueCode.UnsupportedWebsite,
					$"Deck website '{uri.Host}' is not supported." );
			}

			string payload = await RequestStringAsync(
				request.Url,
				cancellationToken );

			string deckText = request.Kind switch
			{
				LegacyPayloadKind.Csv => ConvertCsvToDeckText( payload ),
				LegacyPayloadKind.DeckboxHtml =>
					ConvertDeckboxHtmlToText( payload ),
				_ => payload
			};

			return _textImporter.Import(
				deckText,
				WithSource(
					options,
					url,
					request.Site,
					GetLastPathSegment( uri ) ) );
		}
		catch ( Exception exception )
		{
			return Failure(
				options,
				url,
				DeckImportIssueCode.RequestFailed,
				$"Deck request failed: {exception.Message}" );
		}
	}

	private async Task<DeckImportResult> ImportMoxfieldAsync(
		Uri uri,
		DeckImportOptions options,
		CancellationToken cancellationToken )
	{
		Match match = Regex.Match(
			uri.AbsolutePath,
			@"/decks/(?<id>[^/?]+)",
			RegexOptions.IgnoreCase );

		if ( !match.Success )
		{
			return Failure(
				options,
				uri.ToString(),
				DeckImportIssueCode.InvalidUrl,
				"Invalid Moxfield deck URL." );
		}

		string deckId = match.Groups["id"].Value;
		string endpoint =
			$"https://api2.moxfield.com/v2/decks/all/{deckId}/";
		string json = await RequestStringAsync(
			endpoint,
			cancellationToken,
			referer: "https://www.moxfield.com/" );

		using JsonDocument document = JsonDocument.Parse( json );
		JsonElement root = document.RootElement;
		var text = new StringBuilder();

		AppendMoxfieldBoard(
			root,
			"commanders",
			DeckSections.Commander,
			text );
		AppendMoxfieldBoard(
			root,
			"companions",
			DeckSections.Companion,
			text );
		AppendMoxfieldBoard(
			root,
			"mainboard",
			DeckSections.Main,
			text );
		AppendMoxfieldBoard(
			root,
			"sideboard",
			DeckSections.Sideboard,
			text );
		AppendMoxfieldBoard(
			root,
			"maybeboard",
			DeckSections.Maybeboard,
			text );

		string deckName = GetString( root, "name" )
			?? options.DeckName;

		return _textImporter.Import(
			text.ToString(),
			WithSource(
				options with { DeckName = deckName },
				uri.ToString(),
				"moxfield",
				deckId ) );
	}

	private async Task<DeckImportResult> ImportArchidektAsync(
		Uri uri,
		DeckImportOptions options,
		CancellationToken cancellationToken )
	{
		Match match = Regex.Match(
			uri.AbsolutePath,
			@"/decks/(?<id>\d+)",
			RegexOptions.IgnoreCase );

		if ( !match.Success )
		{
			return Failure(
				options,
				uri.ToString(),
				DeckImportIssueCode.InvalidUrl,
				"Invalid Archidekt deck URL." );
		}

		string deckId = match.Groups["id"].Value;
		string endpoint =
			$"https://archidekt.com/api/decks/{deckId}/cards/";
		string json = await RequestStringAsync(
			endpoint,
			cancellationToken );

		using JsonDocument document = JsonDocument.Parse( json );
		JsonElement root = document.RootElement;
		JsonElement rows = root;

		if ( root.ValueKind == JsonValueKind.Object &&
			root.TryGetProperty( "cards", out JsonElement cards ) )
		{
			rows = cards;
		}

		if ( rows.ValueKind != JsonValueKind.Array )
		{
			throw new JsonException(
				"Archidekt response did not contain a card array." );
		}

		var bySection = new Dictionary<string, StringBuilder>(
			StringComparer.OrdinalIgnoreCase );

		foreach ( JsonElement row in rows.EnumerateArray() )
		{
			if ( !row.TryGetProperty( "card", out JsonElement card ) ||
				card.ValueKind != JsonValueKind.Object )
			{
				continue;
			}

			int quantity = GetInt32( row, "quantity" ) ?? 1;
			string section = GetArchidektSection( row );
			string? uuid = GetString( card, "uid" );
			string name = GetArchidektCardName( card ) ?? "Card";
			string identity = uuid is null ? name : $"{name} {uuid}";

			if ( !bySection.TryGetValue(
				section,
				out StringBuilder? sectionText ) )
			{
				sectionText = new StringBuilder();
				bySection.Add( section, sectionText );
			}

			sectionText.AppendLine( $"{quantity} {identity}" );
		}

		var text = new StringBuilder();
		foreach ( string section in SectionOrder )
		{
			if ( !bySection.TryGetValue(
				section,
				out StringBuilder? sectionText ) )
			{
				continue;
			}

			text.AppendLine( SectionHeading( section ) );
			text.Append( sectionText );
		}

		return _textImporter.Import(
			text.ToString(),
			WithSource(
				options,
				uri.ToString(),
				"archidekt",
				deckId ) );
	}

	private static void AppendMoxfieldBoard(
		JsonElement root,
		string property,
		string section,
		StringBuilder output )
	{
		if ( !root.TryGetProperty( property, out JsonElement board ) ||
			board.ValueKind != JsonValueKind.Object )
		{
			return;
		}

		output.AppendLine( SectionHeading( section ) );

		foreach ( JsonProperty propertyEntry in board.EnumerateObject() )
		{
			JsonElement entry = propertyEntry.Value;
			int quantity = GetInt32( entry, "quantity" ) ?? 1;

			if ( !entry.TryGetProperty(
				"card",
				out JsonElement card ) )
			{
				continue;
			}

			string? uuid = GetString( card, "scryfall_id" );
			string name = GetString( card, "name" )
				?? propertyEntry.Name;
			string identity = uuid is null ? name : $"{name} {uuid}";
			output.AppendLine( $"{quantity} {identity}" );
		}
	}

	private static readonly string[] SectionOrder =
	[
		DeckSections.Commander,
		DeckSections.Companion,
		DeckSections.Main,
		DeckSections.Sideboard,
		DeckSections.Maybeboard
	];

	private static string SectionHeading( string section )
	{
		return section switch
		{
			DeckSections.Commander => "Commander:",
			DeckSections.Companion => "Companion:",
			DeckSections.Sideboard => "Sideboard:",
			DeckSections.Maybeboard => "Maybeboard:",
			_ => "Mainboard:"
		};
	}

	private static string GetArchidektSection( JsonElement row )
	{
		if ( !row.TryGetProperty(
			"categories",
			out JsonElement categories ) ||
			categories.ValueKind != JsonValueKind.Array )
		{
			return DeckSections.Main;
		}

		foreach ( JsonElement category in categories.EnumerateArray() )
		{
			string? name = category.ValueKind switch
			{
				JsonValueKind.String => category.GetString(),
				JsonValueKind.Object => GetString( category, "name" ),
				_ => null
			};

			if ( name is null )
				continue;

			string normalized = name.Trim().ToLowerInvariant();
			if ( normalized.Contains( "commander" ) )
				return DeckSections.Commander;
			if ( normalized.Contains( "companion" ) )
				return DeckSections.Companion;
			if ( normalized.Contains( "sideboard" ) )
				return DeckSections.Sideboard;
			if ( normalized.Contains( "maybeboard" ) )
				return DeckSections.Maybeboard;
		}

		return DeckSections.Main;
	}

	private static string? GetArchidektCardName( JsonElement card )
	{
		if ( card.TryGetProperty(
			"oracleCard",
			out JsonElement oracleCard ) )
		{
			string? name = GetString( oracleCard, "name" );
			if ( name is not null )
				return name;
		}

		return GetString( card, "displayName" )
			?? GetString( card, "name" );
	}

	private static LegacyDeckRequest? CreateLegacyRequest( Uri uri )
	{
		string host = uri.Host.ToLowerInvariant();
		string url = uri.ToString();

		if ( IsHost( host, "deckstats.net" ) )
		{
			string baseUrl = Regex.Replace( url, @"\?cb=\d+.*$", "" );
			return new LegacyDeckRequest(
				AppendQuery( baseUrl, "include_comments=1&export_txt=1" ),
				"deckstats",
				LegacyPayloadKind.Text );
		}

		if ( IsHost( host, "pastebin.com" ) )
		{
			string id = GetLastPathSegment( uri );
			return new LegacyDeckRequest(
				$"https://pastebin.com/raw/{id}",
				"pastebin",
				LegacyPayloadKind.Text );
		}

		if ( IsHost( host, "mtgdecks.net" ) )
		{
			return new LegacyDeckRequest(
				url.TrimEnd( '/' ) + "/dec",
				"mtgdecks",
				LegacyPayloadKind.Text );
		}

		if ( IsHost( host, "deckbox.org" ) )
		{
			return new LegacyDeckRequest(
				url.TrimEnd( '/' ) + "/export",
				"deckbox",
				LegacyPayloadKind.DeckboxHtml );
		}

		if ( IsHost( host, "tappedout.net" ) )
		{
			return new LegacyDeckRequest(
				AppendQuery(
					Regex.Replace( url, @".cb=\d+", "" ),
					"fmt=csv" ),
				"tappedout",
				LegacyPayloadKind.Csv );
		}

		if ( IsHost( host, "mtggoldfish.com" ) &&
			uri.AbsolutePath.Contains(
				"/deck/",
				StringComparison.OrdinalIgnoreCase ) )
		{
			string download = Regex.Replace(
				url,
				@"/deck/",
				"/deck/download/",
				RegexOptions.IgnoreCase );
			int fragment = download.IndexOf( '#' );
			if ( fragment >= 0 )
				download = download[..fragment];

			return new LegacyDeckRequest(
				download,
				"mtggoldfish",
				LegacyPayloadKind.Text );
		}

		if ( IsHost( host, "cubecobra.com" ) )
		{
			string download = Regex.Replace(
				url,
				@"cube/deck",
				"cube/deck/download/mtgo",
				RegexOptions.IgnoreCase );
			return new LegacyDeckRequest(
				download,
				"cubecobra",
				LegacyPayloadKind.Text );
		}

		return null;
	}

	private static string ConvertDeckboxHtmlToText( string html )
	{
		Match body = Regex.Match(
			html,
			@"<body[^>]*>(?<body>.*?)</body>",
			RegexOptions.IgnoreCase | RegexOptions.Singleline );
		string text = body.Success ? body.Groups["body"].Value : html;
		return Regex.Replace(
			text,
			@"<br\s*/?>",
			"\n",
			RegexOptions.IgnoreCase );
	}

	private static string ConvertCsvToDeckText( string csv )
	{
		string[] lines = csv.Replace( "\r\n", "\n" ).Split( '\n' );
		if ( lines.Length == 0 )
			return "";

		List<string> headers = ParseCsvRow( lines[0] );
		int quantityIndex = FindHeader( headers, "qty", "quantity", "count" );
		int nameIndex = FindHeader( headers, "name", "card" );
		int boardIndex = FindHeader( headers, "board", "category" );
		int setIndex = FindHeader(
			headers,
			"set",
			"printing",
			"edition" );
		int collectorIndex = FindHeader(
			headers,
			"collector number",
			"collector_number",
			"collector" );

		if ( quantityIndex < 0 || nameIndex < 0 )
			throw new FormatException( "Deck CSV has no quantity/name columns." );

		var sections = new Dictionary<string, StringBuilder>(
			StringComparer.OrdinalIgnoreCase );

		for ( int index = 1; index < lines.Length; index++ )
		{
			if ( string.IsNullOrWhiteSpace( lines[index] ) )
				continue;

			List<string> row = ParseCsvRow( lines[index] );
			if ( quantityIndex >= row.Count || nameIndex >= row.Count )
				continue;

			string section = boardIndex >= 0 && boardIndex < row.Count
				? NormalizeCsvSection( row[boardIndex] )
				: DeckSections.Main;
			string identity = row[nameIndex].Trim();

			if ( setIndex >= 0 && setIndex < row.Count &&
				!string.IsNullOrWhiteSpace( row[setIndex] ) )
			{
				identity += $" ({row[setIndex].Trim().ToUpperInvariant()})";

				if ( collectorIndex >= 0 &&
					collectorIndex < row.Count &&
					!string.IsNullOrWhiteSpace( row[collectorIndex] ) )
				{
					identity += $" {row[collectorIndex].Trim()}";
				}
			}

			if ( !sections.TryGetValue(
				section,
				out StringBuilder? sectionRows ) )
			{
				sectionRows = new StringBuilder();
				sections.Add( section, sectionRows );
			}

			sectionRows.AppendLine(
				$"{row[quantityIndex].Trim()} {identity}" );
		}

		var output = new StringBuilder();
		foreach ( string section in SectionOrder )
		{
			if ( sections.TryGetValue(
				section,
				out StringBuilder? rows ) )
			{
				output.AppendLine( SectionHeading( section ) );
				output.Append( rows );
			}
		}

		return output.ToString();
	}

	private static List<string> ParseCsvRow( string line )
	{
		var result = new List<string>();
		var field = new StringBuilder();
		bool quoted = false;

		for ( int index = 0; index < line.Length; index++ )
		{
			char character = line[index];

			if ( character == '"' )
			{
				if ( quoted &&
					index + 1 < line.Length &&
					line[index + 1] == '"' )
				{
					field.Append( '"' );
					index++;
				}
				else
				{
					quoted = !quoted;
				}
			}
			else if ( character == ',' && !quoted )
			{
				result.Add( field.ToString() );
				field.Clear();
			}
			else
			{
				field.Append( character );
			}
		}

		result.Add( field.ToString() );
		return result;
	}

	private static int FindHeader(
		List<string> headers,
		params string[] names )
	{
		for ( int index = 0; index < headers.Count; index++ )
		{
			foreach ( string name in names )
			{
				if ( string.Equals(
					headers[index].Trim(),
					name,
					StringComparison.OrdinalIgnoreCase ) )
				{
					return index;
				}
			}
		}

		return -1;
	}

	private static string NormalizeCsvSection( string value )
	{
		string normalized = value.Trim().ToLowerInvariant();

		if ( normalized.Contains( "commander" ) )
			return DeckSections.Commander;
		if ( normalized.Contains( "companion" ) )
			return DeckSections.Companion;
		if ( normalized.Contains( "side" ) )
			return DeckSections.Sideboard;
		if ( normalized.Contains( "maybe" ) )
			return DeckSections.Maybeboard;
		return DeckSections.Main;
	}

	private static async Task<string> RequestStringAsync(
		string url,
		CancellationToken cancellationToken,
		string? referer = null )
	{
		var headers = new Dictionary<string, string>
		{
			["Accept"] = "application/json, text/plain, text/csv, */*"
		};

		if ( referer is not null )
			headers["Referer"] = referer;

		return await Http.RequestStringAsync(
			requestUri: url,
			method: "GET",
			content: null,
			headers: headers,
			cancellationToken: cancellationToken );
	}

	private static DeckImportOptions WithSource(
		DeckImportOptions options,
		string url,
		string site,
		string? externalId )
	{
		return options with
		{
			Source = new DeckSource
			{
				Kind = "website",
				Site = site,
				ExternalId = externalId,
				Url = url
			}
		};
	}

	private static DeckImportResult Failure(
		DeckImportOptions options,
		string? url,
		DeckImportIssueCode code,
		string message )
	{
		return new DeckImportResult
		{
			Deck = new Deck
			{
				Name = options.DeckName,
				FormatCode = options.FormatCode,
				Source = new DeckSource
				{
					Kind = "website",
					Url = url
				}
			},
			Issues =
			[
				new DeckImportIssue
				{
					Severity = DeckImportIssueSeverity.Error,
					Code = code,
					Message = message
				}
			]
		};
	}

	private static bool IsHost( string actual, string expected )
	{
		return string.Equals(
				actual,
				expected,
				StringComparison.OrdinalIgnoreCase ) ||
			actual.EndsWith(
				$".{expected}",
				StringComparison.OrdinalIgnoreCase );
	}

	private static string AppendQuery( string url, string query )
	{
		return url + (url.Contains( '?' ) ? "&" : "?") + query;
	}

	private static string GetLastPathSegment( Uri uri )
	{
		return uri.AbsolutePath.TrimEnd( '/' )
			[(uri.AbsolutePath.TrimEnd( '/' ).LastIndexOf( '/' ) + 1)..];
	}

	private static string? GetString(
		JsonElement element,
		string property )
	{
		return element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty( property, out JsonElement value ) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	private static int? GetInt32(
		JsonElement element,
		string property )
	{
		if ( element.ValueKind != JsonValueKind.Object ||
			!element.TryGetProperty( property, out JsonElement value ) )
		{
			return null;
		}

		if ( value.TryGetInt32( out int result ) )
			return result;

		return value.ValueKind == JsonValueKind.String &&
			int.TryParse( value.GetString(), out result )
				? result
				: null;
	}

	private sealed record LegacyDeckRequest(
		string Url,
		string Site,
		LegacyPayloadKind Kind );

	private enum LegacyPayloadKind
	{
		Text,
		Csv,
		DeckboxHtml
	}
}
