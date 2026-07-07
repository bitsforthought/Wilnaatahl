# Wilnaatahl JSON File Format

The on-disk format for the genealogical data Wilnaatahl imports and exports. This
spec is the single source of truth for the **file schema**. The parser/transform
that reads a file into the domain model is described separately in
[`specs/json-parser.md`](./json-parser.md); the import/export flow that produces
and consumes files is in
[`specs/done/import-export-feature.md`](./done/import-export-feature.md); and the
domain model and features built on the data are in
[`specs/names-and-detail-overlay.md`](./names-and-detail-overlay.md).

## Conventions

- A file is a single JSON object with up to five top-level arrays: `people`,
  `couples`, `huwilp`, `names`, and `namesHeld`.
- Only `people` is present in every meaningful file; the other four arrays are
  optional and default to empty when their key is absent.
- **Absent equals null.** For any field typed `T | null` below, an absent key is
  treated identically to an explicit `null`.
- Records are linked by **numeric ids** unique within their own array
  (`people.id`, `couples.coupleId`, `huwilp.id`, `names.id`). An id has no meaning
  beyond referencing a record within the same file — it implies no ordering and is
  not part of the data's identity (see [Ids and round-tripping](#ids-and-round-tripping)).
- **Dates** come in two forms. `normalized*` fields hold an ISO-8601 date
  (`YYYY-MM-DD`) — the machine-readable value. The un-normalized `dateOf*` fields
  hold free-form display text (e.g. `circa 1925`), used as a display fallback when
  the normalized value is absent.

## Top-level structure

```json
{
  "people":    [ /* Person records — see below */ ],
  "couples":   [ /* Couple records */ ],
  "huwilp":    [ /* Wilp records */ ],
  "names":     [ /* Name records */ ],
  "namesHeld": [ /* NameHeld records */ ]
}
```

## `people`

```json
{
  "id": 0,
  "name": "string | null",
  "parents": "int | null",
  "wilp": "int | null",
  "birthWilp": "int | null",
  "kinshipNote": "string | null",
  "birthOrder": "int | null",
  "dateOfBirth": "string | null",
  "normalizedDateOfBirth": "string | null",
  "dateOfDeath": "string | null",
  "normalizedDateOfDeath": "string | null",
  "gender": "M | F",
  "deceased": "bool | null"
}
```

| Field                   | Type            | Meaning                                                                                                                     |
| ----------------------- | --------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `id`                    | int, required   | Unique person identity. Referenced by `couples.member1`/`member2` and `namesHeld.personId`. (Parent–child links are indirect: a person's `parents` names a couple, whose members are the parents.) |
| `name`                  | string \| null  | The person's colonial (Western/legal) name. Optional — a person may be recorded by their Gitxsan Name(s) alone.             |
| `parents`               | int \| null     | The `couples.coupleId` of the couple this person is a child of. `null` for a person with no recorded parents (a root).      |
| `wilp`                  | int \| null     | The `huwilp.id` of the person's **current** Wilp membership (their Kinship). `null` when no membership is recorded.         |
| `birthWilp`             | int \| null     | The `huwilp.id` of the Wilp the person was **born into**. Differs from `wilp` when the person was adopted into another Wilp. |
| `kinshipNote`           | string \| null  | Free-form note describing what is known about the person's Kinship. Meaningful only when no `wilp` resolves.                |
| `birthOrder`            | int \| null     | Optional sort key ordering siblings when birth dates are unavailable.                                                       |
| `dateOfBirth`           | string \| null  | Free-form display text for the birth date. Display fallback for `normalizedDateOfBirth`.                                    |
| `normalizedDateOfBirth` | string \| null  | ISO-8601 (`YYYY-MM-DD`) birth date.                                                                                         |
| `dateOfDeath`           | string \| null  | Free-form display text for the death date. Display fallback for `normalizedDateOfDeath`.                                    |
| `normalizedDateOfDeath` | string \| null  | ISO-8601 (`YYYY-MM-DD`) death date.                                                                                         |
| `gender`                | `"M"` \| `"F"`  | Required.                                                                                                                   |
| `deceased`              | bool \| null    | Whether the person is deceased.                                                                                            |

## `couples`

```json
{
  "coupleId": 0,
  "member1": 0,
  "member2": 0,
  "dateOfUnion": "string | null"
}
```

| Field         | Type           | Meaning                                                          |
| ------------- | -------------- | --------------------------------------------------------------- |
| `coupleId`    | int, required  | Unique couple identity. Referenced by `people.parents`.         |
| `member1`     | int, required  | A `people.id` — the first partner.                              |
| `member2`     | int, required  | A `people.id` — the second partner.                             |
| `dateOfUnion` | string \| null | ISO-8601 date the union was formed, when known.                 |

A couple's recorded children are the people whose `parents` reference its
`coupleId`; a couple may have no children.

## `huwilp`

Each entry describes a Wilp (matrilineal House) and the Pdeeḵ (clan) it belongs
to.

```json
{
  "id": 0,
  "name": "string | null",
  "pdeek": "string | null"
}
```

| Field   | Type           | Meaning                                                        |
| ------- | -------------- | ------------------------------------------------------------- |
| `id`    | int, required  | Unique Wilp identity. Referenced by `people.wilp` / `birthWilp`. |
| `name`  | string \| null | The Wilp's name.                                              |
| `pdeek` | string \| null | The Pdeeḵ (clan) the Wilp belongs to.                         |

There are four Pdeeḵ. Their canonical on-disk spellings are `LaxGibuu`,
`LaxSkiik`, `Ganeda`, and `Giskaast`. The `pdeek` value is matched leniently:
case, surrounding/interior whitespace, apostrophes/glottal marks, and underline
diacritics are all ignored, so `"Lax Gibuu"`, `"Gisḵ'aast"`, and `"giskaast"` are
all accepted. The exact matching rules live in
[`specs/json-parser.md`](./json-parser.md).

At least one of `name` and `pdeek` should be present for an entry to be usable; an
entry with only `pdeek` records a known clan whose specific Wilp is unknown.

## `names`

Each entry is a Gitxsan Name — a heritable name that outlives its bearer and is
handed down within a Wilp. A Name's identity is its `text`.

```json
{
  "id": 0,
  "text": "string"
}
```

| Field  | Type            | Meaning                                                 |
| ------ | --------------- | ------------------------------------------------------- |
| `id`   | int, required   | Unique Name identity within the file. Referenced by `namesHeld.nameId`. |
| `text` | string, required | The text of the Name.                                  |

## `namesHeld`

Each entry records that one person holds (or held) one Name. A person may hold
several Names at once (e.g. a birth name and a chiefly name), and — because Names
are handed down — the same Name may be held by different people across
generations.

```json
{
  "nameId": 0,
  "personId": 0,
  "nameDate": "string | null",
  "nameOrder": "int | null"
}
```

| Field       | Type           | Meaning                                                                                         |
| ----------- | -------------- | ----------------------------------------------------------------------------------------------- |
| `nameId`    | int, required  | A `names.id` — the Name being held.                                                             |
| `personId`  | int, required  | A `people.id` — the holder.                                                                     |
| `nameDate`  | string \| null | When the Name was given to this person. Used to order a person's Names (later = more recent).    |
| `nameOrder` | int \| null    | Life-order in which the Name was given; a tiebreak when `nameDate` is equal or absent.          |

At least one of `nameDate` and `nameOrder` must be present — they may not both be
null — and when `nameDate` is absent or not a valid ISO-8601 date, `nameOrder`
must be present, so a person's Names always have a well-defined order.

A `names` entry that no `namesHeld` record references is an **unheld** name — no
record of anyone ever having held it. (This differs from a name whose holders are
all deceased, which still has `namesHeld` records.)

## Cross-references

- `people.parents` → `couples.coupleId`
- `couples.member1` / `member2` → `people.id`
- `people.wilp` / `people.birthWilp` → `huwilp.id`
- `namesHeld.nameId` → `names.id`
- `namesHeld.personId` → `people.id`

How unresolved or duplicate references are handled on import (drop, warn, treat as
a root, etc.) is described in [`specs/json-parser.md`](./json-parser.md).

## Ids and round-tripping

Ids exist only to link records within a single file; they carry no meaning beyond
that and are not part of the data's identity. Export is therefore free to assign
fresh ids to any array, and round-tripping a file (import, then export) may
renumber records. A renumbered file describes the same data.
