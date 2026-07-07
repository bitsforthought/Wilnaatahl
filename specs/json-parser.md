# JSON Parser for Import Feature — Implementation Plan

## Problem

The import feature (spec: `specs/import-export-feature.md`) needs a parser that
transforms a JSON file of genealogical data into the inputs `createFamilyGraph`
consumes: `(Person * CoupleId option) seq`, `Couple seq`, and the Name holdings
`(PersonId * NameHeld) seq`. The format uses numeric ids for people, couples,
huwilp, and names; per-person current and birth Wilp are `wilp` / `birthWilp`
references into a top-level `huwilp` array, and Name holdings are `namesHeld` rows
linking `names` to `people`. The format itself is specified in
[`specs/json-file-format.md`](./json-file-format.md); the Names/adoption additions
and the model types they feed are designed in
[`specs/names-and-detail-overlay.md`](./names-and-detail-overlay.md). This plan
covers the parser design, gap analysis, and implementation path — both the import
direction and the `toJson` export inverse.

## JSON schema

The on-disk JSON schema — every top-level array and field, with types,
null/absent semantics, and cross-references — is specified in
[`specs/json-file-format.md`](./json-file-format.md). This plan consumes that
format and focuses on the parser/transform that reads it into the domain model:
the gap analysis, validation rules, warnings, and code structure below describe
parser behavior, not the format itself.

## Gap analysis: JSON vs. current model

### Fields with no model equivalent (silently ignored)

| JSON field | Reason                                                              |
| ---------- | ------------------------------------------------------------------ |
| `deceased` | The model records death via `DateOfDeath` but has no boolean flag. |

`gender` is consumed for the [NodeShape mapping](#nodeshape-mapping); every other
person field maps to the model. In particular the raw `dateOfBirth` /
`dateOfDeath` strings are captured as display fallbacks
(`Person.DateOfBirthText` / `DateOfDeathText`) and `birthWilp` is resolved to
`Person.BirthWilp` — both introduced with the Names/adoption work in
[`specs/names-and-detail-overlay.md`](./names-and-detail-overlay.md).

### Structural gaps (worked around)

| Gap                                | Impact                                          | Workaround                                                |
| ---------------------------------- | ----------------------------------------------- | --------------------------------------------------------- |
| Unresolvable CoupleId on person    | Person references a couple not in the file.     | Person becomes a root. `UnresolvedCoupleId` warning.      |
| Unresolvable PersonId on couple    | Couple references a person not in the file.     | Couple dropped. `UnresolvedMember` warning.               |
|                                    | Persons referencing that couple become roots.   |                                                           |
| Unresolvable WilpId on person      | Person references a Wilp id not in `huwilp`,    | Person's `Kinship` becomes `NoneProvided`.                |
|                                    | or one that was ignored due to missing fields.  | `UnresolvedWilpId` warning.                               |
| Unresolvable birthWilp on person   | Person's `birthWilp` id isn't in `huwilp`.      | `BirthWilp` left `None`. `UnresolvedBirthWilpId` warning. |
| Pdeeḵ-only birthWilp on person     | `birthWilp` resolves to a name-less entry.      | `BirthWilp` left `None`. `BirthWilpNotNamed` warning.     |
| `kinshipNote` with a resolved Wilp | Person has both a resolving `wilp` and a note.  | Note dropped. `IgnoredKinshipNote` warning.               |
| Unresolvable nameId on holding     | `namesHeld` references a `names` id not present.| Holding dropped. `UnresolvedNameId` warning.              |
| Unresolvable personId on holding   | `namesHeld` references a person not present.    | Holding dropped. `UnresolvedNameHolder` warning.          |
| Unheld name                        | A `names` entry referenced by no holding.       | Dropped (held-only storage). `UnheldName` warning.        |
| Duplicate person `id`              | JSON id is the unique key; duplicates would     | Keep first occurrence. `DuplicatePersonId` warning.       |
|                                    | give two `Person` records the same `PersonId`.  |                                                           |
| Duplicate `coupleId`               | Same as above for couple lookups.               | Keep first occurrence. `DuplicateCoupleId` warning.       |
| Duplicate huwilp `id`              | Same as above for Wilp lookups.                 | Keep first occurrence. `DuplicateWilpId` warning.         |
| Duplicate name `id`                | Same as above for name lookups.                 | Keep first occurrence. `DuplicateNameId` warning.         |
| Distinct name ids, same `text`     | Redundant `names` entries for one Name.         | Both resolve to one `Name`. `DuplicateNameText` warning.  |
| Messy date strings (person)        | `DateOnly` requires a valid date.               | Parse `normalizedDateOfBirth/Death` when present; warn if |
|                                    |                                                 | not ISO 8601. The raw `dateOfBirth/Death` are kept as     |
|                                    |                                                 | display text regardless.                                  |
| Messy date string (`dateOfUnion`)  | `DateOnly` requires a valid date.               | Skip unparseable values. `UnparsableCoupleDate` warning.  |

Because a `Name`'s identity is its text, two distinct name `id`s carrying the same
`text` both resolve to the same `Name`, but the redundant entry is flagged with a
`DuplicateNameText` warning. Name-holding resolution, unheld names, and birth-Wilp
resolution are detailed under [Names and holdings](#names-and-holdings) and
[Birth Wilp resolution](#birth-wilp-resolution).

### Wilp validation (per `huwilp` entry)

Each `huwilp` entry must carry some usable identity. The model's `Kinship`
type accommodates both "specific Wilp known" (`Wilp w`, requiring both a
name and a Pdeek) and "Pdeek known but specific Wilp unknown"
(`UnknownWilp p`, requiring only a Pdeek), so the validation rules are:

| Situation                       | Action                                                       |
| ------------------------------- | ------------------------------------------------------------ |
| Both `name` and `pdeek` missing | `WilpMissingNameAndPdeek` warning. Wilp dropped from the     |
|                                 | lookup; any person referencing it gets                       |
|                                 | `Kinship = NoneProvided` plus an `UnresolvedWilpId` warning. |
| `pdeek` present and recognized, | Wilp kept as `UnknownWilp pdeek`. Referencing persons        |
| no `name`                       | resolve to that `Kinship`. No warning — this is a            |
|                                 | first-class case in the model.                               |
| `pdeek` present but             | `UnknownPdeek` warning (whether or not `name` is also        |
| unrecognized                    | present). Wilp dropped; references resolve as in the         |
|                                 | both-missing row.                                            |
| Only `name` (no `pdeek`)        | `WilpMissingPdeek` warning. Wilp dropped from the lookup;    |
|                                 | references resolve as in the both-missing row.               |
| Both present and `pdeek`        | Wilp kept as `Wilp { Name; Pdeek }`. Referencing persons     |
| recognized                      | resolve to that `Kinship`.                                   |

`pdeek` strings are matched leniently: NFD-decompose, lower-case (invariant), then
keep only ASCII letters `a`–`z` — so case, whitespace, apostrophes/glottal marks,
and underline diacritics are all ignored. The recognized canonical forms are:

- `LaxGibuu` — `laxgibuu`
- `LaxSkiik` — `laxskiik`, `laxsgiik`
- `Ganeda` — `ganeda`, `ganada`, `laxseel`
- `Giskaast` — `giskaast`, `giskahaast`

Any other value yields `UnknownPdeek`.

### Per-person Wilp resolution

For each person:

- `wilp` absent or `null` → `Kinship = NoneProvided kinshipNote` (no warning —
  this is normal for people whose Wilp isn't known). The person's `kinshipNote`,
  if any, is carried as `NoneProvided (Some note)`, else `NoneProvided None`.
- `wilp = id`, `id` resolves to a usable entry → `Kinship = Wilp w` or
  `Kinship = UnknownWilp p` per the validation table above. A `kinshipNote`
  present alongside a resolving `wilp` is dropped with an `IgnoredKinshipNote`
  warning.
- `wilp = id`, `id` not in `huwilp` (or in `huwilp` but dropped due to
  validation failure) → `Kinship = NoneProvided kinshipNote` plus an
  `UnresolvedWilpId` warning.

`Kinship.NoneProvided` carries an optional free-form note (`string option`); see
[`specs/names-and-detail-overlay.md`](./names-and-detail-overlay.md).

### Birth Wilp resolution

`birthWilp` is a second reference into `huwilp` naming the Wilp a person was
**born into** (which differs from their current `wilp` after adoption). It
resolves against the same validated huwilp table, but into
`Person.BirthWilp : Wilp option` — only a *named* Wilp qualifies:

- absent / `null` → `None`.
- resolves to a named `Wilp w` → `Some w`.
- resolves to a Pdeeḵ-only (name-less) entry → `None` + `BirthWilpNotNamed`.
- present but unresolvable → `None` + `UnresolvedBirthWilpId`.

### Names and holdings

The `names` array is deduplicated by `id` (`DuplicateNameId`, keeping the first)
into an `id → Name` table used only to resolve holdings; the ids are discarded
afterward. Because a `Name`'s identity is its text, two distinct ids with the same
text both resolve to the same `Name` but emit a `DuplicateNameText` warning (the
entry is redundant). Each `namesHeld` row is then resolved against that table and
the person set:

- unresolvable `nameId` → holding dropped, `UnresolvedNameId` warning.
- unresolvable `personId` → holding dropped, `UnresolvedNameHolder` warning.
- otherwise → a `(PersonId, NameHeld)` pair, the `NameHeld` carrying the resolved
  `Name` by value (plus `nameDate` / `nameOrder`).

A `names` entry referenced by no surviving holding is an **unheld** name — dropped
(the graph stores held names only) with an `UnheldName` warning. See
[`specs/names-and-detail-overlay.md`](./names-and-detail-overlay.md) for the
`Name` / `NameHeld` model, text-identity, and held-only storage.

### NodeShape mapping

`gender = "M"` → `Cube`, `gender = "F"` → `Sphere`.

## Library choice: Thoth.Json

**Packages used:**

| Package                 | Used when            | Purpose                    |
| ----------------------- | -------------------- | -------------------------- |
| `Thoth.Json.Core`       | Always               | Platform-agnostic decoders |
| `Thoth.Json.Newtonsoft` | .NET (tests, server) | .NET JSON backend          |
| `Thoth.Json.JavaScript` | Fable (browser)      | JS JSON backend            |

Thoth.Json provides composable, type-safe decoders with explicit error messages.
Each field decoder handles optional/null values naturally. The decoder pipeline
parses the JSON into intermediate `RawPerson`, `RawCouple`, `RawWilp`, `RawName`,
and `RawNameHeld` records, then a separate transformation step converts to the
model's `Person * CoupleId option`, `Couple`, and `(PersonId * NameHeld)`
holdings. The same Raw records back the `toJson` export, encoded via matching
Thoth.Json encoders.

## Architecture

```
JSON string
    │  JsonReader.read      (Thoth.Json decoders over JsonContracts.Raw* types)
    ▼
RawFile  (Result<RawFile, string>)
    │  Transform.transform
    │   • Reject empty people (EmptyPeopleArray)
    │   • Deduplicate people / couples / huwilp / names by id
    │   • Validate huwilp → Kinship lookup table
    │   • Validate couple members
    │   • Resolve parent, wilp, and birthWilp refs; apply kinshipNote
    │   • Resolve name holdings; drop unheld names
    │   • Parse dates; map gender; build Couples
    │   • Collect warnings
    ▼
ImportResult  (Result<ImportResult, ImportError>)

Transform.fromJson : string → Result<ImportResult, ImportError>
    (import entry point — JsonReader.read ↦ mapError ↦ bind transform)

Transform.toJson : FamilyGraph → string
    (export inverse — synthesize Raw* records, then JsonWriter.write)
```

## Types

The `Raw*` records live in `JsonContracts.fs`; the `ImportWarning` /
`ImportError` / `ImportResult` types live in `Transform.fs`. See
[`specs/names-and-detail-overlay.md`](./names-and-detail-overlay.md) for the model
types the transform produces (`Name`, `NameHeld`, the `Person` fields, and
`Kinship.NoneProvided of string option`).

```fsharp
/// Intermediate type — what Thoth.Json decodes into for a person.
type RawPerson = {
    Id: int
    Name: string option              // colonial name; optional
    Parents: int option              // CoupleId reference; None if root
    Wilp: int option                 // current Wilp reference into the huwilp array
    BirthWilp: int option            // birth Wilp reference into the huwilp array
    KinshipNote: string option       // free-form note; used only when no Wilp resolves
    BirthOrder: int option
    RawDateOfBirth: string option    // free-form display text (dateOfBirth)
    RawDateOfDeath: string option    // free-form display text (dateOfDeath)
    NormalizedDateOfBirth: string option  // ISO-8601
    NormalizedDateOfDeath: string option  // ISO-8601
    Gender: string
}

/// Intermediate type — what Thoth.Json decodes into for a couple.
type RawCouple = {
    CoupleId: int
    Member1: int                     // JSON person id of first member
    Member2: int                     // JSON person id of second member
    DateOfUnion: string option       // ISO-8601; field may be absent
}

/// Intermediate type — what Thoth.Json decodes into for a Wilp.
/// At least one of Name and Pdeek must be present; the transform enforces this.
/// A pdeek-only entry resolves to `Kinship.UnknownWilp pdeek`; an entry with
/// both name and pdeek resolves to `Kinship.Wilp { Name; Pdeek }`.
type RawWilp = {
    Id: int
    Name: string option
    Pdeek: string option
}

/// Intermediate type — one entry of the `names` array.
type RawName = { Id: int; Text: string }

/// Intermediate type — one entry of the `namesHeld` array.
type RawNameHeld = {
    NameId: int
    PersonId: int
    NameDate: string option
    NameOrder: int option
}

/// Top-level decoded file contents. `Couples`, `Huwilp`, `Names`, and
/// `NamesHeld` default to empty when the top-level key is absent.
type RawFile = {
    People: RawPerson list
    Couples: RawCouple list
    Huwilp: RawWilp list
    Names: RawName list
    NamesHeld: RawNameHeld list
}

/// Things that went wrong but didn't prevent import.
type ImportWarning =
    | UnresolvedCoupleId of personName: string * coupleId: int
    | UnresolvedMember of coupleId: int * memberId: int
    | UnresolvedWilpId of personName: string * wilpId: int
    | UnresolvedBirthWilpId of personName: string * wilpId: int
    | BirthWilpNotNamed of personName: string * wilpId: int
    | IgnoredKinshipNote of personName: string
    | UnparseableDate of personName: string * fieldName: string * rawValue: string
    | UnparsableCoupleDate of coupleId: int * rawValue: string
    | DuplicatePersonId of id: int
    | DuplicateCoupleId of id: int
    | DuplicateWilpId of id: int
    | DuplicateNameId of id: int
    | DuplicateNameText of text: string
    | UnresolvedNameId of personId: int * nameId: int
    | UnresolvedNameHolder of nameId: int * personId: int
    | UnheldName of nameId: int * text: string
    | WilpMissingPdeek of id: int
    | WilpMissingNameAndPdeek of id: int
    | UnknownPdeek of wilpId: int * rawPdeek: string

/// Things that prevent an import from completing.
type ImportError =
    | InvalidJson of string
    | EmptyPeopleArray

type ImportResult = {
    PeopleAndCoupleIds: (Person * CoupleId option) list
    Couples: Couple list
    NameHoldings: (PersonId * NameHeld) list
    Warnings: ImportWarning list
}
```

## File layout

```
src/Wilnaatahl.Core/
  Persistence/
    JsonContracts.fs — internal Raw{Person,Couple,Wilp,Name,NameHeld,File} records
    JsonReader.fs    — internal Thoth.Json decoders; read: string → Result<RawFile, string>
    JsonWriter.fs    — internal Thoth.Json encoders; write: RawFile → string
    Transform.fs     — ImportWarning/ImportError/ImportResult; internal transform;
                       public Transform.fromJson (import) and Transform.toJson (export)
    ImportService.fs — importJsonText / loadSampleGraph: build a FamilyGraph
  ViewModel/
    ImportMessages.fs — ImportError.toMessage; ImportWarning.toMessage / summary

tests/Wilnaatahl.Core.Tests/
  Persistence/
    JsonReadWriteTests.fs — decoder/encoder round-trip, malformed JSON, missing fields
    TransformTests.fs     — transform logic: parent/Wilp/birthWilp/name resolution,
                            deduplication, date parsing, warning generation, edge cases
    ImportServiceTests.fs — end-to-end import into a FamilyGraph
  ViewModel/
    ImportMessagesTests.fs — message / summary rendering
```

New files are added to the `Wilnaatahl.Core.fsproj` and
`Wilnaatahl.Core.Tests.fsproj` `<Compile>` lists respectively.

## Notes & future considerations

- **`deceased` flag**: the model has `DateOfDeath` but no boolean flag. The
  decoder ignores `deceased` gracefully; future model work could add a flag and a
  corresponding decode step.
- **Multiple Wilp visualization**: the scene currently renders only one Wilp at
  a time; the `huwilp` array can contain many. Visualization changes are
  tracked separately.
- **Performance**: the sample data has ~200 people, ~75 couples, ~15 huwilp.
  The parser uses Maps for lookups (O(log n)) which is fine at this scale.
