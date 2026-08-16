#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Classes.Database;

public sealed record DatabaseArtifactPaths
{
	public required string GenerationId { get; init; }
	public required string CardData { get; init; }
	public required string CardIndex { get; init; }
	public required string Symbols { get; init; }
	public required string Sets { get; init; }
	public required string Rulings { get; init; }
	public required string Manifest { get; init; }
	public bool IsLegacy { get; init; }
}

public sealed record DatabaseSourceSnapshot
{
	public string BulkType { get; init; } = "default-cards";
	public DateTimeOffset? UpdatedAt { get; init; }
	public string DownloadUri { get; init; } = string.Empty;
	public long CompressedSize { get; init; }
	public string SourceChecksum { get; init; } = string.Empty;
}

public sealed record DatabaseGenerationManifest
{
	public int FormatVersion { get; init; }
	public required string GenerationId { get; init; }
	public DateTimeOffset CreatedAt { get; init; }
	public required DatabaseArtifactPaths Artifacts { get; init; }
	public required DatabaseSourceSnapshot CardSource { get; init; }
	public Dictionary<string, string> ArtifactChecksums { get; init; } = new Dictionary<string, string>( StringComparer.Ordinal );
	public Dictionary<string, string[]> UnknownSourceValues { get; init; } = new Dictionary<string, string[]>( StringComparer.Ordinal );

	public string CardDataChecksum => ArtifactChecksums.TryGetValue( nameof(DatabaseArtifactPaths.CardData), out string? value )? value : string.Empty;
}

/// <summary>
///     Stores immutable database generations. A manifest is written only after
///     every artifact has completed, so an interrupted build cannot replace the
///     last usable generation.
/// </summary>
static class DatabaseGenerationStore
{
	private const string GenerationRoot = "card-database/generations";
	private const string ManifestName = "manifest.json";

	public static DatabaseArtifactPaths CreateGenerationPaths()
	{
		string id = Guid.NewGuid().ToString( "N" );
		string root = $"{GenerationRoot}/{id}";

		FileSystem.Data.CreateDirectory( root );

		return new DatabaseArtifactPaths
		{
			GenerationId = id,
			CardData = $"{root}/cards.dat",
			CardIndex = $"{root}/index.json",
			Symbols = $"{root}/symbols.json",
			Sets = $"{root}/sets.json",
			Rulings = $"{root}/rulings.json",
			Manifest = $"{root}/{ManifestName}"
		};
	}

	public static DatabaseGenerationManifest CompleteGeneration( DatabaseArtifactPaths paths, Dictionary<string, string[]> unknownValues )
	{
		DatabaseSourceSnapshot source = Scryfall.Client.ReadLocalBulkMetadata( "default-cards" ) with
		{
			CompressedSize = FileSystem.Data.FileExists( DatabaseFileInfo.SourceFile )? FileSystem.Data.FileSize( DatabaseFileInfo.SourceFile ) : 0,
			SourceChecksum = FileSystem.Data.FileExists( DatabaseFileInfo.SourceFile )? ComputeFileChecksum( DatabaseFileInfo.SourceFile ) : string.Empty
		};

		Dictionary<string, string> checksums = new Dictionary<string, string>( StringComparer.Ordinal )
		{
			[nameof(DatabaseArtifactPaths.CardData)] = ComputeFileChecksum( paths.CardData ),
			[nameof(DatabaseArtifactPaths.CardIndex)] = ComputeFileChecksum( paths.CardIndex ),
			[nameof(DatabaseArtifactPaths.Symbols)] = ComputeFileChecksum( paths.Symbols ),
			[nameof(DatabaseArtifactPaths.Sets)] = ComputeFileChecksum( paths.Sets ),
			[nameof(DatabaseArtifactPaths.Rulings)] = ComputeFileChecksum( paths.Rulings )
		};

		DatabaseGenerationManifest manifest = new DatabaseGenerationManifest
		{
			FormatVersion = DatabaseFileInfo.CurrentFormatVersion,
			GenerationId = paths.GenerationId,
			CreatedAt = DateTimeOffset.UtcNow,
			Artifacts = paths,
			CardSource = source,
			ArtifactChecksums = checksums,
			UnknownSourceValues = unknownValues
		};

		FileSystem.Data.WriteAllText( paths.Manifest, JsonSerializer.Serialize( manifest, DatabaseFileInfo.DatabaseJsonOptions ) );
		return manifest;
	}

	public static DatabaseArtifactPaths ResolveActivePaths()
	{
		return TryGetActiveManifest()?.Artifacts ?? LegacyPaths();
	}

	public static DatabaseGenerationManifest? TryGetActiveManifest()
	{
		DatabaseGenerationManifest? newest = null;

		if ( !FileSystem.Data.DirectoryExists( GenerationRoot ) )
			return null;

		foreach ( string foundPath in FileSystem.Data.FindFile( GenerationRoot, ManifestName, true ) )
		{
			// BaseFileSystem.FindFile returns paths relative to the directory passed
			// as its first argument, not necessarily relative to FileSystem.Data.
			string path = foundPath.StartsWith( GenerationRoot + "/", StringComparison.OrdinalIgnoreCase )
				? foundPath
				: $"{GenerationRoot}/{foundPath.TrimStart( '/', '\\' )}";

			try
			{
				string? json = FileSystem.Data.ReadAllText( path );

				if ( string.IsNullOrWhiteSpace( json ) )
				{
					Log.Warning( $"Ignoring empty database generation manifest '{path}'." );
					continue;
				}

				DatabaseGenerationManifest? candidate = JsonSerializer.Deserialize<DatabaseGenerationManifest>( json, DatabaseFileInfo.DatabaseJsonOptions );

				if ( candidate is null || candidate.FormatVersion != DatabaseFileInfo.CurrentFormatVersion || candidate.Artifacts is null || !string.Equals( candidate.GenerationId, candidate.Artifacts.GenerationId, StringComparison.Ordinal ) || !ArtifactsExist( candidate.Artifacts ) )
					continue;

				if ( newest is null || candidate.CreatedAt > newest.CreatedAt )
					newest = candidate;
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Ignoring incomplete database generation manifest '{path}': {exception.Message}" );
			}
		}

		return newest;
	}

	public static string ComputeFileChecksum( string path )
	{
		using Stream input = FileSystem.Data.OpenRead( path );
		return Convert.ToHexString( SHA256.HashData( input ) );
	}

	private static bool ArtifactsExist( DatabaseArtifactPaths paths )
	{
		return FileSystem.Data.FileExists( paths.CardData ) &&
		       FileSystem.Data.FileExists( paths.CardIndex ) &&
		       FileSystem.Data.FileExists( paths.Symbols ) &&
		       FileSystem.Data.FileExists( paths.Sets ) &&
		       FileSystem.Data.FileExists( paths.Rulings );
	}

	private static DatabaseArtifactPaths LegacyPaths()
	{
		return new DatabaseArtifactPaths
		{
			GenerationId = "legacy",
			CardData = DatabaseFileInfo.CardDataFile,
			CardIndex = DatabaseFileInfo.CardIndexFile,
			Symbols = DatabaseFileInfo.SymbolDefinitionsFile,
			Sets = DatabaseFileInfo.SetDefinitionsFile,
			Rulings = DatabaseFileInfo.RulingsFile,
			Manifest = string.Empty,
			IsLegacy = true
		};
	}
}
