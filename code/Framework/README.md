# MTG game framework

Place one `GameDirector` and one `RulesEngine` subclass in the match scene or
rules prefab. `TableAnchor` and `TableCamera` are optional presentation
helpers.

Clients submit decks and gameplay requests through `GameDirector`. Do not
expose new gameplay mutations as owner RPCs on cards or zones. Add a host RPC
to the director and validate it through the active rules engine.

See [Game framework walkthrough](WALKTHROUGH.md) for the complete setup,
authority, RPC, director, and rules-engine guide.
