# Gitxsan Names, Richer Labels & Selection Overlay — Design Doc

Adds first-class **Names** to the model, enriches the tree-node **labels**, and
introduces a **detail overlay** for inspecting a single person, gated behind a
**View / Move** app mode. The on-disk file format is specified in
[`specs/json-file-format.md`](./json-file-format.md) and the parser that reads it
in [`specs/json-parser.md`](./json-parser.md); the import/export plumbing this
extends is described in
[`specs/done/import-export-feature.md`](./done/import-export-feature.md).

## Problem

- The visualization shows only a person's colonial name. It carries none of the
  other information the data already holds — Gitxsan Names, cross-Wilp Kinship
  for in-marrying spouses, or birth/death dates.
- In Gitxsan culture a **Name outlives its bearer**: Names are handed down
  through the generations of a Wilp, and one Person can hold several Names at
  once (e.g. a birth name and a chiefly name). A Name is therefore its own
  concept, not a property of a Person.
- There is no way to inspect a single person in detail — the compact label is all
  the user gets, and it cannot comfortably show every Name a person has held.

## Goals

- Model a **Name** as an entity distinct from a Person, with a many-to-many
  "holds" relationship between People and Names carrying an order (`nameDate` /
  `nameOrder`).
- Make the colonial name **optional** everywhere (file format and model), since a
  person may be recorded by their Gitxsan Name(s) alone.
- Enrich the node **label** with: the most-recent Name, cross-Wilp Kinship for
  outside spouses, and a birth/death line.
- Add a **detail overlay** for a single selected person that presents the same
  facts in an easier-to-read form and lists **all** the Names they have held.
- Introduce a **View / Move** app mode so inspection (overlays) and manipulation
  (dragging) don't fight over a single click.
- Keep **F# authoritative** for domain/view-model content: label and overlay
  facts are computed in F# and consumed by a thin TypeScript rendering layer.
- **Round-trip** names and `namesHeld` through Save/export.

## Non-goals (this iteration)

- Coining real Gitxsan Names. Sample Names are ordinary, easily-understood
  English nicknames (not based on stereotypes typically found in AI training
  data), used only to exercise the feature.
- Modeling which Wilp a Name belongs to. The file format's Name record is just an
  id and text; Wilp lineage of a Name is represented only implicitly through the
  people who have held it.
- Enforcing that no two living people hold the same Name.
- Editing Names, dates, or Kinship in-app.
- Modeling adoption _structurally_ (e.g. distinct birth-parents vs.
  adoptive-parents links, or a person appearing in two forests): this iteration
  honors only the `birthWilp` **field** to detect and display adoption, not any
  structural rework of the family graph.
- Rendering more than one Wilp at a time (unchanged).

## Terminology

- **Name** — a heritable Gitxsan name, its own record with a unique id and text.
- **Colonial name** — the Western/legal name, stored in `Person.ColonialName`
  (the file's `name`), now optional.
- **Wilp** / **Huwilp** — a matrilineal House (singular / plural).
- **Birth Wilp** — the Wilp a person was born into, which can differ from their
  current `Kinship` Wilp when they were adopted into another Wilp (the file's
  `birthWilp`).
- **Pdeeḵ** — Clan. Displayed to users in Gitxsan orthography, never as the DU
  label (see [Kinship line](#kinship-line)).

---

## Persistence changes

The on-disk JSON format is the external contract. The full schema lives in
[`specs/json-file-format.md`](./json-file-format.md), and the parser/transform
rules and complete warning list live in
[`specs/json-parser.md`](./json-parser.md). This document records the feature's
contract-level additions so the model and UX rationale remain in one place.

### `people` additions and changed fields

```json
{
  "id": 0,
  "name": "string | null",
  "wilp": "int | null",
  "birthWilp": "int | null",
  "kinshipNote": "string | null",
  "dateOfBirth": "string | null",
  "normalizedDateOfBirth": "string | null",
  "dateOfDeath": "string | null",
  "normalizedDateOfDeath": "string | null"
}
```

- `name` is the person's colonial name and is optional. A person may be recorded
  only by Gitxsan Name(s).
- `wilp` is the person's current Kinship reference.
- `birthWilp` is the Wilp the person was born into. It is meaningful when it
  differs from the person's current Kinship Wilp, which indicates adoption.
- `kinshipNote` is free-form text describing what is known when no Wilp resolves.
  If a Wilp does resolve, the note cannot be represented in `Kinship` and is
  dropped with an import warning.
- `dateOfBirth` and `dateOfDeath` are raw display fallbacks. The normalized date
  fields remain the parseable dates used by model logic; the raw values are shown
  only when their normalized counterpart is absent or unreadable.

### `names`

```json
{
  "id": 0,
  "text": "string"
}
```

A `names` row defines a Gitxsan Name. Its `id` is only a file-local reference
used by `namesHeld`; the Name's domain identity is its `text`. Two distinct ids
with equal text denote the same Name and produce a duplicate-text warning.

### `namesHeld`

```json
{
  "nameId": 0,
  "personId": 0,
  "nameDate": "string | null",
  "nameOrder": "int | null"
}
```

A `namesHeld` row records that one person holds or held one Name.

- `nameId` references `names.id`; unresolved references are dropped with a
  warning.
- `personId` references `people.id`; unresolved holders are dropped with a
  warning.
- `nameDate`, when readable, is the primary recency key for ordering one person's
  held Names. An unreadable `nameDate` is dropped and always warned about.
- `nameOrder` is the recency fallback/tiebreak. A resolved holding with neither a
  readable `nameDate` nor a `nameOrder` is kept but warned about, because its
  ordering falls back to alphabetical Name text.

A `names` row referenced by no surviving holding is an **unheld** Name. Storage is
held-only, so unheld Names are dropped on import with a warning and do not survive
an import→export round trip.

### Export and round-trip identity

Export synthesizes JSON ids for Huwilp and Names because those ids are not domain
identity. The generated `huwilp` id table covers both current Kinship
affiliations and recorded birth-Wilp affiliations. Name ids cover distinct
held-name texts, so a handed-down Name shared by several people collapses to one
`names` row. A round-tripped file is equivalent by data, not by preserving
original ids.

---

## Model changes (`Model.fs`)

### `Name` and holdings

A `Name` is a heritable Gitxsan Name. Its identity is its text: two people holding
the same text hold the same Name. There is no domain `NameId`; file ids are
consumed on read and synthesized on write.

```fsharp
type Name =
    | Name of string

type NameHeld = {
    Name: Name
    NameDate: DateOnly option
    NameOrder: int option
}
```

A `NameHeld` is held by value and does not carry the holder. The holder is the
person associated with the holding in the graph.

### `Person`

The person model carries both the current Kinship and the birth Wilp, plus raw
date fallbacks for display.

```fsharp
type Person = {
    ColonialName: string option
    Kinship: Kinship
    BirthWilp: Wilp option
    DateOfBirth: DateOnly option
    DateOfDeath: DateOnly option
    DateOfBirthText: string option
    DateOfDeathText: string option
    // other existing fields omitted
}
```

`ColonialName` is optional. `DateOfBirth` / `DateOfDeath` are normalized dates;
`DateOfBirthText` / `DateOfDeathText` preserve raw input for display fallback and
are not used for sorting.

### Birth Wilp and adoption

`Person.BirthWilp` records the named Wilp a person was born into. It is `None`
when no `birthWilp` is recorded, the reference cannot be resolved, or it resolves
only to a Pdeeḵ without a Wilp name.

A person is treated as adopted when both of these are true:

- their current Kinship is a named Wilp; and
- `BirthWilp` is known and differs from that current Wilp.

For overlay display, a known `BirthWilp` is shown whenever it differs from the
current named Wilp. If the current Kinship has no named Wilp (`UnknownWilp` or
`NoneProvided`), there is no equality match, so the recorded birth Wilp is shown.

### `Kinship` and `NoneProvided`

```fsharp
type Kinship =
    | Wilp of Wilp
    | UnknownWilp of Pdeek
    | NoneProvided of string option
```

`NoneProvided` carries an optional free-form note from `kinshipNote`. The note is
used only in the overlay's Kinship section. It never appears in the compact node
label.

### Family graph holdings

The family graph stores held Names indexed by person. Storage is held-only:
there is no standalone domain collection of unheld Names. A graph cannot contain a
Name holding for an unknown person; that is a graph-construction error rather than
silently dropped data.

Each person's holdings are exposed most-recent-first. The head of that order is
the person's most-recent Name for label/title purposes.

### Recency ordering

Names held by a person are ordered most-recent-first by three recency groups. An
earlier group is always more recent and comes before a later group:

1. **Dated** — holdings with a `nameDate`, ordered by that date descending (later
   is more recent). Equal dates tiebreak by `nameOrder` descending, with a
   present order ahead of an absent one.
2. **Order-only** — holdings with no `nameDate` but a `nameOrder`, ordered by
   `nameOrder` descending (higher is more recent).
3. **Unordered** — holdings with neither a `nameDate` nor a `nameOrder`, ordered
   alphabetically by Name text ascending.

In every group, the final tiebreak is alphabetical by Name text ascending using
an ordinal/culture-invariant comparison so .NET and the Fable/browser build agree.
The comparison is a consistent, transitive total order.

---

## Import validation and mapping rules

The importer maps decoded JSON to model records with non-fatal warnings wherever
possible. Full parser details are in
[`specs/json-parser.md`](./json-parser.md); the feature-level rules are:

- The file's `name` becomes `Person.ColonialName`.
- Raw `dateOfBirth` / `dateOfDeath` are preserved as display fallbacks; readable
  normalized dates become the model's `DateOnly` values.
- `wilp` resolves to `Kinship`. A missing Wilp reference yields
  `NoneProvided kinshipNote`; an unresolvable reference does the same and warns.
- `kinshipNote` is carried only when Kinship is `NoneProvided`. A note alongside a
  resolving Wilp is dropped with a warning.
- `birthWilp` resolves only to a named Wilp. Missing is `None`; unresolvable and
  Pdeeḵ-only references warn and become `None`.
- `names` are deduplicated by id, keeping the first row. Distinct ids with equal
  text resolve to one `Name` and warn.
- `namesHeld` rows with unresolved `nameId` or `personId` are dropped and warned.
- A present but unreadable `nameDate` is dropped and always warned about. If the
  holding then has no `nameOrder`, it is also warned as unordered.
- A resolved holding with no readable `nameDate` and no `nameOrder` is kept,
  sorted in the unordered group, and warned about.
- A `names` row referenced by no surviving holding is dropped and warned as
  unheld.
- A couple whose two members are the same person is dropped and warned. People
  naming that dropped couple as parents become roots.

---

## Label content

A node label is compact and line-oriented. Present lines are joined with `\n`;
absent lines are omitted without blank gaps.

1. **Colonial name** — the person's `ColonialName`, when present.
2. **Most-recent Name** — the head of the person's held-Name recency order, when
   present. If there is no colonial name, this becomes the first line.
3. **Kinship** — a parenthesized line shown only when the person's current
   Kinship does not match the Wilp they are rendered in. See
   [Kinship line](#kinship-line).
4. **Dates** — a single `B …` / `D …` line when at least one date is known. See
   [Dates](#dates-model--formatting).

### Kinship line

The Kinship line is gated by the rendered Wilp, not by `BirthWilp`. It appears
unless the person's current Kinship is the same named Wilp as the tree in which
the node is rendered. That single rule covers in-marrying spouses, Pdeeḵ-only
Kinship, and adopted members shown outside their current Wilp, while avoiding a
redundant line when an adopted person is shown in their current Wilp.

When the gate is open, the line's content comes from the person's current
`Kinship`:

| `Kinship`           | Line                                      |
| ------------------- | ----------------------------------------- |
| `Wilp w`            | `(B)` — the bare Wilp name in parentheses |
| `UnknownWilp pdeek` | `(Lax̱ Gibuu)` — Pdeeḵ display spelling    |
| `NoneProvided _`    | _(none — nothing to show)_                |

The `Wilp` line is omitted when the bare Wilp name would merely repeat the
most-recent Name already shown above it. The redundant parenthesized line is
dropped rather than printed twice.

Pdeeḵ values use Gitxsan orthography:

| DU         | Display     |
| ---------- | ----------- |
| `LaxGibuu` | `Lax̱ Gibuu` |
| `LaxSkiik` | `Lax̱ Skiik` |
| `Ganeda`   | `G̱aneda`    |
| `Giskaast` | `Gisḵ'aast` |

### Dates (model & formatting)

For each date, a normalized date wins over the raw text fallback:

- normalized `DateOnly` present → format it;
- otherwise raw text present → show it verbatim;
- otherwise omit the date.

Date formatting happens in the TypeScript view layer with the browser's `Intl`
API. The compact label uses short dates; the overlay uses long dates. Raw text is
shown verbatim.

Label date-line assembly is:

| DoB | DoD | Line                |
| --- | --- | ------------------- |
| ✓   | ✓   | `B <dob> - D <dod>` |
| ✓   | ✗   | `B <dob>`           |
| ✗   | ✓   | `- D <dod>`         |
| ✗   | ✗   | _(no line)_         |

---

## Detail overlay

The overlay is a card shown for exactly one selected node while the app is in View
mode. It presents the same facts as the compact label in a more readable form and
adds the rest of the person's held Names.

Empty sections are omitted with their dividers, never left as blank gaps.

### Sections (top to bottom)

1. **Title** (always present). Built from the colonial name and the most-recent
   held Name:
   - colonial only → the colonial name;
   - Gitxsan only → the most-recent Name;
   - both → `<most-recent Name> (<colonial name>)`;
   - neither → empty title text (not expected for real data).

2. **Kinship** (always shown). Current Kinship rows come first:
   - `Wilp w` → `Wilp: <w.Name>` then `Pdeeḵ: <w.Pdeek display>`;
   - `UnknownWilp pdeek` → `Pdeeḵ: <pdeek display>`;
   - `NoneProvided None` → `Kinship: Not provided`;
   - `NoneProvided (Some note)` → `Kinship: <note>`.

   When a recorded `BirthWilp` differs from the current named Wilp, append
   `Birth Wilp: <name>` and `Birth Pdeeḵ: <display>`. These rows are omitted when
   `BirthWilp` is absent or equals the current Wilp. If current Kinship has no
   named Wilp, a recorded birth Wilp is shown because there is no current Wilp to
   equal it.

3. **Dates** — `Born:` and/or `Died:` rows. Normalized dates are formatted as
   long dates per the device's region; raw date text is shown verbatim. The whole
   section is omitted if neither date is known.

4. **Other names held** — heading `Other names held:` followed by the person's
   held Names in recency order, excluding the most-recent Name already used in
   the title. The section is omitted when the person holds zero or one Name.

### Positioning

The overlay is positioned beside the selected node's edge, not the node's centre.
It prefers the right side of the node and flips left when there is not enough room.
It aligns with the node's top edge and flips upward when needed to stay inside the
canvas. The card is rendered above the canvas and node labels so it is not
occluded by scene text.

The position is projected once, when the overlay opens, and reprojected only when
the camera or canvas changes. View mode locks the camera and disables dragging, so
user input cannot move the node underneath the card — but an _animating_ node can:
selecting a node that is still travelling to a target position (after a layout, a
file open, or an undo issued just before switching modes) leaves the card behind
at the node's position as of the last projection. See
[Accepted gaps](#accepted-gaps).

### Dismissal

The overlay is dismissed by any of:

1. clicking its **"×"**;
2. clicking the empty scene/background outside any node or the overlay;
3. clicking the already-selected node, deselecting it;
4. switching app mode.

Clicking a _different_ node while an overlay is open does not dismiss it: the
selection immediately replaces the current one, and the overlay follows to the
newly selected node. Clicking inside the overlay body (except the "×") does
nothing and does not propagate to the scene/background handler.

---

## Interaction model: View / Move modes

The app has two high-level modes: **View** for inspection and **Move** for
manipulation. The mode-toggle button remains available in both modes and its label
names the mode a click switches into.

| Current mode | Button label |
| ------------ | ------------ |
| Move         | `View`       |
| View         | `Move`       |

The boot mode is **View**.

### View mode (inspection)

- Selecting a node opens its detail overlay.
- Selection is single-select.
- Nodes are not draggable.
- Select-mode, Undo, and Redo controls are hidden because they are manipulation
  controls. Their underlying state/history is untouched and reappears unchanged
  on return to Move mode.
- While an overlay is visible, camera orbit/pan/zoom is locked.
- Open and Save continue to work normally.

### Move mode (manipulation)

Move mode keeps the app's manipulation behavior: select-mode controls are
available, single- or multi-select is allowed, selected nodes can be dragged, and
no overlays are shown.

### Mode transitions

Switching modes clears selection and dismisses any overlay. Input is raised between
frames; Runner resolves each target's `EmitsIntent` from the pre-system declaration
snapshot once, and all systems share that ordered list. The `AppMode` on each click
is captured when it is raised, while later declaration mutations affect only later
snapshots and frames. A same-frame node click before the mode switch is applied
before the switch clears the selection, while one after it is interpreted under the
mode the switch just entered.

---

## Localization

All locale-dependent presentation lives in the TypeScript view layer; the F# core
stays presentation-neutral. Two concerns are separated:

- **Translatable chrome** — labels and fixed words such as `Wilp:`, `Pdeeḵ:`,
  `Born:`, `Other names held:`, `B`/`D`, and `Not provided` — comes from the
  shared localization catalog authored in F# and consumed on both sides of the
  F#/TypeScript boundary. English is the only implemented locale.
- **Date/number formatting** follows the device's regional settings via the
  browser's `Intl` API, independent of UI language.

Gitxsan data values — a specific House's/clan's name or a person's Name — are
never translated. Gitxsan words used as UI labels are chrome and belong in the
shared catalog.

---

## Architectural boundaries

The domain/view-model layer supplies presentation-neutral label and overlay data:
raw domain values, date values/fallbacks, row kinds, and show/hide decisions. The
TypeScript layer formats dates, localizes chrome, composes strings, and lays out
the card.

App mode is a single app-wide state. Controls that are meaningless in View mode
are hidden rather than disabled, and the mode switch does not mutate undo/redo
history. Overlay visibility is derived from existing world state — View mode and
exactly one selected node — instead of mirrored into a separate cached flag.

---

## Accepted gaps

- The TypeScript layer currently has no unit tests. In particular, the overlay
  visibility derivation, localization/date formatting helpers, and DOM overlay
  behavior are checked by type-checking and manual verification rather than a
  dedicated TS test suite.
- **The overlay does not follow an animating node.** Its screen position is
  projected when it opens and on camera/canvas changes only, so selecting a node
  that is still animating toward a target position leaves the card behind. It
  realigns only when something retriggers projection — a selection change, or a
  camera or canvas change — and can drift again while the node keeps moving.
  Fixing it means reprojecting while the selected node has a target position,
  without re-rendering the card every frame.
