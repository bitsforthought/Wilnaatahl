# Gitxsan Names, Richer Labels & Selection Overlay — Design Doc

Adds first-class **Names** to the model, enriches the tree-node **labels**, and
introduces a **detail overlay** for inspecting a single person, gated behind a
new **View / Move** app mode. The on-disk file format is specified in
[`specs/json-file-format.md`](./json-file-format.md) and the parser that reads it
in [`specs/json-parser.md`](./json-parser.md); the import/export plumbing this
extends is described in
[`specs/done/import-export-feature.md`](./done/import-export-feature.md).

## Problem

- The visualization shows only a person's colonial name. It carries none of the
  other information the data already holds — Gitxsan Names, cross-Wilp
  Kinship for in-marrying spouses, or birth/death dates.
- In Gitxsan culture a **Name outlives its bearer**: Names are handed down
  through the generations of a Wilp, and one Person can hold several Names at
  once (e.g. a birth name and a chiefly name). A Name is therefore its own
  concept, not a property of a Person.
- There is no way to inspect a single person in detail — the compact label is
  all the user gets, and it cannot comfortably show every Name a person has
  held.

## Goals

- Model a **Name** as an entity distinct from a Person, with a many-to-many
  "holds" relationship between People and Names carrying an order (`nameDate` /
  `nameOrder`).
- Make the colonial name **optional** everywhere (file format and model), since
  a person may be recorded by their Gitxsan Name(s) alone.
- Enrich the node **label** with: the most-recent Name, cross-Wilp Kinship
  for outside spouses, and a birth/death line.
- Add a **detail overlay** for a single selected person that presents the same
  facts in an easier-to-read form and lists **all** the Names they have held.
- Introduce a **View / Move** app mode so inspection (overlays) and manipulation
  (dragging) don't fight over a single click.
- Keep **F# authoritative**: all label/overlay content is computed in F# and
  consumed by a thin TypeScript rendering layer.
- **Round-trip** names & namesHeld through Save (`Transform.toJson`).

## Non-goals (this iteration)

- Coining real Gitxsan Names. Sample Names are ordinary, easily-understood
  English nicknames (not based on stereotypes typically found in AI training
  data), used only to exercise the feature.
- Modeling which Wilp a Name belongs to. The file format's Name record is just
  an id and text; Wilp lineage of a Name is represented only implicitly through
  the people who have held it.
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
- **Pdeeḵ** — Clan. Displayed to users in Gitxsan orthography, never as the DU
  label (see [Kinship text](#kinship-line)).

---

## Persistence changes

This feature extends the on-disk JSON format with the `names` and `namesHeld`
arrays and the person-level `birthWilp` and `kinshipNote` fields, makes `name`
optional, and retains the raw `dateOfBirth` / `dateOfDeath` strings. The full
schema is in [`specs/json-file-format.md`](./json-file-format.md), and the
parser/transform that reads and writes it — exact `Raw*` record shapes,
decoders/encoders, resolution rules, and the complete warning list — is
specified in [`specs/json-parser.md`](./json-parser.md). The persistence-code
deltas this feature introduces are:

- **Contracts / reader** (`JsonContracts.fs`, `JsonReader.fs`): `RawPerson.Name`
  becomes `string option`; add `RawPerson` fields `RawDateOfBirth`,
  `RawDateOfDeath`, `KinshipNote`, and `BirthWilp`; add `RawName` and
  `RawNameHeld` records; `RawFile` gains optional `Names` and `NamesHeld` arrays
  (absent → empty, like `couples`).
- **Transform** (`Transform.fs`): field mapping, Kinship-note / birth-Wilp / name
  resolution, and the new warnings — see [Transform](#transform-transformfs)
  below.
- **Writer / export** (`JsonWriter.fs`, `Transform.toJson`): emit `name` /
  `dateOfBirth` / `dateOfDeath` only when present; write `kinshipNote` only for
  `NoneProvided (Some note)`; write `birthWilp` only when `BirthWilp` is `Some`,
  with the synthesized `huwilp` id map covering the **union** of `Kinship` and
  `BirthWilp` Wilps; add the `names` / `namesHeld` arrays, synthesizing `names`
  ids the way `toJson` already synthesizes `huwilp` ids (distinct held-name texts
  → ids, shared/handed-down names collapsing to one). Round-trip is **held-only**:
  unheld names are dropped on import (with a warning) and so do not survive a
  graph→JSON→graph round-trip. Per
  [`specs/json-file-format.md`](./json-file-format.md) ids are not part of the
  data's identity, so a round-tripped file is equivalent by **data**, not by id.

---

## Model changes (`Model.fs`)

### `Name` and holdings

**A `Name`'s identity is its text.** A Name is a heritable Gitxsan name that
outlives its bearer and is handed down within a Wilp, so "the same name" means
"the same text" — two people holding the same text hold the _same_ Name. The
numeric ids in the file format therefore carry **no domain meaning**; they exist
only to let `namesHeld` reference `names` on disk. So the domain has **no
`NameId` type** — ids are consumed on read (to resolve holdings) and _synthesized_
on write (like the huwilp ids `toJson` already fabricates; see
[Persistence changes](#persistence-changes)).

```fsharp
/// A heritable Gitxsan Name. Its identity *is* its text: two people holding the
/// same text hold the same Name. Not a property of a Person — a Person reaches
/// its Names only through the graph's holdings. (No Wilp lineage is modeled —
/// see this doc's non-goals.)
#if FABLE_COMPILER
[<Erase>]
#endif
type Name =
    | Name of string

    member this.AsString =
        let (Name text) = this
        text

/// One Person's holding of one Name, carrying the life-order in which it was
/// given. The Name is held *by value* (identity is its text, so value and
/// reference coincide) — no id indirection. NameDate is raw text used only for
/// recency ordering, never displayed; NameOrder is the fallback tiebreak,
/// analogous to Person.BirthOrder. Which Person holds it is supplied by
/// context (the holding is stored keyed by PersonId), so it is not a field here.
type NameHeld = {
    Name: Name
    NameDate: string option
    NameOrder: int option
}
```

### `Person`

- **Rename `Label` → `ColonialName`** (`string option`, already optional in the
  model) to reflect its narrowed role now that Gitxsan Names are modeled
  separately. This touches `Person.Empty`, `Transform.fs`, `Initial`, and the
  TypeScript reader of `person.Label` (see [TS wiring](#typescript-wiring)).
- Add raw date fallbacks, kept alongside the parsed `DateOnly` used for layout
  sorting:

```fsharp
type Person = {
    // ...existing fields...
    Kinship: Kinship                  // current Wilp/Pdeek (see below)
    BirthWilp: Wilp option            // NEW: the Wilp born into, when known
    DateOfBirth: DateOnly option      // normalized/parsed — layout sorting (unchanged)
    DateOfDeath: DateOnly option      // normalized/parsed — layout sorting (unchanged)
    DateOfBirthText: string option    // NEW: raw fallback, display only
    DateOfDeathText: string option    // NEW: raw fallback, display only
}
```

`Person.Empty` gains `BirthWilp = None; DateOfBirthText = None; DateOfDeathText = None`.

### Birth Wilp (adoption)

`Person.BirthWilp : Wilp option` records the Wilp a person was **born into**,
resolved from the file's `birthWilp` reference. It is `None` when no `birthWilp`
is recorded. A person is treated as **adopted** when their current Kinship Wilp
and their `BirthWilp` are both known and differ.

The type is `Wilp option` (a fully-named Wilp), deliberately **not** the full
`Kinship`: a birth Wilp is never "NoneProvided with a note", and both consumers
want a concrete Wilp — the label compares it against the current Wilp (structural
`Wilp` equality) and the overlay displays its name and Pdeeḵ. A `birthWilp`
reference that resolves only to a Pdeeḵ (a name-less `huwilp` entry) cannot be
represented as a `Wilp`, so it is dropped to `None` with a warning (see
[Transform](#transform-transformfs)).

### `Kinship` (the `NoneProvided` note)

`NoneProvided` gains an optional free-form note capturing whatever is known about
a person's Kinship when no structured Wilp/Pdeek is available:

```fsharp
type Kinship =
    | Wilp of Wilp
    | UnknownWilp of Pdeek
    | NoneProvided of string option   // NEW: optional free-form Kinship note
```

Consequences: every `NoneProvided` match site becomes `NoneProvided _` /
`NoneProvided note`, and `Kinship.Pdeek` (the member returning `Pdeek option`)
matches `NoneProvided _ -> None`. The note is populated from the file's
`kinshipNote` (see [Transform](#transform-transformfs)) and rendered only in the
overlay's `Kinship` section (see [Detail overlay](#detail-overlay)); it never
appears in the compact node label.

### `FamilyGraph`

The graph gains the **held** Names so the view model can build labels and
overlays without threading extra arguments through spawning. Storage is
**held-only** — a name is represented solely through the people who hold it:

- Store the holdings indexed for lookup by person:
  `NamesHeldByPersonId: Map<int, NameHeld list>`. There is **no** standalone
  `Names` collection — names in the file that nobody holds (unheld names) are
  not represented (they are warned about on import; see
  [Transform](#transform-transformfs)).
- `createFamilyGraph` gains a holdings parameter (e.g. `(PersonId * NameHeld) seq`)
  and validates that each holding's `PersonId` resolves to a known person —
  **fail-fast**, mirroring the existing couple-member validation. (There is no
  name-side id to validate; the `Name` is already a value.) It groups the
  holdings into `NamesHeldByPersonId`.
- New accessors: `namesHeldBy : PersonId -> FamilyGraph -> NameHeld list`
  returning the person's names already sorted **most-recent-first** (each
  `NameHeld` carries its `Name`, so no pairing is needed), and an iterator over
  all holdings for the writer.

### Recency ordering

For "most recent Name" (label) and the full ordering (overlay), Names held by a
person are ordered by:

1. **`nameDate` descending** — later date = more recent. A `nameDate` that
   parses as a date sorts after any that don't parse and after `None`.
2. **`nameOrder` descending** as the tiebreak when dates are equal or absent —
   higher order = more recent.

This mirrors the Person sort (normalized `DateOfBirth` primary, `BirthOrder`
fallback). "Most recent" is the head of that ordering.

---

## Transform (`Transform.fs`)

The transform maps the decoded `RawFile` to model records. The deltas this feature
adds (full resolution rules and the complete `ImportWarning` list live in
[`specs/json-parser.md`](./json-parser.md)):

- `RawPerson.Name` → `Person.ColonialName`; raw `dateOfBirth` / `dateOfDeath` →
  `Person.DateOfBirthText` / `DateOfDeathText`.
- `kinshipNote` populates `Kinship.NoneProvided` when no Wilp resolves; a note
  present alongside a **resolving** Wilp is dropped (`IgnoredKinshipNote`).
- `birthWilp` resolves to `Person.BirthWilp : Wilp option` — a **named** Wilp
  only (`UnresolvedBirthWilpId` / `BirthWilpNotNamed` otherwise).
- `names` are deduplicated by id (`DuplicateNameId`); a `Name`'s identity is its
  text (distinct ids with equal text resolve to one `Name` but emit a
  `DuplicateNameText` warning). `namesHeld` rows resolve to
  `(PersonId, NameHeld)` pairs (`UnresolvedNameId` / `UnresolvedNameHolder` drop
  a bad row); a name held by nobody is dropped (`UnheldName`).
- `ImportResult` gains the resolved holdings as `(PersonId * NameHeld) list`,
  which `ImportService.importJsonText` passes to `createFamilyGraph`. The new
  warning cases get `toMessage` / `summary` handling in
  `ViewModel/ImportMessages.fs`.

---

## Label content (View model)

### Where it is built

The label content is a function of the **Person's own data** — colonial name,
held Names, dates, and the `Kinship` line text — plus a single per-node boolean:
whether the person's **current Wilp differs from the Wilp they are rendered in**,
which gates the `Kinship` line. That boolean is computed at spawn by comparison —
the name of `Person.Kinship`'s Wilp is not `renderedWilpName` — where
`renderedWilpName` is the scene's chosen Wilp (`firstWilp`), already in hand in
`spawnScene`. The rendered Wilp is used **only** to compute this gate, **never**
as label content (see [Kinship](#kinship-line)). The person's held Names come
from `namesHeldBy` on the graph, also available at spawn.

Because the same Person can be rendered in a Wilp that is or isn't their current
one, the composed label varies per node, so it is stored on the node.

Design: a `ViewModel` function

```fsharp
NodeLabel.build : Person -> NameHeld list -> currentWilpDiffersFromRendered: bool -> string
```

produces the multi-line label string, computed once when the node is spawned
(`Entities/People.spawnTreeNode`) and stored on the node in a **new view
trait** so the TypeScript layer renders it directly instead of reading
`person.ColonialName`:

```fsharp
// Traits/ViewTraits.fs (or PeopleTraits.fs)
let NodeLabel = valueTrait {| text = "" |}
```

`TreeNodeMesh.tsx` reads `NodeLabel.text` instead of `defaultArg(person.ColonialName,…)`.

### Label lines (top to bottom)

Built by concatenating the present lines with `\n`; absent lines are omitted (no
blank lines):

1. **Colonial name** — `Person.ColonialName` if `Some`.
2. **Most-recent Name** — the head of the recency ordering, if the person holds
   any Names.
   - If there is no colonial name, the Name becomes the first line.
3. **Kinship** — shown when the person's **current Wilp differs from the
   rendered Wilp** (the `currentWilpDiffersFromRendered` gate), in parentheses;
   the content comes purely from the person's `Kinship`. This covers both an
   in-marrying spouse and an adopted member whose current Wilp isn't the one
   they're shown in. See [below](#kinship-line).
4. **Dates** — a single `B …`/`D …` line. See [Dates](#dates-model--formatting).

### Kinship line

**Gate.** The line is shown only when `currentWilpDiffersFromRendered` is true.
That gate is computed once at spawn (in `spawnScene`) by comparison — the name of
`Person.Kinship`'s Wilp is **not** `renderedWilpName`, the scene's chosen Wilp
(`firstWilp`). The rendered Wilp is used **only** for this gate, never as
content. One comparison covers both cases the requirement calls out:

- an **in-marrying spouse** from another Wilp (their `Kinship` Wilp isn't the
  rendered one); and
- an **adopted** member whose current `Kinship` Wilp differs from the Wilp
  they're rendered in.

Crucially, the line is **never** shown when the current Wilp _equals_ the
rendered Wilp — so an adopted person shown in their own current Wilp `A` does
**not** get a redundant `(A)` line. This is why the gate compares against the
rendered Wilp rather than against `BirthWilp` directly: comparing to
`BirthWilp` would wrongly light up an adopted member who is rendered in their
current Wilp. Note this means **`BirthWilp` is not consulted when building the
label** — an adopted-out member's current Wilp already differs from the rendered
Wilp, so the same comparison shows them; `BirthWilp` drives only the overlay.
(The comparison also lights up a blood descendant recorded with a _different
displayable_ Wilp; with adoption now a first-class concept that is the intended
behavior, not the messy edge it was previously framed as.)

**Content.** When the gate is true, the parenthesized text comes purely from the
person's `Kinship` (given the gate, `Kinship`'s Wilp name is never
`renderedWilpName`):

| `Kinship`           | Line                                       |
| ------------------- | ------------------------------------------ |
| `Wilp w`            | `(B)` — the bare Wilp name in parentheses  |
| `UnknownWilp pdeek` | `(Lax̲ Gibuu)` — Pdeeḵ **display** spelling |
| `NoneProvided`      | _(none — nothing to show)_                 |

Pdeeḵ is rendered in Gitxsan orthography via a new
`Pdeek.displayName` mapping (NOT the DU label):

| DU         | Display     |
| ---------- | ----------- |
| `LaxGibuu` | `Lax̲ Gibuu` |
| `LaxSkiik` | `Lax̲ Skiik` |
| `Ganeda`   | `G̱aneda`    |
| `Giskaast` | `Gisḵ'aast` |

(These carry underline diacritics / apostrophes; keep the source file UTF-8.)

In the current single-Wilp layout the people whose current Wilp differs from the
rendered one are in-marrying spouses and adopted-out members; both are covered by
the one comparison above.

### Dates (model & formatting)

The displayed value per date is the **normalized** date when present, else the
**raw** text:

- `DateOfBirth : DateOnly option` present → format.
- else `DateOfBirthText : string option` present → show verbatim.
- else nothing.

**Label** date formatting:

- Normalized → `YYYY/MM/DD`.
- Raw → the raw string verbatim (may be arbitrary text like `circa 1925`).

**Label** date line assembly (either side may be missing):

| DoB | DoD | Line                |
| --- | --- | ------------------- |
| ✓   | ✓   | `B <dob> - D <dod>` |
| ✓   | ✗   | `B <dob>`           |
| ✗   | ✓   | `- D <dod>`         |
| ✗   | ✗   | _(no line)_         |

---

## Detail overlay

A rectangular card that appears next to a single selected node while in **View
mode**, presenting the same facts as the label in an easier-to-read form plus
**every** Name the person has held.

### Content (F#-provided)

The overlay's data is provided by a F# view-model builder (analogous to
`NodeLabel.build`) so TypeScript stays a pure renderer:

```fsharp
type NodeDetail = {
    Title: string                 // see "Title" below — always present
    Kinship: string list          // 1–3 rendered rows; see "Kinship" section
    Born: string option           // "Born: <date>" value, formatted per below
    Died: string option           // "Died: <date>" value
    OtherNames: string list       // held Names minus the most-recent, most-recent-first
}
NodeDetail.build : Person -> NameHeld list -> NodeDetail
```

Note `NodeDetail.build` takes **no gate boolean** — unlike the label, the overlay
renders the `Kinship` section **unconditionally** (every person has a `Kinship`,
and the birth-Wilp rows depend only on `Person.BirthWilp`, both carried by
`Person`). `NameHeld list` is the person's names, most-recent-first (from
`namesHeldBy`).

**Computed on demand, not stored.** Unlike `NodeLabel` — which every visible
node needs every frame and so is stored as a per-node trait — the overlay exists
for at most **one** selected node at a time. `NodeDetail` is therefore computed
lazily for the selected node when the overlay renders, and **not** persisted on
any entity. This keeps the single, rarely-shown detail off the per-node trait
set and means there is nothing to invalidate: an open overlay simply reflects the
latest `Person`/Names on its next render (relevant once editing lands).

### Sections (top to bottom)

Rendered as a titled card with thin dividers between sections; any empty section
(and its divider) is omitted, never left as a blank gap. Styling is a
theme-aware neutral surface (not the toasts' alert colors), light text, ~8px
radius, the toast shadow, ~18em wide — see [Positioning](#positioning).

1. **Title** (the card header, always present, bold). From the colonial name and
   the most-recent held Name:
   - colonial only → the colonial name (e.g. `Margaret Ashford`).
   - Gitxsan only → the most-recent Name (e.g. `The Mayor`).
   - both → `<most-recent Name> (<colonial name>)` (e.g.
     `The Mayor (Margaret Ashford)`).
   - (A person with neither is not expected; the header would be empty.)

2. **Kinship** (always shown). First the current Kinship rows, rendered from
   `Kinship`:
   - `Wilp w` → two rows: `Wilp: <w.Name>` then `Pdeeḵ: <w.Pdeek display>`.
   - `UnknownWilp pdeek` → one row: `Pdeeḵ: <pdeek display>` (no Wilp row).
   - `NoneProvided None` → one row: `Kinship: Unknown`.
   - `NoneProvided (Some note)` → one row: `Kinship: <note>`.

   Then, **when `Person.BirthWilp` is `Some bw` and `bw` differs from the current
   Kinship Wilp** (i.e. the person was adopted), two more rows are appended
   **below** the current-Kinship rows: `Birth Wilp: <bw.Name>` then
   `Birth Pdeeḵ: <bw.Pdeek display>`. They are omitted when `BirthWilp` is absent
   or equals the current Wilp (`bw` is compared against `Kinship`'s Wilp only —
   an `UnknownWilp`/`NoneProvided` current Kinship never equals a named `bw`, so
   the rows show).

   Pdeek values use the `Pdeek.displayName` mapping (`Lax̲ Gibuu` / `Lax̲ Skiik`
   / `G̱aneda` / `Gisḵ'aast`); the current-Kinship row **label** is the word
   `Pdeeḵ`, the birth rows `Birth Wilp` / `Birth Pdeeḵ`.

3. **Dates** — `Born:` / `Died:` **labeled rows** using **long localized dates**
   when normalized (`Born: March 10, 1925`, `Died: July 22, 1980`), or the raw
   text verbatim otherwise (`Born: circa 1925`). Each row appears only if that
   date exists; the whole section is omitted if neither does.

4. **Other names held** — heading `Other names held:` followed by the person's
   held Names in **reverse chronological order** (most-recent-first), **excluding
   the most-recent Name** (already in the title), one per line, no per-name
   dates. Omitted entirely when the person holds ≤ 1 Name.

A dismiss **"×"** sits in the upper-right corner, styled like the existing
error/warning toast dismiss button (`App.tsx` `dismissButtonStyle`).

Example (colonial + three Gitxsan names, adopted from Wilp B into Wilp A, both
dates):

```
┌────────────────────────────────────────┐
│  The Mayor (Margaret Ashford)       ✕   │
│  ──────────────────────────────────────  │
│  Wilp:        A                         │
│  Pdeeḵ:       Gisḵ'aast                  │
│  Birth Wilp:  B                         │
│  Birth Pdeeḵ: G̱aneda                     │
│  ──────────────────────────────────────  │
│  Born:   April 17, 1932                 │
│  Died:   March 2, 2011                  │
│  ──────────────────────────────────────  │
│  Other names held:                      │
│    Lefty                                │
│    Doc                                  │
└────────────────────────────────────────┘
```

(The `Birth Wilp` / `Birth Pdeeḵ` rows are present only because `BirthWilp` is
recorded; a non-adopted person omits them.)

### Positioning

Computed **once**, at selection time, by projecting the node's world position to
canvas pixel coordinates via the R3F camera. Because View mode locks the camera
and disables dragging (below), the node cannot move while the overlay is up, so a
once-computed fixed position never goes stale.

- **Horizontal.** Placed to the **right** of the node by default; flipped to the
  **left** when there isn't room on the right — i.e. when
  `nodeScreenX + gap + overlayWidth + margin > canvasWidth`.
- **Vertical.** The **top** of the overlay is aligned with the **top** of the
  node by default; flipped so the **bottom** of the overlay aligns with the
  **bottom** of the node when the node is too near the canvas bottom (i.e. when a
  top-aligned overlay would extend past the bottom edge).

**Width strategy — fixed `em` width with guards (suggested; tune during
implementation based on how it looks).** A known width keeps the horizontal flip
a pure calculation with no DOM measurement or two-pass render (an auto-width card
isn't measurable until laid out, risking a placement flicker). To keep a
fixed width from misbehaving on real content:

- `em`/`rem` width (not `px`) so it scales with browser zoom / OS text size.
- `max-width: min(<fixed>, ~90vw)` so it never overflows a narrow canvas on both
  sides.
- `overflow-wrap: break-word` so a long unbroken token can't spill past the edge.
- A `max-height` with `overflow-y: auto`, since the free-form `kinshipNote`
  (plus a long "other names held" list) is unbounded and can otherwise run off
  the bottom of the canvas.

The exact width, `gap`, and `margin` values are by-eye tuning to be settled
during implementation.

---

## Interaction model: View / Move modes

A **second** mode toolbar button joins the existing select-mode button. Its
label reflects the mode it switches _into_ (matching the select-mode button
convention where the label shows the state you move into on click):

| Current mode | Button label |
| ------------ | ------------ |
| Move         | `View`       |
| View         | `Move`       |

**Default at boot: View mode** (button reads `Move`).

### View mode (inspection)

- The **select-mode** button is **disabled**.
- Selection is forced to **single-select**.
- Selecting a node brings up its **detail overlay**.
- **No dragging** — nodes are never draggable.
- While an overlay is visible, interaction is **locked to the overlay**:
  - No camera orbit / pan / zoom.
  - Clicking another node does **not** select it — it dismisses the overlay
    (see dismissal).
  - **Undo / Redo** toolbar buttons are **temporarily disabled** (without
    touching undo/redo history / stacks).
  - **Open file…** and **Save** work normally and do not affect the overlay.

### Move mode (manipulation — today's behavior)

- Behaves exactly as the app does today: select-mode button enabled;
  single- or multi-select; selection enables dragging; **no overlays**.

### Mode & overlay transitions

- Switching **View → Move** or **Move → View** dismisses any overlay and clears
  selection (entering a mode starts clean).
- Changing **select mode** (only possible in Move mode) dismisses any overlay —
  moot in practice since overlays only exist in View mode, but stated for
  completeness.

### Overlay dismissal

The overlay is dismissed by any of:

1. Clicking its **"×"**.
2. Clicking **anywhere in the scene outside the overlay** (including on another
   node — which is _not_ selected as a result).
3. Switching app mode (View ↔ Move).

Clicking **inside the overlay body** (anywhere but the "×") does nothing and
does **not** propagate to the scene/background handler.

---

## F# / ECS wiring

### New mode button (a `ViewMode` system, wired into `Runner.fs`)

Following `Selection.fs`'s `SelectModeButton` pattern:

- A `ViewModeButton = valueTrait {| viewMode = true |}` and a
  `spawnViewModeControls (sortOrder, world)` added to the `spawnControls` chain
  in `EntityLifeCycle.fs` (placed relative to the select-mode button).
- A `handleViewModeButtonClick` toggles the mode, updates the button label to
  the mode-you-enter-next, and clears selection (`world.RemoveAll Selected`),
  mirroring the select-mode toggle. Boot state is **View** (button label
  `Move`).
- The **select-mode button's `disabled`** is driven from the current mode
  (disabled in View mode).

### Overlay-visible state and Undo/Redo gating

- "Overlay visible" is derivable from world state: **View mode AND exactly one
  `Selected` node**. It is represented explicitly (e.g. a world tag the
  ViewMode/Selection system maintains) so both TypeScript (whether to render the
  overlay) and F# (Undo/Redo gating) read one source of truth.
- `UndoRedo.updateButtonState` additionally forces `disabled = true` while the
  overlay is visible, **without** popping/pushing either stack, so history is
  untouched and the buttons restore to their stack-derived enabled state once
  the overlay closes.

### Selection changes

- `Selection.fs` respects the mode: in View mode it stays single-select and does
  not enable dragging (dragging is already `Selected`-driven; the drag path
  simply won't fire because View mode won't spawn DragControls around nodes — see
  TS wiring). Background/other-node clicks in View mode dismiss the overlay via
  the standard `PointerMissedEvent` / node `ClickEvent` handling; the node is not
  added to `Selected` while an overlay is open.

_(The exact split of responsibilities between the Selection and ViewMode systems
is an implementation detail to be settled during TDD; the invariants above are
the contract.)_

## TypeScript wiring

- **`TreeNodeMesh.tsx`** renders `NodeLabel.text` (new trait) instead of
  `person.ColonialName`.
- **`HuwilpGroup.tsx`** wraps nodes in `DragControls` **only in Move mode**; in
  View mode all nodes render as static meshes (no drag).
- **`TreeScene.tsx`** disables `OrbitControls` while an overlay is visible (extend
  the existing `enabled={!isDragInProgress}` to also account for overlay state).
- **New `DetailOverlay.tsx`** renders the `NodeDetail` for the single selected
  node when the overlay-visible state holds. It stops click propagation on its
  body, exposes the "×", and is absolutely positioned per
  [Positioning](#positioning). It reuses the toast **dismiss-button** style and
  shadow but on a **neutral** (non-alert) surface — see
  [Sections](#sections-top-to-bottom).
- **`Toolbar.tsx`** already renders every `Button` entity generically, so the new
  View/Move button and the mode-driven `disabled` states need no bespoke wiring.

## Sample data

Sample **Names** — ordinary, easily-understood **English nicknames** that are
**non-humorous, non-stereotypical, and not Indigenous-adjacent** (no attempt at
real Gitxsan; avoid nature/animal/"spirit" tropes — prefer neutral
occupational/trait handles) — are added to **both**:

- **`Model.fs` `Initial`** (the boot demo), via new `name`/`nameHeld`-style
  seed helpers, so the feature is exercised on first paint. A few people should
  hold **multiple** Names (to exercise most-recent selection and the "other
  names" list), some with `nameDate`/`nameOrder`, some without (to exercise the
  fallback ordering). Include at least one person with **no colonial name** (Name
  only), at least one **outside spouse** (`Kinship` line), at least one **adopted**
  person (`BirthWilp` set and differing from their `Kinship` Wilp — exercises the
  label's adoption trigger and the overlay's birth-Wilp rows), and at least one
  person with `Kinship = NoneProvided (Some note)` and one with `NoneProvided
None` (to exercise both overlay `Kinship:` rows).
- **`samples/sample.json`** — matching `names` and `namesHeld` arrays, plus at
  least one person with `name` omitted and raw `dateOfBirth`/`dateOfDeath` values
  (no normalized) to exercise the raw-date fallback, at least one person with
  `kinshipNote` set (no `wilp`) to exercise the note path, and at least one
  person with `birthWilp` set (differing from `wilp`) to exercise adoption.
  `sample.json` is a **demo showcase and must import warning-clean** — it does
  not exercise warning corner cases (e.g. unheld names, unresolved refs); a
  separate diagnostics fixture for those is out of scope here.

## Testing (TDD, per the mandatory dev loop)

F# unit tests (`Wilnaatahl.Core.Tests`), each written RED-first:

- **Reader/contracts**: optional `name`; raw date fields captured; `names` /
  `namesHeld` decoded; absent top-level arrays → empty.
- **Transform**: dedup Names (`DuplicateNameId`); two distinct ids with equal
  text resolve to one `Name` and emit `DuplicateNameText`; unresolved `nameId`
  (`UnresolvedNameId`) and `personId` (`UnresolvedNameHolder`) drop the holding;
  a name held by nobody yields `UnheldName` and is dropped; colonial name `None`
  flows through; raw dates flow into the `…Text` fields; `kinshipNote` populates
  `NoneProvided` when no Wilp resolves, and yields `IgnoredKinshipNote` (dropped)
  when a Wilp does resolve; `birthWilp` resolves to `Person.BirthWilp` (named
  `Wilp` → `Some`; unresolved → `None` + `UnresolvedBirthWilpId`; Pdeeḵ-only →
  `None` + `BirthWilpNotNamed`).
- **Recency ordering**: `nameDate` primary (later first), `nameOrder` fallback,
  missing-both behavior; "most recent" head.
- **`NodeLabel.build`**: every line-presence combination (colonial only, Name
  only, both, neither); `Kinship` content rows (`Wilp`, `UnknownWilp` Pdeeḵ
  display, `NoneProvided`) gated by `currentWilpDiffersFromRendered` true/false;
  the adoption case (adopted-out member shown, adopted member in their own
  current Wilp shows **no** line); all four date-line shapes; `YYYY/MM/DD` vs raw
  verbatim.
- **`NodeDetail.build`**: title variants (colonial-only, Gitxsan-only, both);
  `Kinship` rows for all `Kinship` shapes (`Wilp` two rows, `UnknownWilp` Pdeeḵ
  row, `NoneProvided None` → `Kinship: Unknown`, `NoneProvided (Some note)` →
  `Kinship: <note>`); `Birth Wilp`/`Birth Pdeeḵ` rows present when `BirthWilp` is
  `Some` and differs from the current Wilp, omitted when absent or equal; labeled
  long-date rows and raw fallback; "other names" excludes the most-recent and is
  omitted for ≤ 1 held Name.
- **Writer / round-trip**: `toJson` emits `names`/`namesHeld` with synthesized
  ids, omits absent `name`, writes `kinshipNote` only for `NoneProvided (Some)`,
  emits `birthWilp` when `BirthWilp` is `Some` (with the huwilp id map covering
  Kinship ∪ BirthWilp Wilps), shared/handed-down names collapse to one id, and
  re-reading reproduces the held graph with no warnings.
- **ViewMode / Selection / UndoRedo systems** (`Wilnaatahl.Core.Tests`, .NET
  against the mock — these are app systems, not wrapper/mock-equivalence tests,
  so they do **not** belong in the portable `Wilnaatahl.ECS.Tests`): boot mode is
  View; toggling labels/clears selection; select-mode disabled in View mode;
  overlay gating disables Undo/Redo without mutating stacks and restores
  afterward; dragging never enables in View mode.

Then the full gate: `npm run build` → `npm test` / `test:koota` →
`npm run coverage:check` → the mandatory multi-model adversarial review.

## Open questions / to confirm before build

- **Overlay placement / sizing tuning** — the exact overlay width, `gap`, and
  `margin` values (and the `max-height` before scrolling) are by-eye tuning to be
  settled during implementation; see [Positioning](#positioning).
