#nullable enable

using System;
using System.IO;

namespace Sandbox.Classes.Database;

/// <summary>An intact source catalog uses a contract this importer cannot safely normalize.</summary>
public sealed class DatabaseSourceCompatibilityException : Exception
{
	public DatabaseSourceCompatibilityException( string message, Exception innerException ) : base( message, innerException ) { }
}

public sealed class DatabaseGenerationMismatchException : Exception
{
	public DatabaseGenerationMismatchException( string message ) : base( message ) { }
}
