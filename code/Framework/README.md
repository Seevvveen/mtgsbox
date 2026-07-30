# MTG game framework

Place `MtgGameDirector` and one `MtgGameRules` component in the match
scene. `MtgTableAnchor` and `MtgTableCamera` are optional presentation
helpers.

An actual game variant subclasses `MtgGameRules` and normally overrides:

- `CreateFormat` for deck construction and legality.
- `SetupMatch` to create each player's zones, populate their library, draw
  opening hands, and shuffle.
- `TryMulligan` and `OnOpeningHandKept`.
- `CanMoveCard`, `CanFlipCard`, and `CanTapCard` for permissions.
- `ActionsFor` and `TryPerformAction` for game-specific commands.
- `OnTurnStarted`, `OnPhaseChanged`, and
  `OnAllPlayersPassedPriority`.

Use `CreatePlayerZone`, `PopulateZoneFromDeck`, and `CreateCard` inside the
rules subclass. They assign ownership and network-spawn the resulting
objects.

Clients submit decks and actions through `MtgGameDirector`. Do not expose
new gameplay mutations as owner RPCs on cards or zones; add a host RPC to
the director and validate it through the active rules component.
