# Modular rules architecture

`Match` is the authoritative request gateway and lifecycle owner. Player-facing
code submits an immutable `GameIntent`; it never calls card or zone mutation APIs
for a live match.

The request pipeline is:

1. `Match` resolves the RPC caller to a `Seat`.
2. `RulesEngine` checks non-overridable match and actor invariants.
3. Core and format modules evaluate the intent in deterministic `Order`.
4. The rules session returns a structured rejection or trusted `GameCommand`.
5. `GameActionExecutor` applies replacement rules and commits the transaction.
6. State-based actions run repeatedly until the state stabilizes.
7. Trigger rules collect stack entries from committed events.
8. Outcome rules run and Match synchronizes the resulting public state.

## Adding a format module

Derive from `RulesModule` and add the component beneath the match prefab. Set a
stable `ModuleId`, `ModuleVersion`, dependencies, incompatibilities, and `Order`.
List mandatory module IDs in the corresponding `MTGFormat.RequiredRuleModules`.
The lobby fails early if its module composition is invalid.

A module can implement any combination of:

- `IGameRuleModule` for action legality;
- `IDeckRuleProvider` for construction rules;
- `ITurnStructurePolicy` for steps and active-player rotation;
- `IPriorityPolicy` for priority order and eligibility;
- `IOutcomeRule` for player/team victory conditions;
- `IGameCommandProvider` and `IGameCommandHandler` for new actions;
- `IReplacementRule` for replacement/prevention behavior;
- `IStateBasedActionRule` for stabilization;
- `ITriggerRule` for triggered stack entries.

`RuleEvaluation.OverrideAllow()` may override an earlier policy denial, allowing a
format module to intentionally change standard card/zone/flow policy. It cannot
override match-state, caller-identity, or eliminated-participant invariants.

Modules receive `RulesContext` and must not locate dependencies through the scene.
They may inspect authoritative state but should return decisions, commands, or
events instead of directly mutating cards and zones.

## Commander and team variants

Commander should be a set of deck, command-zone, tax, commander-damage, and
outcome modules. A team variant such as Two-Headed Giant should provide turn,
priority, combat, and outcome policies, using `Seat.ParticipantGroupId` to group
participants. Because these concerns are separate modules, Commander and a team
variant can be combined without creating an inheritance tree.

The current implementation establishes the enforcement and extension boundaries.
Card-text execution, costs, targeting, continuous-effect layers, replacement
semantics, combat assignment, and format-specific rule packages remain incremental
modules built on these contracts.
