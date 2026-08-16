#nullable enable

using Sandbox.Classes.Cards;

namespace Sandbox.Classes.Zones;

public sealed class HandZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Hand;
	public override ZoneLayout DefaultLayout => ZoneLayout.Fan;
	public override MtgZoneCardState DefaultCardState => MtgZoneCardState.OwnerOnly;
}

public sealed class BattlefieldZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Battlefield;
	public override ZoneLayout DefaultLayout => ZoneLayout.Freeform;
	public override Vector2 DefaultSize => new( CardMesh.Width * 10f, CardMesh.Height * 5f );
}

public sealed class GraveyardZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Graveyard;
	protected override bool EnforcePhysicalStackSpacing => true;
}

public sealed class ExileZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Exile;
	protected override bool EnforcePhysicalStackSpacing => true;
}

public sealed class CommandZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Command;


	public override bool CanAccept( CardObject card )
	{
		return base.CanAccept( card ) && card.IsDeclaredCommander && card.OwnerPlayerId == OwnerPlayerId;
	}
}

public sealed class StackZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Stack;
	public CardObject? Top => Cards.Count > 0? Cards[^1] : null;
}

public sealed class SideboardZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Sideboard;
	public override ZoneLayout DefaultLayout => ZoneLayout.Freeform;
	public override MtgZoneCardState DefaultCardState => MtgZoneCardState.OwnerOnly;
	public override Vector2 DefaultSize => new( CardMesh.Width * 2f, CardMesh.Height * 1.2f );
}

public sealed class CustomZone : ZoneObject
{
	public override ZoneType Type => ZoneType.Custom;
	public override MtgZoneCardState DefaultCardState => MtgZoneCardState.Preserve;
}
