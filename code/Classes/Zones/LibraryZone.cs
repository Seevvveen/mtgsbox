#nullable enable

using Sandbox.Classes.Cards;
using System;

namespace Sandbox.Classes.Zones;

public sealed class LibraryZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Library;
	public override MtgZoneCardState DefaultCardState => MtgZoneCardState.Concealed;
	protected override bool EnforcePhysicalStackSpacing => true;
	

	public CardObject? TakeTop()
	{
		RequireAuthority();

		if ( CardsInternal.Count == 0 )
			return null;

		CardObject card = CardsInternal[^1];
		Remove( card );

		return card;
	}


	public IReadOnlyList<CardObject> TakeTop( int count )
	{
		if ( count < 0 )
			throw new ArgumentOutOfRangeException( nameof(count) );

		RequireAuthority();
		int amount = Math.Min( count, CardsInternal.Count );
		List<CardObject> cards = new( amount );

		for ( int index = 0; index < amount; index++ )
		{
			CardObject card = CardsInternal[^1];
			Remove( card, reflow: false );
			cards.Add( card );
		}

		Reflow();

		return cards;
	}


	public IReadOnlyList<CardObject> PeekTop( int count )
	{
		if ( count < 0 )
			throw new ArgumentOutOfRangeException( nameof(count) );

		int amount = Math.Min( count, CardsInternal.Count );
		List<CardObject> cards = new( amount );

		for ( int offset = 0; offset < amount; offset++ )
			cards.Add( CardsInternal[CardsInternal.Count - 1 - offset] );

		return cards;
	}


	public void PutOnTop( CardObject card, bool animate = true )
	{
		Add( card, index: CardsInternal.Count, animate: animate );
	}


	public void PutOnBottom( CardObject card, bool animate = true )
	{
		Add( card, index: 0, animate: animate );
	}


	public void MoveToTop( CardObject card, bool animate = true )
	{
		ArgumentNullException.ThrowIfNull( card );
		int index = CardsInternal.IndexOf( card );

		if ( index < 0 )
			throw new InvalidOperationException( "The card is not in this library." );

		Move( index, CardsInternal.Count - 1, animate );
	}


	public void MoveToBottom( CardObject card, bool animate = true )
	{
		ArgumentNullException.ThrowIfNull( card );
		int index = CardsInternal.IndexOf( card );

		if ( index < 0 )
			throw new InvalidOperationException( "The card is not in this library." );

		Move( index, 0, animate );
	}


	public void Shuffle()
	{
		RequireAuthority();

		for ( int index = CardsInternal.Count - 1; index > 0; index-- )
		{
			int other = Game.Random.Next( index + 1 );
			(CardsInternal[index], CardsInternal[other]) = (CardsInternal[other], CardsInternal[index]);
		}

		foreach ( CardObject card in CardsInternal )
			card.Conceal();

		Reflow();
	}
}
