# JSON Parser for Import Feature — Implementation Plan

## Problem

The import feature (spec: `specs/import-feature.md`) needs a parser that transforms
a JSON file of genealogical data into the `(Person * CoParentRelationship option) seq`
format consumed by `createFamilyGraph`. The JSON format uses name-based parent
references, has no Wilp/Pdeek data, and contains several field types and edge cases
the current model can't represent. This plan covers the parser design, gap analysis,
and implementation path.

## JSON schema (observed)

```json
{
  "people": [
    {
      "name": "string", // required, unique identifier
      "mother": "string | null", // name-ref to another person
      "father": "string | null", // name-ref to another person
      "birthOrder": "int | missing", // optional sort key
      "dateOfBirth": "string | null", // raw, messy format
      "normalizedDateOfBirth": "string | null", // ISO-8601 when available
      "dateOfDeath": "string | null", // raw, messy format
      "normalizedDateOfDeath": "string | null", // ISO-8601 when available
      "gender": "M | F", // required
      "deceased": "bool", // no model equivalent
      "marriedTo": "string | null" // no model equivalent
    }
  ]
}
```

## Gap analysis: JSON vs. current model

### Fields with no model equivalent (silently ignored)

| JSON field  | Reason                                                        |
| ----------- | ------------------------------------------------------------- |
| `marriedTo` | Model only tracks `CoParentRelationship` (requires children). |
| `deceased`  | Model has `DateOfDeath` but no boolean deceased flag.         |

### Structural gaps (worked around)

| Gap                            | Impact                                        | Workaround                                          |
| ------------------------------ | --------------------------------------------- | --------------------------------------------------- |
| No Wilp / Pdeek data           | Tree structure rooted by WilpName; scene      | Auto-assign Wilp from filename, Pdeek = Giskaast.   |
|                                | crashes without at least one Wilp.            | Propagate Wilp through matriline (mother chain).    |
| Name-based parent references   | Model uses integer `PersonId`.                | Build name→index; assign sequential IDs on import.  |
| Unresolvable parent references | Children reference names that don't match any | Strict exact-match; unresolvable links are dropped; |
|                                | person (typos, Unicode diacritics, suffixes). | person becomes a root. Warning emitted.             |
| Single-parent reference        | Model requires both Mother and Father for a   | Treat person as root (no `CoParentRelationship`).   |
| (one parent null)              | `CoParentRelationship`.                       | Warning emitted.                                    |
| Messy date strings             | `DateOnly` requires a valid date.             | Use `normalizedDateOfBirth/Death` when present;     |
|                                |                                               | skip unparseable raw dates. Warning emitted.        |

### Who gets the Wilp (matrilineal assignment)

The JSON has no Wilp data, so the parser infers membership using the matrilineal
convention:

1. **Identify female roots**: gender = F, no resolved parents, AND at least one child
   references them as "mother." These are the top-of-tree matriarchs.
2. **Assign the import Wilp** (derived from filename, Pdeek = Giskaast) to those roots.
3. **Propagate**: every child whose _mother_ has the Wilp inherits it (regardless of
   the child's own gender). Continue recursively through female children who themselves
   become mothers.
4. **Everyone else** (fathers, childless female roots, people whose mother link broke)
   gets `Wilp = None`.

This matches the existing test data convention where all matrilineal descendants
(sons and daughters) carry the Wilp, while co-parents from outside do not.

### NodeShape mapping

`gender = "M"` → `Cube`, `gender = "F"` → `Sphere`.

## Library choice: Thoth.Json

**Packages to add:**

| Package                 | Used when            | Purpose                    |
| ----------------------- | -------------------- | -------------------------- |
| `Thoth.Json.Core`       | Always               | Platform-agnostic decoders |
| `Thoth.Json.Newtonsoft` | .NET (tests, server) | .NET JSON backend          |
| `Thoth.Json.JavaScript` | Fable (browser)      | JS JSON backend            |

Thoth.Json provides composable, type-safe decoders with explicit error messages.
Each field decoder handles optional/null values naturally. The decoder pipeline will
parse the JSON into an intermediate `RawPerson` record, then a separate transformation
step converts to the model's `Person * CoParentRelationship option`.

## Architecture

```
JSON string
    │
    ▼
┌─────────────────────────┐
│ Thoth.Json decoder      │  JsonParser.fs
│ → RawPerson list        │
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│ Import.transform        │  Import.fs
│  • Build name→index     │
│  • Assign PersonIds     │
│  • Resolve parent refs  │
│  • Assign Wilp          │
│  • Map dates/gender     │
│  • Collect warnings     │
│ → ImportResult          │
└────────────┬────────────┘
             │
             ▼
  Result<(Person * CoParentRelationship option) seq
         * ImportWarning list,
         ImportError>
```

## Types

```fsharp
/// Intermediate type — what Thoth.Json decodes into.
type RawPerson = {
    Name: string
    Mother: string option
    Father: string option
    BirthOrder: int option
    DateOfBirth: string option           // raw
    NormalizedDateOfBirth: string option  // ISO-8601
    DateOfDeath: string option           // raw
    NormalizedDateOfDeath: string option  // ISO-8601
    Gender: string
}

/// Things that went wrong but didn't prevent import.
type ImportWarning =
    | UnresolvedMother of childName: string * motherName: string
    | UnresolvedFather of childName: string * fatherName: string
    | SingleParentDropped of childName: string * resolvedParent: string
    | UnparseableDate of personName: string * fieldName: string * rawValue: string
    | DuplicateName of name: string

/// Things that prevent import entirely.
type ImportError =
    | InvalidJson of string
    | EmptyPeopleArray
    | NoPeopleAfterFiltering

type ImportResult = {
    PeopleAndParents: (Person * CoParentRelationship option) list
    Warnings: ImportWarning list
}
```

## File layout

```
src/Wilnaatahl.Core/
  Import/
    JsonTypes.fs     — RawPerson, ImportWarning, ImportError, ImportResult
    JsonParser.fs    — Thoth.Json decoders (RawPerson decoder, top-level decoder)
    Import.fs        — transform: RawPerson list → wilpName → ImportResult | ImportError
                       (name resolution, Wilp assignment, date parsing, ID assignment)

tests/Wilnaatahl.Core.Tests/
  Import/
    JsonParserTests.fs  — decoder round-trip tests, malformed JSON, missing fields
    ImportTests.fs      — transformation logic: parent resolution, Wilp assignment,
                          date parsing, warning generation, edge cases
```

All new files added to `Wilnaatahl.Core.fsproj` `<Compile>` list and
`Wilnaatahl.Core.Tests.fsproj` respectively.

## Implementation todos (TDD — strict red/green/refactor)

Per AGENTS.md: "Write failing tests first, observe the failure, then implement.
Don't skip the red phase — it validates the test itself."

Each implementation unit follows the cycle:

1. **Red** — write tests for the next behavior; run them, confirm they fail.
2. **Green** — write the minimum implementation to make them pass.
3. **Refactor** — clean up while tests stay green.

---

### 1. `add-thoth-json` — Add Thoth.Json NuGet packages

Add `Thoth.Json.Core` unconditionally and the two platform backends conditionally:

- `Thoth.Json.Newtonsoft` when `FABLE_COMPILER != true`
- `Thoth.Json.JavaScript` when `FABLE_COMPILER == true`

Verify Fable.Core 4.5.0 compatibility. Run `dotnet restore` and `npm run fable`
to confirm both .NET and Fable compilation succeed.

No tests for this step — it's infrastructure.

### 2. `define-import-types` — Define types in JsonTypes.fs

`RawPerson`, `ImportWarning`, `ImportError`, `ImportResult` as described above.
Pure data types, no logic. No tests needed — the types are exercised by every
subsequent test.

### 3. `test-json-decoders` — Write failing decoder tests (RED)

Create `JsonParserTests.fs` with tests that call the not-yet-implemented
`parseJson` function. All tests fail (function doesn't exist or returns
`Error`).

Test cases:

- Valid minimal JSON (one person, no parents) → Ok with correct RawPerson
- Person with all optional fields populated → Ok with all fields set
- Person with all optional fields null/missing → Ok with None values
- Malformed JSON → `InvalidJson` error
- Empty people array → `EmptyPeopleArray` error
- Extra/unknown fields (e.g. `marriedTo`, `deceased`) → ignored gracefully

### 4. `impl-json-decoders` — Implement decoders to pass tests (GREEN)

Write the Thoth.Json decoders in `JsonParser.fs`:

- `Decode.object` decoder for `RawPerson` (name, mother, father, birthOrder,
  dates, gender; ignores extra fields)
- Top-level decoder: `{ "people": RawPerson list }`
- Expose `parseJson: string → Result<RawPerson list, ImportError>`

Run tests — all decoder tests should now pass. Refactor if needed.

### 5. `test-transform` — Write failing transform tests (RED)

Create `ImportTests.fs` with tests that call the not-yet-implemented
`transform` function. All tests fail.

Test cases — parent resolution:

- Two people with a parent-child link → correct CoParentRelationship
- Unresolvable mother → person becomes root, `UnresolvedMother` warning
- Unresolvable father → person becomes root, `UnresolvedFather` warning
- Single parent (one resolves, one null) → root, `SingleParentDropped` warning
- Duplicate names → first kept, `DuplicateName` warning

Test cases — Wilp assignment:

- Female root who is a mother → gets import Wilp (Giskaast)
- Male root → Wilp = None
- Female root who is NOT a mother → Wilp = None
- Child of Wilp mother → inherits Wilp (regardless of gender)
- Multi-generation propagation through mother chain
- Co-parent father → Wilp = None

Test cases — field mapping:

- Gender M → Cube, F → Sphere
- Normalized ISO date → correct DateOnly
- Missing normalized date, unparseable raw → None + `UnparseableDate` warning
- BirthOrder present → used; missing → default 0
- Sequential PersonId assignment (0, 1, 2, …)

Test cases — error conditions:

- Empty input after filtering → `NoPeopleAfterFiltering` error

### 6. `impl-transform` — Implement transform to pass tests (GREEN)

Write `Import.fs` with:

`transform: RawPerson list → string (* wilpName *) → Result<ImportResult, ImportError>`

Steps:

1. **Deduplicate names**: if duplicate names exist, keep the first, warn on rest.
2. **Build name→index**: `Map<string, int>` for O(1) parent lookups.
3. **Resolve parent references**: for each person with mother/father strings,
   look up in the name→index. Collect warnings for unresolvable refs.
4. **Determine CoParentRelationship**: only when BOTH parents resolve.
   Otherwise the person is a root (CoParentRelationship = None).
5. **Assign matrilineal Wilp**:
   - Find female roots who are mothers (gender=F, no resolved parents,
     referenced as "mother" by at least one other person).
   - Assign `{ Name = WilpName wilpName; Pdeek = Giskaast }` to those roots.
   - Propagate through mother links: every person whose resolved mother
     has the Wilp also gets it. Iterative or topological-sort traversal.
6. **Map fields**:
   - `Gender "M" → Cube, "F" → Sphere`
   - `BirthOrder → default 0 if missing`
   - `NormalizedDateOfBirth → DateOnly.Parse` (ISO-8601); if missing/
     unparseable, try raw `dateOfBirth`; if still unparseable, None + warning.
   - Same for death dates.
7. **Assign sequential PersonIds** (0, 1, 2, …).
8. **Build output**: `(Person * CoParentRelationship option) list` + warnings.
9. **Validate**: if output is empty → `Error NoPeopleAfterFiltering`.

Run tests — all transform tests should now pass. Refactor if needed.

### 7. `test-and-impl-seam` — Wire parseJsonFile + integration tests

Write integration tests that call `parseJsonFile` end-to-end (JSON string in,
model types out), then implement:

```fsharp
let parseJsonFile (json: string) (wilpName: string) =
    parseJson json
    |> Result.bind (fun rawPeople -> transform rawPeople wilpName)
    |> Result.map (fun result ->
        result.PeopleAndParents, result.Warnings)
```

Signature:
`parseJsonFile: string → string → Result<(Person * CoParentRelationship option) list * ImportWarning list, ImportError>`

This is the function the TS import service will call via Fable-generated interop.

### 8. `coverage-check` — Run coverage check

Run `npm run coverage:check` to verify line coverage doesn't regress.

## Notes & future considerations

- **Extending the model for dropped fields**: `marriedTo` (childless marriages),
  `deceased` flag, and Wilp/Pdeek per-person are all candidates for future model
  extensions. The parser already decodes the full JSON; adding support later means
  updating `Import.transform`, not the decoder.
- **Fuzzy name matching**: currently strict exact-match. If data quality issues are
  common, a future iteration could add normalized matching (strip diacritics,
  case-insensitive, etc.).
- **Multiple Wilp**: the scene currently renders only one Wilp. When multi-Wilp
  rendering is added, the import could support per-person Wilp assignment.
- **Performance**: the sample data has ~170 people. The parser uses Maps for lookups
  (O(log n)) which is fine for this scale. No special optimization needed.
