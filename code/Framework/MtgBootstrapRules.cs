#nullable enable

using Sandbox.Classes.DeckValidation;
using System;
using System.Collections.Generic;

namespace Sandbox.Classes;

/// <summary>
/// Minimal two-player rules used only by <see cref="MtgTestBootstrap"/>.
/// It exercises the real director/deck/zone pipeline without pretending to
/// implement a complete Magic format.
/// </summary>
public sealed class MtgBootstrapRules : MtgGameRules
{
	public override DeckFormatDefinition CreateFormat(
		string formatCode )
	{
		DeckFormatDefinition format =
			DeckFormatDefinition.Constructed(
				formatCode,
				"Bootstrap Test",
				60,
				15 );
		return format with
		{
			DefaultCopyLimit = 60,
			CardLegalityCode = "vintage"
		};
	}

	public override void SetupMatch(
		IReadOnlyList<MtgPlayerSeat> players )
	{
		for ( int index = 0;
			index < players.Count;
			index++ )
		{
			MtgPlayerSeat player = players[index];
			Deck deck = Director.GetSubmittedDeck(
				player.PlayerId )
				?? throw new InvalidOperationException(
					$"{player.DisplayName} has no submitted deck." );
			CreateBoard( player, deck, index, players.Count );
		}
	}

	private void CreateBoard(
		MtgPlayerSeat player,
		Deck deck,
		int index,
		int playerCount )
	{
		float side = index % 2 == 0 ? -1f : 1f;
		float rowY = side * CardMesh.Height * 1.65f;
		Rotation rotation = side < 0f
			? Director.WorldRotation
			: Rotation.FromYaw( 180f ) *
				Director.WorldRotation;

		Transform At( float x, float y ) => new(
			Director.WorldPosition +
				Director.WorldRotation *
					new Vector3( x, y, 0f ),
			rotation );

		ZoneObject library = CreatePlayerZone(
			player,
			MtgZoneKind.Library,
			At( -CardMesh.Width * 1.35f, rowY ) );
		ZoneObject graveyard = CreatePlayerZone(
			player,
			MtgZoneKind.Graveyard,
			At( 0f, rowY ) );
		CreatePlayerZone(
			player,
			MtgZoneKind.Exile,
			At( CardMesh.Width * 1.35f, rowY ) );
		CreatePlayerZone(
			player,
			MtgZoneKind.Battlefield,
			At( 0f, rowY - side *
				CardMesh.Height * 0.75f ),
			MtgZoneLayout.Freeform );
		ZoneObject hand = CreatePlayerZone(
			player,
			MtgZoneKind.Hand,
			At( 0f, rowY + side *
				CardMesh.Height * 0.65f ),
			MtgZoneLayout.Fan );

		PopulateZoneFromDeck(
			player,
			deck,
			library,
			DeckSections.Main,
			MtgZoneCardState.Concealed );
		library.Shuffle();

		for ( int cardIndex = 0;
			cardIndex < 7;
			cardIndex++ )
		{
			CardObject? card = library.DrawTop();

			if ( card is not null )
			{
				hand.AddCard(
					card,
					MtgZoneCardState.OwnerOnly,
					animate: false );
			}
		}
	}
}
