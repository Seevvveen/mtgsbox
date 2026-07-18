using System;
using System.IO;

namespace Sandbox.Classes.CardDatabase;

//
// Take DTO's captured from scryfall and Return a Normalized Card
//
public static class ScryfallCardNormalizer
{
	public static CardDefinition Normalize( ScryfallCardDto dto )
	{
		ArgumentNullException.ThrowIfNull( dto );

		Guid scryfallId = ParseRequiredGuid(dto.ScryfallId, "Scryfall ID");
		Guid? oracleId = ParseOptionalGuid(dto.OracleId, "Oracle ID");
		string name = NormalizeRequiredText(dto.Name, $"Card '{scryfallId}' has no name.");

		return new CardDefinition
		{
			ScryfallId = scryfallId,
			OracleId = oracleId,
			Name = name
		};
	}


	private static Guid ParseRequiredGuid(string? value, string label)
	{
		if (!Guid.TryParse(value, out Guid result)) {
			throw new InvalidDataException($"Invalid {label}:  {value}");
		}
		return result;
	}

	private static Guid? ParseOptionalGuid(string? value, string fieldName ) {
		if ( string.IsNullOrWhiteSpace( value ) )
			return null;

		if ( !Guid.TryParse( value, out Guid result ) ) {
			throw new InvalidDataException($"Invalid {fieldName}: '{value}'");
		}

		return result;
	}
	
	private static string NormalizeRequiredText(string? value, string errorMessage ) {
		if ( string.IsNullOrWhiteSpace( value ) )
			throw new InvalidDataException( errorMessage );

		return value.Trim();
	}
	
	
}