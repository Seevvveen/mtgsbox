#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Zones;
using System;

namespace Sandbox.Framework.Rules;

/// <summary>
///     An untrusted request to change authoritative match state. Intents contain
///     what a participant wants to do, never permission to perform it.
/// </summary>
public abstract record GameIntent( Guid ActorPlayerId );

public sealed record SelectCardIntent( Guid ActorPlayerId, CardObject? Card ) : GameIntent( ActorPlayerId );
public sealed record GrabCardIntent( Guid ActorPlayerId, CardObject Card ) : GameIntent( ActorPlayerId );
public sealed record ReleaseCardIntent( Guid ActorPlayerId, CardObject Card ) : GameIntent( ActorPlayerId );
public sealed record MoveCardIntent( Guid ActorPlayerId, CardObject Card, ZoneObject Destination, Transform FreeformPose ) : GameIntent( ActorPlayerId );
public sealed record ThrowCardIntent( Guid ActorPlayerId, CardObject Card, Vector3 Velocity, Vector3 AngularVelocity ) : GameIntent( ActorPlayerId );
public sealed record FlipCardIntent( Guid ActorPlayerId, CardObject Card ) : GameIntent( ActorPlayerId );
public sealed record TapCardIntent( Guid ActorPlayerId, CardObject Card, bool Tapped ) : GameIntent( ActorPlayerId );
public sealed record PassPriorityIntent( Guid ActorPlayerId ) : GameIntent( ActorPlayerId );
public sealed record EndTurnIntent( Guid ActorPlayerId ) : GameIntent( ActorPlayerId );
public sealed record ConcedeIntent( Guid ActorPlayerId ) : GameIntent( ActorPlayerId );

/// <summary>
///     Trusted operations produced only after every rule module accepts an
///     intent. Commands are applied by the host-side executor.
/// </summary>
public abstract record GameCommand;

public sealed record NoOpCommand( string Reason ) : GameCommand;
public sealed record SelectCardCommand( Seat Actor, CardObject? Card ) : GameCommand;
public sealed record GrabCardCommand( Guid ActorPlayerId, CardObject Card ) : GameCommand;
public sealed record ReleaseCardCommand( Guid ActorPlayerId, CardObject Card ) : GameCommand;
public sealed record MoveCardCommand( CardObject Card, ZoneObject Destination, Transform FreeformPose ) : GameCommand;
public sealed record ThrowCardCommand( CardObject Card, Vector3 Velocity, Vector3 AngularVelocity ) : GameCommand;
public sealed record FlipCardCommand( CardObject Card ) : GameCommand;
public sealed record TapCardCommand( CardObject Card, bool Tapped ) : GameCommand;
public sealed record PassPriorityCommand( Seat Actor ) : GameCommand;
public sealed record EndTurnCommand( Seat Actor ) : GameCommand;
public sealed record ConcedeCommand( Seat Actor ) : GameCommand;
