# Card database architecture

The runtime database is game-scoped in `FileSystem.Data`. Scryfall is an external,
open-ended source contract; its DTO vocabulary must not leak into rules as a
closed set of assumptions.

## Boundaries

- `Scryfall` downloads bulk sources and records their exact versioned URI,
  timestamp, size, and checksum.
- `ScryfallCardNormalizer` validates structural gameplay data, preserves raw
  provider vocabulary, and derives stable `CardCapabilities`.
- `DatabaseBuilder` streams sources into an immutable v7 generation.
- `DatabaseGenerationStore` writes the generation manifest last. Incomplete
  generations are never selected by `CardDatabase`.
- `DatabaseManager` owns the single provisioning/repair operation and classifies
  failures. Schema incompatibility is not retried as source corruption.
- `Match` advertises the host checksum and source snapshot. Clients rebuild from
  that exact snapshot when available and may not submit or ready before matching.
- Rules and deck validation consume normalized gameplay data and capabilities,
  never Scryfall DTOs or presentation-only frame metadata.

## Unknown source values

Open-ended values are retained in their `*Code` fields. Their known enum
projection becomes `Unknown`, and one summary per field is written to the
generation manifest and log. Unknown presentation values do not invalidate the
catalog. Unknown legality and layout capability fail closed for deck construction.

## Generation activation

Each build writes under `card-database/generations/{generation-id}/`. The manifest
contains artifact checksums, source provenance, and unknown-value diagnostics.
It is created only after every artifact has been flushed and hashed. A failed or
cancelled build therefore leaves the previously completed generation usable.
