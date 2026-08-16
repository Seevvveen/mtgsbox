#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;

namespace Sandbox.Classes.Database;

/// <summary>
///     Produces the stable identity exchanged by a host and its clients. The
///     format version is included so an identical payload cannot be mistaken
///     for a compatible database after the file contract changes.
/// </summary>
static class CardDatabaseChecksum
{
	public static string Compute()
	{
		DatabaseGenerationManifest? manifest = DatabaseGenerationStore.TryGetActiveManifest();

		if ( manifest is not null && !string.IsNullOrWhiteSpace( manifest.CardDataChecksum ) )
		{
			string actual = DatabaseGenerationStore.ComputeFileChecksum( manifest.Artifacts.CardData );

			if ( !string.Equals( actual, manifest.CardDataChecksum, StringComparison.Ordinal ) )
				throw new InvalidDataException( $"Database generation {manifest.GenerationId} failed its card-data integrity check." );

			return $"v{manifest.FormatVersion}:{actual}";
		}

		DatabaseArtifactPaths paths = DatabaseGenerationStore.ResolveActivePaths();

		if ( !FileSystem.Data.FileExists( paths.CardData ) )
			throw new FileNotFoundException( $"Card data file '{paths.CardData}' does not exist.", paths.CardData );

		using Stream input = FileSystem.Data.OpenRead( paths.CardData );
		byte[] hash = SHA256.HashData( input );

		return $"v{DatabaseFileInfo.CurrentFormatVersion}:{Convert.ToHexString( hash )}";
	}
}
