#nullable enable

using Sandbox.Classes.DeckValidation;
using System;
namespace Sandbox.Classes;

/// <summary>
///     Minimal two-player rules used only by <see cref = "MtgTestBootstrap"/>.
///     It exercises the real director/deck/zone pipeline without pretending to
///     implement a complete Magic format.
/// </summary>
public sealed class MtgBootstrapRulesEngine : RulesEngine
{
	public override DeckFormatDefinition CreateFormat( string formatCode )
	{
		DeckFormatDefinition format = DeckFormatDefinition.Constructed( formatCode, "Bootstrap Test" );

		return format with { DefaultCopyLimit = 60, CardLegalityCode = "vintage" };
	}


	public override void SetupMatch( IReadOnlyList<PlayerSeat> players )
	{
		for ( int index = 0; index < players.Count; index++ )
		{
			PlayerSeat player = players[index];
			Deck       deck   = Director.GetSubmittedDeck( player.PlayerId ) ?? throw new InvalidOperationException( $"{player.DisplayName} has no submitted deck." );
			CreateBoard( player, deck, index, players.Count );
		}
	}


	private void CreateBoard( PlayerSeat player, Deck deck, int index, int playerCount )
	{
		float    side     = index % 2 == 0? -1f : 1f;
		float    rowY     = side * CardMesh.Height * 1.65f;
		Rotation rotation = side < 0f? Director.WorldRotation : Rotation.FromYaw( 180f ) * Director.WorldRotation;

		Transform At( float x, float y ) { return new Transform( Director.WorldPosition + Director.WorldRotation * new Vector3( x, y, 0f ), rotation ); }

		ZoneObject library   = CreatePlayerZone( player, ZoneType.Library, At( -CardMesh.Width * 1.35f, rowY ) );
		ZoneObject graveyard = CreatePlayerZone( player, ZoneType.Graveyard, At( 0f, rowY ) );
		CreatePlayerZone( player, ZoneType.Exile, At( CardMesh.Width * 1.35f, rowY ) );
		CreatePlayerZone( player, ZoneType.Battlefield, At( 0f, rowY - side * CardMesh.Height * 0.75f ), ZoneLayout.Freeform );
		ZoneObject hand = CreatePlayerZone( player, ZoneType.Hand, At( 0f, rowY + side * CardMesh.Height * 0.65f ), ZoneLayout.Fan );

		PopulateZoneFromDeck( player, deck, library, DeckSections.Main, MtgZoneCardState.Concealed );
		library.Shuffle();

		for ( int cardIndex = 0; cardIndex < 7; cardIndex++ )
		{
			CardObject? card = library.DrawTop();

			if ( card is not null )
				hand.AddCard( card, MtgZoneCardState.OwnerOnly, animate: false );
		}
	}
}
