# Plain-text deck import

This folder contains the plain-text deck import pipeline. It turns a pasted deck list into a `DeckImportResult` containing a normalized `Deck` and any line-level issues.

Website requests and website-specific response adapters are not part of this pipeline.

## Request flow

```text
DeckImport panel
      |
      | pasted text
      v
PlainTextDeckParser
      |
      | section + quantity + card identity
      v
local CardDatabase lookup
      |
      v
DeckImportResult { Deck, Issues }
```

## Files

| File | Responsibility |
| --- | --- |
| `../DeckImport.razor` | Collects pasted text and previews the import result. |
| `../DeckImport.razor.scss` | Styles the import panel. |
| `PlainTextDeckParser.cs` | Parses the text and resolves card identities against the local database. |
| `DeckImportModels.cs` | Defines import options, results, and issues. |
| `../Deck.cs` | Defines the resulting portable deck model. |

## Using the panel

The scene must contain a `DeckImport` panel component, and the scene-level `DatabaseManager` must have opened the local card database.

1. Paste a deck list into **Deck List**.
2. Select **Import Text**.
3. The panel creates a `PlainTextDeckParser`, which looks cards up in the local database.
4. The resulting deck and issues are assigned to `DeckImport.Result`.

The current handler sets `GameObject.Enabled = false` after importing. This means another system is expected to retrieve `Result` and continue the deck-submission flow.

## Accepted text format

Parsing begins in the `main` section. Empty lines and lines beginning with `//` or `#` are ignored.

Recognized section headings are case-insensitive and may end in `:`:

- `Deck`, `Main`, or `Mainboard`
- `Sideboard` or `Side Board`
- `Commander` or `Commanders`
- `Companion` or `Companions`
- `Maybeboard` or `Maybe Board`

`SB:` at the beginning of a card line switches to the sideboard and parses the rest of the line.

Example:

```text
Commander:
1 Atraxa, Praetors' Voice

Mainboard:
1 Sol Ring
2x Forest
1 Counterspell (2XM) 47
1 Arcane Signet [ELD:331]

Sideboard:
1 Negate
```

A quantity is optional and defaults to one. Supported forms include `4 Card Name`, `4x Card Name`, and `4X Card Name`.

## Card identity lookup

Each card line is interpreted in this order:

1. Scryfall UUID anywhere in the card text.
2. Name, set, and collector number: `Card Name (SET) 123`.
3. Bracket form: `Card Name [SET:123]`.
4. Name and set query form: `Card Name&set=SET`.
5. Name and set: `Card Name (SET)`.
6. Exact card name after removing a trailing `*F*` or `*E*` marker.

The resolver then tries:

1. exact Scryfall printing UUID;
2. exact set code and collector number;
3. exact name, optionally filtered to the requested set.

Set matching is case-insensitive. A suffix after `_` is discarded, so `MOM_foil` is resolved as set `MOM`.

There is no fuzzy or partial name search. When an exact name matches multiple printings, the first database result is selected and an `AmbiguousCard` warning is returned.

Entries with the same section and resolved Scryfall printing ID are merged by adding their quantities. The same printing in two different sections remains two entries.

## Result handling

An import may be partial. An unresolved line adds a `CardNotFound` error, but parsing continues and successfully resolved entries remain in the deck.

Currently emitted issue codes are:

| Code | Meaning |
| --- | --- |
| `InvalidQuantity` | A recognized numeric quantity is zero, too large, or otherwise invalid. |
| `CardNotFound` | No exact local database match was found. |
| `AmbiguousCard` | Multiple printings matched and the first was selected. This is a warning. |

`InvalidLine` remains reserved but is not currently emitted.

Consumers should inspect both sides of the result:

```csharp
DeckImportResult result = importer.Import( text, options );

if ( result.HasErrors )
{
	// Display the issues or reject the partial deck.
}

Deck deck = result.Deck;
```

`DeckImportResult` does not save, submit, or validate the deck automatically. Those are separate steps for the calling system.

## Database requirement

`PlainTextDeckParser` expects `CardDatabase` to be open. `DatabaseManager` normally owns the database lease for the scene and exposes readiness through `IsReady`, `Completion`, `State`, and `FailureReason`.

The current panel does not wait for that state. Calling the importer before the database is ready can throw `Card database is not open`.
