#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Deck.Validation;
using Sandbox.Classes.Zones;
using Sandbox.Framework.GameInfo;
using System;
namespace Sandbox.Framework.Rules;

internal static class CardMovePolicy
{
	public static RuleEvaluation EvaluateCommandZoneEntry( bool commandZone, bool declaredCommander )
	{
		return commandZone && !declaredCommander
			? RuleEvaluation.Deny( "zone.commander_only", "Only a declared commander can enter the command zone." )
			: RuleEvaluation.Abstain();
	}
}

internal static class FlowPermissionPolicy
{
	public static RuleEvaluation Evaluate( GameIntent intent, Guid actorPlayerId, MatchFlowSnapshot flow )
	{
		if ( intent is GrabCardIntent or MoveCardIntent or ThrowCardIntent or FlipCardIntent or TapCardIntent )
		{
			if ( flow.PriorityPlayerId != actorPlayerId )
				return RuleEvaluation.Deny( "priority.not_holder", "You cannot take that action while another participant has priority." );
		}

		if ( intent is PassPriorityIntent )
			return flow.PriorityPlayerId == actorPlayerId? RuleEvaluation.Allow() : RuleEvaluation.Deny( "priority.not_holder", "You do not currently have priority." );

		if ( intent is EndTurnIntent )
			return flow.ActivePlayerId == actorPlayerId? RuleEvaluation.Allow() : RuleEvaluation.Deny( "turn.not_active_player", "Only the active participant can end the turn." );

		return intent is ConcedeIntent? RuleEvaluation.Allow() : RuleEvaluation.Abstain();
	}
}

/// <summary>
///     Facade for the authoritative rules session attached to one match. It
///     composes small rule modules and policies; it does not own networking,
///     turn state, or UI state.
/// </summary>
public sealed class RulesEngine : Component
{
	private readonly List<IGameRuleModule>       _modules               = [ ];
	private readonly List<IGameRuleModule>       _invariants            = [ ];
	private readonly List<IDeckFormatRule>       _additionalDeckRules   = [ ];
	private readonly List<IGameCommandProvider>  _commandProviders      = [ ];
	private readonly List<IGameCommandHandler>   _commandHandlers       = [ ];
	private readonly List<IReplacementRule>      _replacementRules      = [ ];
	private readonly List<IStateBasedActionRule> _stateBasedActionRules = [ ];
	private readonly List<ITriggerRule>          _triggerRules          = [ ];
	private readonly List<IMatchLifecycleRule>   _lifecycleRules        = [ ];

	private Match?             _match;
	private MTGFormat?         _format;
	private IDeckRuleProvider? _deckRules;

	public ITurnStructurePolicy        TurnPolicy           { get; private set; } = new StandardTurnPolicy();
	public IPriorityPolicy             PriorityPolicy       { get; private set; } = new StandardPriorityPolicy();
	public IReadOnlyList<IOutcomeRule> OutcomeRules         { get; private set; } = [ ];
	public string                      CompositionSignature { get; private set; } = "core@1";


	public void Initialize( Match match, MTGFormat format )
	{
		ArgumentNullException.ThrowIfNull( match );
		ArgumentNullException.ThrowIfNull( format );

		if ( ReferenceEquals( _match, match ) && ReferenceEquals( _format, format ) && _modules.Count > 0 )
			return;

		_match  = match;
		_format = format;
		_modules.Clear();
		_invariants.Clear();
		_additionalDeckRules.Clear();
		_commandProviders.Clear();
		_commandHandlers.Clear();
		_replacementRules.Clear();
		_stateBasedActionRules.Clear();
		_triggerRules.Clear();
		_lifecycleRules.Clear();

		_invariants.Add( new MatchStateRule() );
		_invariants.Add( new ActorEligibilityRule() );
		_modules.Add( new CardInteractionRule() );
		_modules.Add( new FlowPermissionRule() );

		RulesModule[] customModules = match.GameObject.GetComponentsInChildren<RulesModule>().OrderBy( module => module.Order ).ToArray();
		ValidateModuleComposition( customModules, format );
		CompositionSignature = string.Join( ";", new[] { "core@1" }.Concat( customModules.OrderBy( module => module.EffectiveModuleId, StringComparer.OrdinalIgnoreCase ).Select( module => $"{module.EffectiveModuleId}@{module.ModuleVersion}" ) ) );

		foreach ( RulesModule module in customModules )
			_modules.Add( module );

		_commandProviders.AddRange( customModules.OfType<IGameCommandProvider>() );
		_commandHandlers.AddRange( customModules.OfType<IGameCommandHandler>() );
		_replacementRules.AddRange( customModules.OfType<IReplacementRule>() );
		_stateBasedActionRules.AddRange( customModules.OfType<IStateBasedActionRule>() );
		_triggerRules.AddRange( customModules.OfType<ITriggerRule>() );
		_lifecycleRules.Add( new StandardLifecycleRule( format ) );
		_lifecycleRules.AddRange( customModules.OfType<IMatchLifecycleRule>() );

		_deckRules = customModules.OfType<IDeckRuleProvider>().LastOrDefault() ?? new StandardDeckRuleProvider();

		foreach ( IDeckRuleProvider provider in customModules.OfType<IDeckRuleProvider>() )
			_additionalDeckRules.AddRange( provider.AdditionalDeckRules );

		TurnPolicy     = customModules.OfType<ITurnStructurePolicy>().LastOrDefault() ?? new StandardTurnPolicy();
		PriorityPolicy = customModules.OfType<IPriorityPolicy>().LastOrDefault()      ?? new StandardPriorityPolicy();
		OutcomeRules   = customModules.OfType<IOutcomeRule>().ToArray();
	}


	public RuleDecision Evaluate( GameIntent intent, RulesContext context )
	{
		ArgumentNullException.ThrowIfNull( intent );
		ArgumentNullException.ThrowIfNull( context );

		foreach ( IGameRuleModule invariant in _invariants )
		{
			RuleEvaluation evaluation = invariant.Evaluate( intent, context );

			if ( evaluation.Verdict == RuleVerdict.Deny )
				return RuleDecision.Reject( evaluation.Code, evaluation.Message );
		}

		bool            governed  = false;
		RuleEvaluation? rejection = null;

		foreach ( IGameRuleModule module in _modules )
		{
			RuleEvaluation evaluation = module.Evaluate( intent, context );

			if ( evaluation.Verdict == RuleVerdict.Deny )
				rejection = evaluation;
			else if ( evaluation.Verdict == RuleVerdict.OverrideAllow )
				rejection = null;

			governed |= evaluation.Verdict is RuleVerdict.Allow or RuleVerdict.OverrideAllow;
		}

		if ( rejection is RuleEvaluation denied )
			return RuleDecision.Reject( denied.Code, denied.Message );

		if ( !governed )
			return RuleDecision.Reject( "action.unsupported", "This rules profile does not support that action." );

		RuleDecision core = CreateCommand( intent, context );

		if ( core.Allowed )
			return core;

		foreach ( IGameCommandProvider provider in _commandProviders )
		{
			if ( provider.TryCreateCommand( intent, context, out GameCommand? command ) && command is not null )
				return RuleDecision.Permit( command );
		}

		return core;
	}


	public bool TryExecuteCustomCommand( GameCommand command, GameFlowCoordinator flow )
	{
		foreach ( IGameCommandHandler handler in _commandHandlers )
		{
			if ( !handler.CanExecute( command ) )
				continue;

			handler.Execute( command, new GameExecutionContext { Match = _match!, Flow = flow } );

			return true;
		}

		return false;
	}


	public GameCommand ApplyReplacements( GameCommand command, RulesContext context )
	{
		GameCommand current = command;

		foreach ( IReplacementRule rule in _replacementRules )
			current = rule.Replace( current, context ) ?? throw new InvalidOperationException( "A replacement rule returned no command." );

		return current;
	}


	public IReadOnlyList<GameCommand> EvaluateStateBasedActions( RulesContext context )
	{
		return _stateBasedActionRules.SelectMany( rule => rule.EvaluateState( context ) ).ToArray();
	}


	public IReadOnlyList<StackEntry> CollectTriggers( IReadOnlyList<GameEvent> events, RulesContext context )
	{
		return _triggerRules.SelectMany( rule => rule.CollectTriggers( events, context ) ).ToArray();
	}


	public RuleEvaluation CanJoin( Match match, Connection connection, IReadOnlyList<Seat> seats )
	{
		return EvaluateLifecycle( rule => rule.CanJoin( match, connection, seats ) );
	}


	public RuleEvaluation CanSubmitDeck( Match match, Seat seat, Deck deck )
	{
		return EvaluateLifecycle( rule => rule.CanSubmitDeck( match, seat, deck ) );
	}


	public RuleEvaluation CanSetReady( Match match, Seat seat, bool ready )
	{
		return EvaluateLifecycle( rule => rule.CanSetReady( match, seat, ready ) );
	}


	public RuleEvaluation CanBegin( Match match, IReadOnlyList<Seat> seats )
	{
		return EvaluateLifecycle( rule => rule.CanBegin( match, seats ) );
	}


	private RuleEvaluation EvaluateLifecycle( Func<IMatchLifecycleRule, RuleEvaluation> evaluate )
	{
		RuleEvaluation? rejection = null;
		bool            governed  = false;

		foreach ( IMatchLifecycleRule rule in _lifecycleRules )
		{
			RuleEvaluation result = evaluate( rule );

			if ( result.Verdict == RuleVerdict.Deny )
				rejection = result;
			else if ( result.Verdict == RuleVerdict.OverrideAllow )
				rejection = null;

			governed |= result.Verdict is RuleVerdict.Allow or RuleVerdict.OverrideAllow;
		}

		return rejection ?? ( governed? RuleEvaluation.Allow() : RuleEvaluation.Deny( "lifecycle.unsupported", "No lifecycle rule handles this transition." ) );
	}


	public DeckFormatDefinition CreateFormat( string formatCode )
	{
		MTGFormat            format     = _format ?? throw new InvalidOperationException( "Initialize the rules session before requesting deck rules." );
		DeckFormatDefinition definition = ( _deckRules ?? new StandardDeckRuleProvider() ).CreateDeckFormat( format );

		return string.Equals( definition.Code, formatCode, StringComparison.OrdinalIgnoreCase )? definition : definition with { Code = formatCode };
	}


	public DeckValidationReport ValidateDeck( Deck deck )
	{
		MTGFormat format = _format ?? throw new InvalidOperationException( "Initialize the rules session before validating a deck." );

		return DeckValidator.Validate( deck, CreateFormat( format.FormatCode ), _additionalDeckRules );
	}


	/// <summary>
	///     Trusted setup performed by Match after lobby validation succeeds.
	/// </summary>
	public void SetupMatch( IReadOnlyList<Seat> players )
	{
		foreach ( Seat player in players )
		{
			if ( player.SubmittedDeck is not { } deck )
				continue;

			if ( player.Library is not null )
				PopulateZoneFromDeck( player, deck, player.Library );

			if ( player.Command is not null )
				PopulateZoneFromDeck( player, deck, player.Command, DeckSections.Commander );

			if ( player.Sideboard is not null )
				PopulateZoneFromDeck( player, deck, player.Sideboard, DeckSections.Sideboard );
		}
	}


	private static RuleDecision CreateCommand( GameIntent intent, RulesContext context )
	{
		return intent switch
			   {
				   SelectCardIntent select   => RuleDecision.Permit( new SelectCardCommand( context.Actor, select.Card ) ),
				   GrabCardIntent grab       => RuleDecision.Permit( new GrabCardCommand( grab.ActorPlayerId, grab.Card ) ),
				   ReleaseCardIntent release => RuleDecision.Permit( new ReleaseCardCommand( release.ActorPlayerId, release.Card ) ),
				   MoveCardIntent move       => RuleDecision.Permit( new MoveCardCommand( move.Card, move.Destination, move.FreeformPose ) ),
				   ThrowCardIntent thrown    => RuleDecision.Permit( new ThrowCardCommand( thrown.Card, thrown.Velocity, thrown.AngularVelocity ) ),
				   FlipCardIntent flip       => RuleDecision.Permit( new FlipCardCommand( flip.Card ) ),
				   TapCardIntent tap         => RuleDecision.Permit( new TapCardCommand( tap.Card, tap.Tapped ) ),
				   PassPriorityIntent        => RuleDecision.Permit( new PassPriorityCommand( context.Actor ) ),
				   EndTurnIntent             => RuleDecision.Permit( new EndTurnCommand( context.Actor ) ),
				   ConcedeIntent             => RuleDecision.Permit( new ConcedeCommand( context.Actor ) ),
				   _                         => RuleDecision.Reject( "action.unsupported", "This action has no authoritative command." )
			   };
	}


	private static void ValidateModuleComposition( IReadOnlyList<RulesModule> modules, MTGFormat format )
	{
		Dictionary<string, RulesModule> byId = new Dictionary<string, RulesModule>( StringComparer.OrdinalIgnoreCase );

		foreach ( RulesModule module in modules )
		{
			if ( !byId.TryAdd( module.EffectiveModuleId, module ) )
				throw new InvalidOperationException( $"Rules module '{module.EffectiveModuleId}' is registered more than once." );
		}

		foreach ( RulesModule module in modules )
		{
			foreach ( string dependency in module.Dependencies.Where( value => !string.IsNullOrWhiteSpace( value ) ) )
			{
				if ( !byId.ContainsKey( dependency.Trim() ) )
					throw new InvalidOperationException( $"Rules module '{module.EffectiveModuleId}' requires missing module '{dependency}'." );
			}

			foreach ( string incompatible in module.IncompatibleWith.Where( value => !string.IsNullOrWhiteSpace( value ) ) )
			{
				if ( byId.ContainsKey( incompatible.Trim() ) )
					throw new InvalidOperationException( $"Rules module '{module.EffectiveModuleId}' is incompatible with '{incompatible}'." );
			}
		}

		foreach ( string required in format.RequiredRuleModules.Where( value => !string.IsNullOrWhiteSpace( value ) ) )
		{
			if ( !byId.ContainsKey( required.Trim() ) )
				throw new InvalidOperationException( $"Format '{format.DisplayName}' requires missing rules module '{required}'." );
		}
	}


	private IReadOnlyList<CardObject> PopulateZoneFromDeck( Seat player, Deck deck, ZoneObject zone, string section = DeckSections.Main, MtgZoneCardState state = MtgZoneCardState.ZoneDefault )
	{
		List<CardObject> cards               = new List<CardObject>();
		bool             isDeclaredCommander = string.Equals( section, DeckSections.Commander, StringComparison.OrdinalIgnoreCase );

		foreach ( DeckEntry entry in deck.Entries.Where( entry => string.Equals( entry.Section, section, StringComparison.OrdinalIgnoreCase ) ) )
		{
			for ( int copy = 0; copy < entry.Quantity; copy++ )
			{
				CardObject card = CreateCard( player, entry.Card.ScryfallId );
				card.IsDeclaredCommander = isDeclaredCommander;
				zone.AddCard( card, state, animate: false );
				cards.Add( card );
			}
		}

		return cards;
	}


	private CardObject CreateCard( Seat seat, Guid printingId )
	{
		if ( !Networking.IsHost )
			throw new InvalidOperationException( "Only the host can create match cards." );

		Match      match      = _match ?? throw new InvalidOperationException( "The rules session has no match." );
		GameObject cardObject = new GameObject( match.GameObject, true, "MTG Card" );
		CardObject card       = cardObject.Components.Create<CardObject>();
		card.OwnerPlayerId      = seat.Player;
		card.ControllerPlayerId = seat.Player;
		card.SetCard( printingId );

		if ( Networking.IsActive )
			cardObject.NetworkSpawn();

		return card;
	}


	private sealed class MatchStateRule : IGameRuleModule
	{
		public RuleEvaluation Evaluate( GameIntent intent, RulesContext context )
		{
			if ( intent is ConcedeIntent )
				return context.MatchState is MatchState.Mulligan or MatchState.Playing? RuleEvaluation.Allow() : RuleEvaluation.Deny( "match.not_playing", "You can only concede after the match has started." );

			return context.MatchState == MatchState.Playing? RuleEvaluation.Allow() : RuleEvaluation.Deny( "match.not_playing", "That action is only available while the match is being played." );
		}
	}


	private sealed class ActorEligibilityRule : IGameRuleModule
	{
		public RuleEvaluation Evaluate( GameIntent intent, RulesContext context )
		{
			if ( context.Actor.Player != intent.ActorPlayerId )
				return RuleEvaluation.Deny( "actor.mismatch", "The request does not belong to this participant." );

			return context.Actor.Eliminated? RuleEvaluation.Deny( "actor.eliminated", "An eliminated participant cannot take game actions." ) : RuleEvaluation.Allow();
		}
	}


	private sealed class CardInteractionRule : IGameRuleModule
	{
		public RuleEvaluation Evaluate( GameIntent intent, RulesContext context )
		{
			if ( intent is SelectCardIntent selection )
				return selection.Card is null || selection.Card.IsValid()
					? RuleEvaluation.Allow()
					: RuleEvaluation.Deny( "card.missing", "The selected card no longer exists." );

			CardObject? card = intent switch
							   {
								   GrabCardIntent value    => value.Card,
								   ReleaseCardIntent value => value.Card,
								   MoveCardIntent value    => value.Card,
								   ThrowCardIntent value   => value.Card,
								   FlipCardIntent value    => value.Card,
								   TapCardIntent value     => value.Card,
								   _                       => null
							   };

			if ( card is null )
				return RuleEvaluation.Abstain();

			if ( !card.IsValid() )
				return RuleEvaluation.Deny( "card.missing", "The selected card no longer exists." );

			Guid controller = card.ControllerPlayerId != Guid.Empty? card.ControllerPlayerId : card.OwnerPlayerId;

			if ( controller != Guid.Empty && controller != context.Actor.Player )
				return RuleEvaluation.Deny( "card.not_controller", "Only this card's controller may manipulate it." );

			if ( intent is GrabCardIntent && card.GrabbedByPlayerId != Guid.Empty && card.GrabbedByPlayerId != context.Actor.Player )
				return RuleEvaluation.Deny( "card.already_grabbed", "Another participant is currently moving this card." );

			if ( intent is ReleaseCardIntent && card.GrabbedByPlayerId != context.Actor.Player )
				return RuleEvaluation.Deny( "card.not_grabbed", "You are not currently moving this card." );

			if ( intent is MoveCardIntent or ThrowCardIntent )
			{
				if ( card.GrabbedByPlayerId != context.Actor.Player )
					return RuleEvaluation.Deny( "card.not_grabbed", "Grab this card before moving it." );
			}

			if ( intent is MoveCardIntent move )
			{
				RuleEvaluation commandZone = CardMovePolicy.EvaluateCommandZoneEntry( move.Destination is CommandZone, card.IsDeclaredCommander );

				if ( commandZone.Verdict == RuleVerdict.Deny )
					return commandZone;

				if ( !move.Destination.CanAccept( card ) )
					return RuleEvaluation.Deny( "zone.full", "The destination zone cannot accept this card." );

				if ( move.Destination.OwnerPlayerId != Guid.Empty && move.Destination.OwnerPlayerId != context.Actor.Player )
					return RuleEvaluation.Deny( "zone.not_owner", "You cannot manually move cards into another participant's private zone." );
			}

			if ( intent is TapCardIntent && ZoneObject.Find( card.ZoneId )?.Type != ZoneType.Battlefield )
				return RuleEvaluation.Deny( "card.not_battlefield", "Only battlefield permanents can be tapped." );

			return RuleEvaluation.Allow();
		}
	}


	private sealed class FlowPermissionRule : IGameRuleModule
	{
		public RuleEvaluation Evaluate( GameIntent intent, RulesContext context )
		{
			return FlowPermissionPolicy.Evaluate( intent, context.Actor.Player, context.Flow );
		}
	}


	private sealed class StandardDeckRuleProvider : IDeckRuleProvider
	{
		public IEnumerable<IDeckFormatRule> AdditionalDeckRules => Array.Empty<IDeckFormatRule>();


		public DeckFormatDefinition CreateDeckFormat( MTGFormat format )
		{
			return DeckFormatDefinition.Constructed( format.FormatCode, format.DisplayName, format.DeckSize );
		}
	}


	private sealed class StandardTurnPolicy : ITurnStructurePolicy
	{
		public IReadOnlyList<TurnStep> Steps { get; } =
			[
				TurnStep.Untap,
				TurnStep.Upkeep,
				TurnStep.Draw,
				TurnStep.PrecombatMain,
				TurnStep.BeginningOfCombat,
				TurnStep.DeclareAttackers,
				TurnStep.DeclareBlockers,
				TurnStep.FirstStrikeCombatDamage,
				TurnStep.CombatDamage,
				TurnStep.EndOfCombat,
				TurnStep.PostcombatMain,
				TurnStep.End,
				TurnStep.Cleanup
			];


		public bool GrantsPriority( TurnStep step, RulesContext context )
		{
			return step is not (TurnStep.Untap or TurnStep.Cleanup);
		}


		public bool CanTakeTurn( Seat seat, RulesContext context )
		{
			return seat.Occupied && seat.IsConnected && !seat.Eliminated;
		}


		public Seat? NextActivePlayer( Seat current, RulesContext context )
		{
			int currentIndex = -1;

			for ( int index = 0; index < context.Seats.Count; index++ )
			{
				if ( ReferenceEquals( context.Seats[index], current ) || context.Seats[index].Player == current.Player )
				{
					currentIndex = index;

					break;
				}
			}

			if ( currentIndex < 0 )
				return null;

			for ( int offset = 1; offset <= context.Seats.Count; offset++ )
			{
				Seat candidate = context.Seats[( currentIndex + offset ) % context.Seats.Count];

				if ( CanTakeTurn( candidate, context ) )
					return candidate;
			}

			return null;
		}
	}


	private sealed class StandardPriorityPolicy : IPriorityPolicy
	{
		public IReadOnlyList<Seat> EligiblePlayers( RulesContext context )
		{
			return context.Seats.Where( seat => seat.Occupied && seat.IsConnected && !seat.Eliminated ).ToArray();
		}


		public Seat? FirstPlayer( RulesContext context )
		{
			IReadOnlyList<Seat> eligible = EligiblePlayers( context );

			return eligible.FirstOrDefault( seat => seat.Player == context.Flow.ActivePlayerId ) ?? eligible.FirstOrDefault();
		}
	}


	private sealed class StandardLifecycleRule : IMatchLifecycleRule
	{
		private readonly MTGFormat _format;


		public StandardLifecycleRule( MTGFormat format )
		{
			_format = format;
		}


		public RuleEvaluation CanJoin( Match match, Connection connection, IReadOnlyList<Seat> seats )
		{
			if ( match.State != MatchState.Lobby )
				return RuleEvaluation.Deny( "lobby.closed", "The match is no longer accepting participants." );

			return seats.Count < _format.MaximumPlayers? RuleEvaluation.Allow() : RuleEvaluation.Deny( "lobby.full", "The match has no open seats." );
		}


		public RuleEvaluation CanSubmitDeck( Match match, Seat seat, Deck deck )
		{
			if ( match.State != MatchState.Lobby )
				return RuleEvaluation.Deny( "deck.lobby_closed", "Decks can only be submitted in the lobby." );

			return string.IsNullOrWhiteSpace( deck.FormatCode ) || string.Equals( deck.FormatCode, _format.FormatCode, StringComparison.OrdinalIgnoreCase )? RuleEvaluation.Allow() : RuleEvaluation.Deny( "deck.wrong_format", $"This lobby requires a {_format.DisplayName} deck." );
		}


		public RuleEvaluation CanSetReady( Match match, Seat seat, bool ready )
		{
			if ( match.State != MatchState.Lobby )
				return RuleEvaluation.Deny( "ready.lobby_closed", "Ready state can only change in the lobby." );

			return !ready || seat.HasSubmittedDeck? RuleEvaluation.Allow() : RuleEvaluation.Deny( "ready.deck_required", "Submit a valid deck before readying up." );
		}


		public RuleEvaluation CanBegin( Match match, IReadOnlyList<Seat> seats )
		{
			if ( match.State != MatchState.Lobby )
				return RuleEvaluation.Deny( "match.already_started", "The match has already started." );

			if ( seats.Count < _format.MinimumPlayers || seats.Count > _format.MaximumPlayers )
				return RuleEvaluation.Deny( "match.player_count", $"{_format.DisplayName} requires {_format.MinimumPlayers}–{_format.MaximumPlayers} participants." );

			return seats.All( seat => seat.IsConnected && seat.HasSubmittedDeck && seat.Ready && seat.SubmittedDeck is not null )? RuleEvaluation.Allow() : RuleEvaluation.Deny( "match.not_ready", "Every participant needs an accepted deck and must be ready." );
		}
	}
}
