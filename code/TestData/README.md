# Card normalization fixtures

`TestCards.json` is the canonical fixture suite for
`ScryfallCardNormalizer` and the normalized database JSON contract.

The suite contains:

- valid real-card and synthetic edge cases;
- every Scryfall card layout recognized by the normalizer;
- expected normalized layout names;
- expected mana cost for every normalized face;
- expected `produced_mana` state (`null`, `empty`, or `values`);
- complete Card-object field mapping and database JSON round-trip checks;
- forward-compatibility fixtures for unknown root, face, image, price,
  preview, and related-card fields;
- invalid source cases with an expected diagnostic substring.

The copy in the s&box data directory is a convenience mirror. Automated
tests read this tracked copy so the suite remains portable.

The `Integration` tests also stream the local Oracle Cards, Rulings, Sets,
and Symbology sources when those files are present. This checks every local
source object without embedding the large daily Scryfall exports in git.
The final build/open test requires the s&box editor test host because the
plain `dotnet test` host does not initialize `FileSystem.Data`.

## Regenerating

Run `misc/GenerateCardNormalizationTestSuite.ps1` with:

- `BulkPath` pointing to the local gzipped Scryfall oracle bulk file;
- `LegacyFixturePath` pointing to either the prior concatenated fixture or
  an existing generated suite;
- `OutputPath` pointing to this `TestCards.json`.

The generator selects stable named examples from the bulk file, retains the
legacy multifaced examples, removes duplicate IDs, and appends synthetic
edge and rejection cases.

## Running

The s&box editor generates the `UnitTests` project after the project gains
an `UnitTests` directory and the editor is restarted. Tests can then be run
with:

```text
dotnet test UnitTests/mtgsbox.unittest.csproj
```
