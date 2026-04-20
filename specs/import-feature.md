# Import Feature — Design & Implementation Plan

Adds the ability for users to load real genealogical data into Wilnaatahl,
replacing reliance on the hardcoded `Initial.peopleAndParents` seed.

This document is **format-agnostic**: it covers user flow, app entry behavior,
state transitions, and the F#/TS seam. The on-disk file format (GEDCOM, JSON,
custom, etc.) and parser are deliberately out of scope.

## Problem

- Today the app launches directly into a visualization of hardcoded sample data
  (`Initial.peopleAndParents`, loaded synchronously by `GraphViewFactory.LoadGraph`
  in `App.tsx`).
- There is no way for a user to bring their own family data, which is the
  primary intended use of the tool.
- The seed data is also useful as a **demo** for first-time visitors who don't
  yet have a file.

## Goals

- Let a user load their own genealogical data from a local file.
- Preserve a friction-free "just look at it" experience for first-time visitors.
- Keep the implementation purely client-side (no server, no upload).
- Cleanly separate the import UI/IO concern from the F# domain model.
- Leave room for future enhancements (re-import, multiple files, persistence,
  export) without locking them in now.

## Non-goals (this iteration)

- Choosing or implementing a file format / parser.
- Editing data in-app and writing it back to disk.
- Multi-user, sync, or cloud storage.
- Authentication / privacy controls beyond "the file never leaves the browser".

## Current state (relevant code)

- `src/main.tsx` — mounts `<App />` inside `<WorldProvider>`.
- `src/react-components/App.tsx` — calls `LoadGraph()` once on mount and
  drives `spawnScene` / `layoutNodes` / `destroyScene`.
- `src/Wilnaatahl.Core/ViewModel/ViewModel.fs` — `GraphViewFactory.LoadGraph`
  returns `createFamilyGraph Initial.peopleAndParents`.
- `src/Wilnaatahl.Core/Model.fs` — `module Initial` holds the hardcoded seed;
  `createFamilyGraph` builds a `FamilyGraph` from a sequence of
  `Person * CoParentRelationship option`.
- `src/react-components/Toolbar.tsx` — toolbar is ECS-driven (`Button` trait),
  so a new tool button drops in cleanly.

## App entry / user flow

**Decision: Option A** — landing screen with two choices.

On first load the canvas is replaced by a centered landing panel:

> **Wilnaatahl**
> [ Open a file… ] [ Explore sample data ]

- "Open a file…" → native file picker (also a drop target on desktop).
- "Explore sample data" → loads `Initial.peopleAndParents` and enters the viz.
- Once any graph is showing, the toolbar exposes an **Open file…** button that
  swaps the scene when used.

### Forward-compatibility with Option C (future)

Once test data exists as importable files, the desired end state is **Option
C** — empty canvas with import as the only entry, sample data removed entirely.
The implementation must make that transition trivial. Concretely:

- The landing component takes its CTAs (calls to action — i.e. the buttons
  the user clicks to get started) as **data**, not hardcoded markup, e.g.:
  ```ts
  type LandingChoice = { id: string; label: string; onSelect(): void };
  ```
  Switching to Option C is "remove the sample CTA from the array and delete
  the sample-loading code path" — no structural rewrite.
- The sample-data path lives behind a single feature flag / module boundary
  (e.g. `loadSampleGraph()` in one place, called from exactly one CTA).
  Removing Option B/A's sample affordance later means deleting that one call
  site and the function; nothing else cares.
- The state machine (below) already treats "sample" and "file" as two ways
  into the same `Visualizing` state, so removing the "sample" entry point
  doesn't change states or transitions.
- `Initial.peopleAndParents` and `LoadSampleGraph` are explicitly **not**
  referenced from anywhere except the sample CTA wiring, so they can be
  deleted in a single follow-up PR when test data files exist.

## Scene replacement on import

When the user imports a file (from landing or from the toolbar):

1. Run `destroyScene` — tears down all entities of the currently-displayed
   graph (whether sample or a previous import).
2. Build the new `FamilyGraph` from parsed input.
3. Run `spawnScene(newGraph)` then `layoutNodes(newGraph)`.

There is no concept of "merging" or "keeping" the previous data. The sample
data and its entities are fully destroyed once a real file is loaded; the
same applies to any previously-imported file when a new one is opened.

## Import affordance — UI shape

Per [File Import Patterns](./file-import-patterns.md), the universally-portable
mechanism is `<input type="file">`. The plan uses **one** import component that
exposes both interaction styles:

- **Mobile / tablet**: tap the button (or the landing panel's drop zone) →
  native OS file picker.
- **Desktop**: same button works, _and_ the landing panel + a top-level overlay
  accept drag-and-drop anywhere in the window during a drag.
- **Optional progressive enhancement**: feature-detect
  `window.showOpenFilePicker` on Chromium for a nicer file handle. Not in
  scope for v1 unless trivial.

Both paths converge on a `File` object handed to a single `importFile(file)`
function.

## Re-import affordance

**Toolbar only** for v1. A new ECS `Button` entity ("Open file…") appears in
the toolbar once the app is in the `Visualizing` state and triggers the same
import flow as the landing CTA. No menu, no keyboard shortcut in this
iteration (could be added later without disturbing the architecture).

```
 ┌──────────────┐     File      ┌──────────────────┐  string/bytes  ┌────────────────┐
 │ Import UI    │──────────────▶│ Import service   │───────────────▶│ Format parser  │
 │ (React)      │   File API    │ (TS, thin shell) │                │ (TBD; F# pref.)│
 └──────────────┘               └──────────────────┘                └───────┬────────┘
                                                                            │ peopleAndParents
                                                                            ▼
                                                                   ┌─────────────────────┐
                                                                   │ createFamilyGraph   │
                                                                   │ (F#, Model.fs)      │
                                                                   └──────────┬──────────┘
                                                                              │ FamilyGraph
                                                                              ▼
                                                                   ┌─────────────────────┐
                                                                   │ worldActions:       │
                                                                   │  destroyScene →     │
                                                                   │  spawnScene →       │
                                                                   │  layoutNodes        │
                                                                   └─────────────────────┘
```

Key points:

- The **format parser is a black box** behind a stable seam:
  `parse: string | ArrayBuffer -> Result<seq<Person * CoParentRelationship option>, ImportError>`.
  Defining this seam now lets us land the UI/flow without committing to a format.
- The React layer owns only file selection and error display; all domain logic
  remains in F#.
- Loading a new graph reuses the existing `destroyScene` → `spawnScene` →
  `layoutNodes` action sequence, so no new ECS wiring is needed for the swap
  itself.

## F#/TS seam changes

- `IGraphViewFactory` / `GraphViewFactory` evolve from a fixed `LoadGraph()`
  to support both seed and supplied data:
  - `LoadSampleGraph(): FamilyGraph` — wraps current behavior.
  - `LoadGraphFromPeopleAndParents(input): FamilyGraph` — pure constructor
    over a parsed input shape (whatever the parser yields).
- A new F# `Import` module owns the `ImportError` discriminated union and the
  parser-result → `FamilyGraph` adapter (no I/O, fully testable in
  `Wilnaatahl.Core.Tests`).
- TS `importService.ts` (thin shell) owns `File` reading and calls into the
  generated F# functions. Errors are surfaced as React state for the UI.

## State machine (app-level)

```
 ┌──────────┐  click "Explore sample data"  ┌────────────┐
 │ Landing  │──────────────────────────────▶│ Visualizing│
 │ (no data)│  ◀──────────────────────────  │ (any data) │
 └────┬─────┘   click "Open a different     └─────┬──────┘
      │         file…" / drag a new file          │
      │ pick / drop file                          │ pick / drop file
      ▼                                           ▼
 ┌──────────┐ parse OK ┌────────────┐    parse OK (replaces current graph)
 │ Importing│─────────▶│ Visualizing│◀────────────┘
 │          │          └────────────┘
 │          │ parse error
 │          │─────────▶ stays in current state, shows toast/inline error
 └──────────┘
```

- "Importing" is a transient state; on success it transitions into
  "Visualizing" by running the existing scene actions.
- On failure, the previous state is preserved and an error message is shown
  inline (landing) or as a toast (visualizing).

## Persistence (this iteration and beyond)

Persist a tiny piece of state in `localStorage`:

```ts
{ lastChoice: "sample" | "file"; fileName?: string }
```

Behavior on reload:

- `"sample"` → skip landing, load sample directly into viz.
- `"file"` → return to landing with a hint: _"Last opened: family.ged.
  [Open file…]"_ (browsers can't silently re-read a file by path for privacy
  reasons — the user must re-pick). Showing the previous filename keeps the
  re-pick low-friction.
- unset (first visit) → show landing.

Caching the file's contents to restore the graph automatically on reload is
**not planned** — re-picking the file on reload is acceptable.

## Error & edge-case handling

- Wrong file type / unparseable content → show error message identifying the
  file and a one-line summary; do not destroy the existing scene.
- Empty file / zero people parsed → treated as an error.
- Very large files → no special handling in v1; rely on browser limits. Note
  as a follow-up if files in practice exceed a few MB.
- Cancelled file picker → no-op.
- Drop of multiple files → use the first; ignore the rest with a toast.
- Drop of a non-file (e.g. text selection) → ignored.

## Accessibility

- Landing buttons must be keyboard-focusable and labeled.
- The drop zone must have a visible focused state and an equivalent button
  (drop is a desktop-only enhancement, never the only path).
- Error messages must be associated with the controls that produced them
  (`aria-describedby`).

## Testing strategy

- F# unit tests in `Wilnaatahl.Core.Tests` for the import adapter:
  empty input, duplicate IDs, dangling parent refs, etc. — all using stable
  `TestData`, not `Initial`.
- Component-level test of the import flow can wait until a parser exists; the
  React seam is thin enough that manual verification is acceptable for v1.
- Existing ECS / view-model tests are unaffected; the scene-swap path already
  has coverage via `destroyScene` / `spawnScene`.

## Decisions confirmed with user

1. **Entry flow = Option A** (landing with two CTAs), but structured for an
   easy future move to Option C (see "Forward-compatibility" above).
2. **Scene replacement on import**: previous data (sample or earlier file) is
   fully destroyed; no merging.
3. **Re-import**: toolbar button only for v1.
4. **Persistence**: `lastChoice` marker in `localStorage` only; the file's
   contents are not cached and the user re-picks the file on reload.

## Implementation outline (todos)

1. Spec & seam: define `ImportError` DU and `parse` signature in F# (no real
   parser body — return `Error NotImplemented` for now).
2. F# adapter: `Import.toFamilyGraph` from parsed input, with unit tests.
3. F# factory: split `GraphViewFactory.LoadGraph` into `LoadSampleGraph` and
   `LoadGraphFromInput`; regenerate TS via Fable.
4. TS import service: `importFile(file): Promise<Result>` shell that reads the
   `File` and invokes the F# parse + adapter.
5. React landing component (gated by app-level state); wire chosen entry-flow
   option.
6. React drop-zone wrapper around landing + global drag overlay during viz.
7. Toolbar "Open file…" button (new ECS `Button` entity) for re-import while
   visualizing.
8. App-level state machine (`landing | importing | visualizing` + last error)
   and integration with existing `spawnScene` / `destroyScene` actions.
9. `localStorage` `lastChoice` persistence and reload behavior.
10. Error UI: inline on landing, toast in viz.
11. Accessibility pass (focus, labels, keyboard, aria).
12. Update README with the new entry flow and a note that file format is TBD.
