using System;
using System.IO;
using System.Text.Json;


//
// AI'd because i cannot be asked
//
public static class JsonArrayReader
{
	private enum ReaderState
	{
		BeforeArray,

		// Accepts either the first object or an empty-array closing bracket.
		ValueOrEnd,

		// Used after a comma. Does not accept ']', preventing trailing commas.
		Value,

		CommaOrEnd,
		Done
	}

	/// <summary>
	/// Reads a top-level JSON array containing objects.
	/// Only one complete object is retained in memory at a time.
	/// </summary>
	public static void ReadObjectBytes(Stream stream, Action<ReadOnlyMemory<byte>, int> onObject, int readBufferSize = 64 * 1024 )
	{
		if ( stream is null )
			throw new ArgumentNullException( nameof(stream) );

		if ( onObject is null )
			throw new ArgumentNullException( nameof(onObject) );

		if ( !stream.CanRead )
			throw new ArgumentException("The supplied stream must be readable.", nameof(stream));

		if ( readBufferSize <= 0 )
			throw new ArgumentOutOfRangeException(nameof(readBufferSize), "Buffer size must be greater than zero.");
		
		byte[] readBuffer = new byte[readBufferSize];

		// Contains only the JSON object currently being assembled.
		using MemoryStream objectBuffer = new( 32 * 1024 );

		ReaderState state = ReaderState.BeforeArray;

		bool readingObject = false;
		bool insideString = false;
		bool escaped = false;

		int structureDepth = 0;
		int objectIndex = 0;

		// Process one byte through the parser state machine.
		void ProcessByte( byte current )
		{
			if ( readingObject )
			{
				objectBuffer.WriteByte( current );

				if ( insideString )
				{
					if ( escaped )
						escaped = false;
					
					else if ( current == (byte)'\\' )
						escaped = true;
					
					else if ( current == (byte)'"' )
						insideString = false;

					return;
				}

				switch ( current )
				{
					case (byte)'"':
						insideString = true;
						break;

					case (byte)'{':
					case (byte)'[':
						structureDepth++;
						break;

					case (byte)'}':
					case (byte)']':
						structureDepth--;
						break;
				}

				if ( structureDepth < 0 )
					throw new JsonException($"Invalid JSON structure near array index {objectIndex}.");

				if ( structureDepth == 0 )
				{
					byte[] objectJson = objectBuffer.ToArray();

					// The callback is executed before moving to the next object.
					onObject( objectJson, objectIndex );

					objectIndex++;
					readingObject = false;
					state = ReaderState.CommaOrEnd;

					objectBuffer.SetLength( 0 );
					objectBuffer.Position = 0;
				}

				return;
			}

			if ( IsWhitespace( current ) )
				return;

			switch ( state )
			{
				case ReaderState.BeforeArray:
				{
					if ( current != (byte)'[' )
						throw new JsonException("Expected a top-level JSON array.");

					state = ReaderState.ValueOrEnd;
					break;
				}

				case ReaderState.ValueOrEnd:
				{
					if ( current == (byte)']' )
					{
						state = ReaderState.Done;
						break;
					}
					StartObject(current, objectBuffer, ref readingObject, ref insideString, ref escaped, ref structureDepth, objectIndex);
					break;
				}

				case ReaderState.Value:
				{
					// Unlike ValueOrEnd, this state deliberately rejects ']'.
					// This prevents trailing commas such as [{},].
					StartObject(current, objectBuffer, ref readingObject, ref insideString, ref escaped, ref structureDepth, objectIndex);
					break;
				}

				case ReaderState.CommaOrEnd:
				{
					if ( current == (byte)',' )
						state = ReaderState.Value;
					
					else if ( current == (byte)']' )
						state = ReaderState.Done;
					
					else
					{
						int previousObjectIndex = objectIndex - 1;
						throw new JsonException($"Expected ',' or ']' after array index " + $"{previousObjectIndex}.");
					}

					break;
				}

				case ReaderState.Done:
					throw new JsonException("Unexpected non-whitespace data after the JSON array.");
			}
		}

		/*
		 * Read the first three bytes separately so a UTF-8 BOM can be
		 * checked as the exact EF BB BF sequence.
		 *
		 * If the prefix is not a BOM, the bytes are passed into the parser
		 * normally rather than being discarded.
		 */
		byte[] prefix = new byte[3];
		int prefixLength = ReadUpTo( stream, prefix, prefix.Length );

		int prefixStart = HasUtf8Bom( prefix, prefixLength ) ? 3 : 0;

		for ( int i = prefixStart; i < prefixLength; i++ )
			ProcessByte( prefix[i] );

		while ( true )
		{
			int bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);

			if ( bytesRead == 0 )
				break;

			for ( int i = 0; i < bytesRead; i++ )
				ProcessByte( readBuffer[i] );
		}

		if ( readingObject )
			throw new JsonException($"JSON ended before the object at array index " + $"{objectIndex} was complete.");

		if ( state != ReaderState.Done )
			throw new JsonException("JSON ended before the top-level array was completed.");
	}

	/// <summary>
	/// Reads and deserializes each object in a top-level JSON array.
	/// </summary>
	public static void ReadObjects<T>(Stream stream, Action<T, int> onObject, JsonSerializerOptions options = null, int readBufferSize = 64 * 1024 )
	{
		if ( onObject is null )
			throw new ArgumentNullException( nameof(onObject) );

		ReadObjectBytes(
			stream,
			(jsonBytes, index) =>
			{
				T value = JsonSerializer.Deserialize<T>(jsonBytes.Span, options);
				
				if ( value is null )
					throw new JsonException($"The JSON object at array index {index} " + "deserialized to null.");

				onObject( value, index );
			},
			readBufferSize
		);
	}

	private static void StartObject(byte current, MemoryStream objectBuffer, ref bool readingObject, ref bool insideString, ref bool escaped, ref int structureDepth, int  objectIndex )
	{
		if ( current != (byte)'{' )
			throw new JsonException($"Expected a JSON object at array index {objectIndex}.");

		objectBuffer.SetLength( 0 );
		objectBuffer.Position = 0;
		objectBuffer.WriteByte( current );

		readingObject = true;
		insideString = false;
		escaped = false;
		structureDepth = 1;
	}

	private static bool HasUtf8Bom(byte[] prefix, int    prefixLength )
	{
		return prefixLength >= 3
			&& prefix[0] == 0xEF
			&& prefix[1] == 0xBB
			&& prefix[2] == 0xBF;
	}

	/// <summary>
	/// Attempts to fill the requested buffer section, stopping at EOF.
	/// Handles streams that return fewer bytes than requested.
	/// </summary>
	private static int ReadUpTo(Stream stream, byte[] buffer, int    requestedCount )
	{
		int totalRead = 0;

		while ( totalRead < requestedCount )
		{
			int bytesRead = stream.Read(buffer, totalRead, requestedCount - totalRead);

			if ( bytesRead == 0 )
				break;

			totalRead += bytesRead;
		}

		return totalRead;
	}

	private static bool IsWhitespace( byte value )
	{
		return value is
			(byte)' ' or
			(byte)'\t' or
			(byte)'\r' or
			(byte)'\n';
	}
}