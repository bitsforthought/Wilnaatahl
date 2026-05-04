# JSON Parser for Import Feature — Implementation Plan

## Problem

The import feature (spec: `specs/import-export-feature.md`) needs a parser that transforms
a JSON file of genealogical data into the `(Person * CoupleId option) seq` and
`Couple seq` formats consumed by `createFamilyGraph`. The JSON format uses numeric
IDs for people, couples, and huwilp; per-person Wilp membership is recorded
explicitly via a `wilp` reference into a top-level `huwilp` array. This plan
covers the parser design, gap analysis, and implementation path.

## JSON schema (observed)

For any field below typed `T | null`, the key may also be absent from a record;
the parser treats an absent key as equivalent to `null`.

```json
{
  "people": [
    {
      "id": 0, // required, unique integer
      "name": "string", // required, display label
      "parents": "int | null", // CoupleId ref; null if root
      "wilp": "int | null", // ref into top-level "huwilp" by id; null if no Wilp membership
      "birthWilp": "int | null", // ignored by the parser (see Gap analysis)
      "birthOrder": "int | null", // optional sort key
      "dateOfBirth": "string | null", // display-only at source; ignored
      "normalizedDateOfBirth": "string | null", // ISO-8601; warn if not parseable
      "dateOfDeath": "string | null", // display-only at source; ignored
      "normalizedDateOfDeath": "string | null", // ISO-8601; warn if not parseable
      "gender": "M | F", // required
      "deceased": "bool" // no model equivalent
    }
  ],
  "couples": [
    {
      "coupleId": 0, // required, unique integer
      "member1": "int", // PersonId of first member
      "member2": "int", // PersonId of second member
      "dateOfUnion": "string | null" // ISO-8601 when known
    }
  ],
  "huwilp": [
    {
      "id": 0, // required, unique integer
      "name": "string | null", // Wilp name (may be unknown — see Wilp validation)
      "pdeek": "string | null" // Pdeek (Clan) name (may be unknown)
    }
  ]
}
```

## Gap analysis: JSON vs. current model

### Fields with no model equivalent (silently ignored)

| JSON field    | Reason                                                       |
| ------------- | ------------------------------------------------------------ |
| `deceased`    | Model has `DateOfDeath` but no boolean flag.                 |
| `dateOfBirth` | Display-only raw string at the source; model uses            |
|               | `normalizedDateOfBirth` for the parsed `DateOnly`.           |
| `dateOfDeath` | Display-only raw string at the source; model uses            |
|               | `normalizedDateOfDeath` for the parsed `DateOnly`.           |
| `birthWilp`   | The Wilp a person was born into (which may differ from their |
|               | current Wilp after adoption). The model has no concept of    |
|               | birth-Wilp yet; ignore for now.                              |

### Structural gaps (worked around)

| Gap                               | Impact                                         | Workaround                                               |
| --------------------------------- | ---------------------------------------------- | -------------------------------------------------------- |
| Unresolvable CoupleId on person   | Person references a couple not in the file.    | Person becomes a root. `UnresolvedCoupleId` warning.     |
| Unresolvable PersonId on couple   | Couple references a person not in the file.    | Couple dropped. `UnresolvedMember` warning.              |
|                                   | Persons referencing that couple become roots.  |                                                          |
| Unresolvable WilpId on person     | Person references a Wilp id not in `huwilp`,   | Person's `Kinship` becomes `NoneProvided`.               |
|                                   | or one that was ignored due to missing fields. | `UnresolvedWilpId` warning.                              |
| Duplicate person `id`             | JSON id is the unique key; duplicates would    | Keep first occurrence. `DuplicatePersonId` warning.      |
|                                   | give two `Person` records the same `PersonId`. |                                                          |
| Duplicate `coupleId`              | Same as above for couple lookups.              | Keep first occurrence. `DuplicateCoupleId` warning.      |
| Duplicate huwilp `id`             | Same as above for Wilp lookups.                | Keep first occurrence. `DuplicateWilpId` warning.        |
| Messy date strings (person)       | `DateOnly` requires a valid date.              | Use `normalizedDateOfBirth/Death` when present; warn if  |
|                                   |                                                | not ISO 8601. The raw `dateOfBirth/Death` are not used.  |
| Messy date string (`dateOfUnion`) | `DateOnly` requires a valid date.              | Skip unparseable values. `UnparsableCoupleDate` warning. |

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

`pdeek` strings are matched case-insensitively after collapsing whitespace.
Accepted spellings: `"LaxGibuu"`/`"Lax Gibuu"` → `LaxGibuu`,
`"LaxSkiik"`/`"Lax Skiik"` → `LaxSkiik`, `"Ganeda"` → `Ganeda`,
`"Giskaast"` → `Giskaast`. Any other value yields `UnknownPdeek`.

### Per-person Wilp resolution

For each person:

- `wilp` absent or `null` → `Kinship = NoneProvided` (no warning — this is
  normal for people whose Wilp isn't known).
- `wilp = id`, `id` resolves to a usable entry → `Kinship = Wilp w` or
  `Kinship = UnknownWilp p` per the validation table above.
- `wilp = id`, `id` not in `huwilp` (or in `huwilp` but dropped due to
  validation failure) → `Kinship = NoneProvided` plus an `UnresolvedWilpId`
  warning.

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
parses the JSON into intermediate `RawPerson`, `RawCouple` and `RawWilp` records,
then a separate transformation step converts to the model's
`Person * CoupleId option` and `Couple`.

## Architecture

```
JSON string
    │
    ▼
┌─────────────────────────┐
│ Thoth.Json decoder      │  JsonParser.fs (module internal)
│ → RawFile               │  Result<RawFile, string>
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│ Transform.transform     │  Transform.fs (let internal)
│  • Reject empty input   │  Result<ImportResult, ImportError>
│  • Deduplicate by id    │
│  • Validate huwilp      │
│  • Validate couple mbrs │
│  • Resolve parent refs  │
│  • Resolve wilp refs    │
│  • Map dates/gender     │
│  • Build Couple list    │
│  • Collect warnings     │
└────────────┬────────────┘
             │
             ▼
  Transform.fromJson: string → Result<ImportResult, ImportError>
  (the public entry point — composes parseJson ↦ mapError ↦ bind transform)
```

## Types

```fsharp
/// Intermediate type — what Thoth.Json decodes into for a person.
type RawPerson = {
    Id: int
    Name: string
    Parents: int option              // CoupleId reference; None if root
    Wilp: int option                 // Wilp reference into the huwilp array
    BirthOrder: int option
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

/// Top-level decoded file contents.
type RawFile = {
    People: RawPerson list
    Couples: RawCouple list
    Huwilp: RawWilp list
}

/// Things that went wrong but didn't prevent import.
type ImportWarning =
    | UnresolvedCoupleId of personName: string * coupleId: int
    | UnresolvedMember of coupleId: int * memberId: int
    | UnresolvedWilpId of personName: string * wilpId: int
    | UnparseableDate of personName: string * fieldName: string * rawValue: string
    | UnparsableCoupleDate of coupleId: int * rawValue: string
    | DuplicatePersonId of id: int
    | DuplicateCoupleId of id: int
    | DuplicateWilpId of id: int
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
    Warnings: ImportWarning list
}
```

## File layout

```
src/Wilnaatahl.Core/
  Import/
    JsonParser.fs   — module internal: Raw{Person,Couple,Wilp,File} types and
                      Thoth.Json decoders; parseJson: string → Result<RawFile, string>
    Transform.fs    — namespace-level ImportWarning, ImportError, ImportResult;
                      internal transform: RawFile → Result<ImportResult, ImportError>
                      (semantic validation, id resolution, Wilp lookup, date parsing,
                      Couple construction); public Transform.fromJson composer

tests/Wilnaatahl.Core.Tests/
  Import/
    JsonParserTests.fs  — decoder round-trip tests, malformed JSON, missing fields
    ImportTests.fs      — transformation logic: parent resolution, Wilp resolution,
                          date parsing, warning generation, edge cases
```

All new files added to `Wilnaatahl.Core.fsproj` `<Compile>` list and
`Wilnaatahl.Core.Tests.fsproj` respectively.

## Notes & future considerations

- **`deceased` flag**: the model has `DateOfDeath` but no boolean flag. The
  decoder already ignores `deceased` gracefully; future model work could add a
  flag and a corresponding decode step.
- **`birthWilp`**: a person's birth Wilp can differ from their current Wilp
  (e.g. after adoption). The current model only records a single `Kinship`,
  so the decoder silently ignores `birthWilp`. Capturing it will require a
  model extension.
- **Multiple Wilp visualization**: the scene currently renders only one Wilp at
  a time; the `huwilp` array can contain many. Visualization changes are
  tracked separately.
- **Performance**: the sample data has ~200 people, ~75 couples, ~15 huwilp.
  The parser uses Maps for lookups (O(log n)) which is fine at this scale.
