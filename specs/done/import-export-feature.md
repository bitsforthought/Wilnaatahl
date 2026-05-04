# Import / Export Feature — Design & Implementation Plan

Lets users load their own genealogical data into Wilnaatahl from a local file
(**import**) and save the current graph back out to a JSON file (**export**).
The on-disk file format and parser are described separately in
[`specs/json-parser.md`](./json-parser.md); this plan consumes its
`Transform.fromJson` (import) and `Transform.toJson` (export) entry points and
otherwise treats them as a black box.

## Problem

- The app can only show hardcoded sample data (`Initial.peopleAndParents`), but
  loading real family data is the tool's primary purpose.
- There is likewise no way to get a graph back out — a user viewing their data
  can't save it to a file to keep or share.
- The sample data is still valuable as a **demo** for first-time visitors who
  don't yet have a file.

## Goals

- Let a user load their own genealogical data from a local file.
- Let a user export the current graph to a JSON file.
- Boot straight into the sample visualization — no intermediate screen.
- Make "Open file…" open the OS file picker **immediately**.
- Keep the implementation purely client-side (no server, no upload).
- Keep the import/export UI/IO concern separate from the F# domain model.
- Give each visualization its own `World`, so Koota's per-`World`
  relation-query growth cannot accumulate across loads (see
  [Performance](#performance)).
- Leave room for future enhancements (multiple files, persistence) without
  locking them in now.

## Non-goals (this iteration)

- Defining the file format or implementing the parser/serializer — see
  `specs/json-parser.md`.
- Editing data in-app: export round-trips the currently-loaded graph; there is
  no in-app editing of that data before saving.
- A native save dialog / directory chooser: export downloads to the browser's
  default location (universally portable).
- Drag-and-drop import — desktop-only; the single file-input path is universal.
- A separate landing / file-chooser screen.
- Multi-user, sync, or cloud storage.
- Authentication / privacy controls beyond "the file never leaves the browser".
- Eliminating the intra-session relation-query growth (follow-up; see
  [Performance](#performance)).

## User flow

On load the app immediately:

1. Creates a fresh `World`.
2. Loads `Initial.peopleAndParents` as a `FamilyGraph`.
3. Spawns controls + scene into that `World` and shows the visualization.

The toolbar's **"Open file…"** button opens the OS file picker directly.

- **Pick a file → success:** the current `World` is destroyed, a new `World` is
  created, and the new graph is spawned into it. Warnings, if any, appear as a
  dismissible toast.
- **Pick a file → parse error:** the current visualization is left untouched;
  a dismissible **error toast** is shown over it (see
  [Error & warning UI](#error--warning-ui)).
- **Cancel the picker:** no-op.

The toolbar's **"Save"** button exports the current graph: it serializes via
`Transform.toJson` and downloads a JSON file to the browser's default location.
It does not swap the `World` or otherwise affect the visualization.

### Sample data uses the same path as an import

The built-in sample is not special-cased in the `World` machinery: it seeds the
first `World` through the same path an import uses — a `FamilyGraph` (from
`ImportService.loadSampleGraph` here, from a parsed file otherwise) spawned into
a freshly-created `World`. `loadSampleGraph` is called from exactly one place
(the initial load), and `Initial.peopleAndParents` / `loadSampleGraph` are
referenced only from that wiring, so the sample's contents can change
independently without touching the `World` lifecycle, import path, or toolbar.

## World lifecycle

Every visualization — including the initial sample — gets its **own** `World`,
and the previous one is destroyed before the next is shown.

### Ownership and provider placement

The work around the `World` splits across two components:

- **`App`** is stable across loads. It creates and destroys the `World`, holds
  the import swap logic and the error/warnings toasts, and provides the current
  `World` to its subtree via `<WorldProvider>`. `src/main.tsx` just renders
  `<App />`; the `World` is owned by `App`, not a module-level singleton.
- **`<Visualizer />`** is the component _inside_ `<WorldProvider>` that renders
  and operates on the current `World`: it owns the `<Canvas>` and `<Toolbar>`
  and, on mount, spawns the controls + scene + layout into that `World`.

`<Visualizer />` is a new component on this branch, and the `World` lifecycle is
what justifies it. Anything that uses World hooks (`useWorld`, `useQuery`,
`useActions`) must live _below_ `<WorldProvider>`, but `App` cannot: `App` owns
the provider and must survive across swaps to run the swap and hold the toasts.
A distinct below-provider component is therefore required, and `<Visualizer />`
is it.

`App` renders `<WorldProvider world={world}>` **keyed by a monotonic per-load
key** — not the `World` id, which Koota recycles when a `World` is destroyed —
so creating a new `World` remounts `<Visualizer />`: React disposes the old
`<Canvas>` and its Three.js resources, and the new mount spawns fresh controls +
scene into the new `World`. Each `World` thus gets its own `<Visualizer />`
instance, which guards its spawn effect so scene setup runs exactly once per
`World` (see [StrictMode](#strictmode--double-invocation-caveat)).

`worldActions` / `eventActions` are built with `createActions` and bind to
whichever `World` is in context via `useActions`, so they target the current
per-load `World` automatically.

### Destroying the old World is mandatory

Koota caps concurrently-live worlds at **16** (`WORLD_ID_BITS = 4`);
`createWorld()` throws `"Too many worlds created"` once the pool is exhausted.
`world.destroy()` releases the world id back to the pool (and resets its
internal maps); dropping the reference does **not** free the slot. So each swap
must:

1. `const next = createWorld()`
2. `previous.destroy()`
3. render under `next`

### Load path

The load path is spawn-only: `spawnControls` + `spawnScene` + `layoutNodes`
into the newly-created `World`. Teardown is destroying the `World`; there is no
per-entity scene teardown.

### StrictMode / double-invocation caveat

React `StrictMode` double-invokes effects in development, which raises two
concerns. First, because the world pool is small (16) and `destroy()` is
required, `World` creation/destruction must be resilient to double mounting
(e.g. create the initial `World` in an effect and destroy the current `World` —
tracked through a ref, since an import may have swapped it — in the matching
cleanup). Getting this wrong surfaces as `"Too many worlds created"` in dev.
Second, `StrictMode` re-runs the `Visualizer`'s spawn effect on the _same_
instance and `World` (no remount, same key), and `spawnControls` / `spawnScene`
are not idempotent, so the `Visualizer` guards its spawn effect with a
per-instance ref to spawn exactly once per `World`.

### Toolbar during the swap

All toolbar buttons are ECS `Button` entities (see
[Toolbar buttons — all ECS-backed](#toolbar-buttons--all-ecs-backed)), so a
freshly-created `World` starts with an empty toolbar until
`spawnControls` runs. To avoid a visibly empty/partial toolbar during the swap,
**the Visualizer hides the toolbar until its control entities exist** (e.g. gate
toolbar rendering on `useQuery(Button).length > 0`, and/or spawn controls before
first paint).

## Toolbar buttons — all ECS-backed

Every toolbar button (undo/redo, select-mode, Open, Save) is an ECS `Button`
entity: `Toolbar` renders every `Button` entity, ordered by `sortOrder`, via
`ToolButton`, with no special-case markup or per-button props.

Open and Save have side effects a system running _inside_ a `World` cannot
perform — browser IO (the OS picker; a Blob download) and, for Open, the
`World` lifecycle (destroy this `World`, create the next). So **the button and
its click** live in ECS, while **the side effect** is fulfilled by a thin React
bridge, using the `ClickEvent`→system pattern — except these systems emit an
_outward request_ instead of mutating in-`World` domain state.

### The request-trait bridge

1. **Traits (F#):** discriminators `OpenFileButton` / `SaveButton` on the button
   entities, and request signals `OpenFileRequested` / `SaveRequested` as
   **World traits** — set on the world itself, exactly like the global
   drag/pointer events today (e.g. `handleDragStart` does
   `world.Add DragStartEvent`). They are cleared once consumed.
2. **Spawn (F#):** `spawnControls` (e.g. a new `spawnFileControls`) spawns the
   Open and Save `Button` entities with `sortOrder` values placing them relative
   to undo/redo/select-mode.
3. **System (F#):** a `FileCommands` system in `Runner.fs` maps a `ClickEvent`
   on the Open (resp. Save) button to `world.Add OpenFileRequested` (resp.
   `world.Add SaveRequested`).
4. **React bridge (TS):** the Visualizer observes the request World-trait (via a
   world-trait subscription) and fulfills it, clearing it afterward with
   `world.Remove`:
   - `OpenFileRequested` → `input.click()` on the hidden `<input type="file">`;
     the chosen file flows through `onChange` → `importFile` → `App` swaps the
     `World`.
   - `SaveRequested` → `Transform.toJson(graph)` (domain serialization stays in
     F#) + Blob download.

     Then clear the signal.

**Boundary.** The button and its click-command live in ECS; the OS picker, the
download, and the `World` create/destroy are the React bridge. This buys a
uniform, `sortOrder`-driven toolbar; a trivial `Toolbar` component; and
F#-testable click semantics for Open/Save (mock-`World` click→request tests,
like the `UndoRedo` / `Selection` system tests).

## Data flow

```
 ┌──────────────┐  ClickEvent   ┌──────────────────┐  OpenFileRequested
 │ "Open file…" │──────────────▶│ FileCommands sys │──────────────┐
 │ ECS Button   │  (F# system)  │ (F#: sets signal)│              │
 └──────────────┘               └──────────────────┘              ▼
                                              ┌──────────────────────────────┐
                                              │ React bridge: input.click()  │
                                              │ → onChange → file.text()     │
                                              └───────────────┬──────────────┘
                                                              │ json string
                                                              ▼
                                                   ┌─────────────────────┐
                                                   │ Transform.fromJson  │
                                                   │ (F#: JsonReader +   │
                                                   │       Transform)    │
                                                   └──────────┬──────────┘
                                                              │ ImportResult
                                                              │ (PeopleAndCoupleIds,
                                                              │  Couples, Warnings)
                                                              ▼
                                                   ┌─────────────────────┐
                                                   │ createFamilyGraph   │
                                                   │ (F#, Model.fs)      │
                                                   └──────────┬──────────┘
                                                              │ FamilyGraph
                                                              ▼
                                                   ┌─────────────────────┐
                                                   │ App: swap World     │
                                                   │  createWorld →      │
                                                   │  destroy(previous)→ │
                                                   │  (remount) →        │
                                                   │  spawnControls →    │
                                                   │  spawnScene →       │
                                                   │  layoutNodes        │
                                                   └─────────────────────┘
```

Key points:

- The parser exists (`Transform.fromJson` in `Wilnaatahl.Persistence`,
  signature `string -> Result<ImportResult, ImportError>`), consumed via
  Fable-generated interop; `ImportError`, `ImportWarning`, and `ImportResult`
  are owned by the parser/transform layer.
- The React layer owns only the IO bridge (file selection, download), the
  warnings/error toasts, and the `World` swap; all domain logic stays in F#.
- Wilp identity comes from the JSON file's top-level `huwilp` array, so the
  parser needs no filename or side-channel input to determine which Wilp a
  person belongs to.

## Import affordance — UI shape

The universally-portable mechanism is `<input type="file">`
([File Import Patterns](./file-import-patterns.md)). There is **one** import
path:

- The "Open file…" toolbar button is an ECS `Button` entity; clicking it emits
  `OpenFileRequested`.
- A hidden `<input type="file" accept=".json,application/json">` lives in the
  visualizer. The React bridge, on `OpenFileRequested`, calls `input.click()`.
- `onChange` hands the chosen `File` to a single `importFile(file)` function.

### File picker `accept` attribute

`accept=".json,application/json"` — extension and MIME, for best cross-platform
behavior (mobile pickers are inconsistent about MIME).

## F#/TS seam

- `Wilnaatahl.Persistence.ImportService` exposes the two graph-loading entry
  points the React layer calls:
  - `loadSampleGraph: unit -> FamilyGraph` —
    `createFamilyGraph Initial.peopleAndParents Initial.couples`.
  - `importJsonText: json: string -> Result<ImportSuccess, ImportError>`, where
    `ImportSuccess = { Graph: FamilyGraph; Warnings: ImportWarning list }`; calls
    `Transform.fromJson` and on `Ok` builds the `FamilyGraph` via
    `createFamilyGraph`.
- `Wilnaatahl.Persistence` (see `specs/json-parser.md`) also owns the parser
  itself: `Transform.fromJson`, `ImportError`, `ImportWarning`, and
  `ImportResult`.
- `Wilnaatahl.ViewModel` owns the user-facing rendering of those parser values
  (message text is a view concern, not a persistence one):
  - `ImportError.toMessage: ImportError -> string`
  - `ImportWarning.toMessage: ImportWarning -> string`
  - `ImportWarning.summary: ImportWarning list -> string` (e.g. "3 unresolved
    parent couples, 1 unparseable date") for the warnings toast.
- **TS has no `importService.ts` adapter.** The file-input `onChange` handler
  does the minimum: `await file.text()`, call Fable-generated
  `ImportService.importJsonText(text)`, then dispatch the `World` swap. The DUs
  (`ImportError` / `ImportWarning`) keep their default Fable shape and render via
  the F# `toMessage` helpers, so TS never pattern-matches on DU tags.
- `EntityLifeCycle.fs` exposes `spawnControls`, `spawnScene`, and `layoutNodes`
  as `worldActions` (the load path is spawn-only).
- **Toolbar/file commands (F#):** `OpenFileButton` / `SaveButton` discriminators
  and `OpenFileRequested` / `SaveRequested` request signals (World traits, like
  today's global drag/pointer events); `spawnControls` also spawns the Open/Save
  `Button` entities (e.g. via `spawnFileControls`); a `FileCommands` system
  (wired into `Runner.fs`) maps their `ClickEvent`s to the request signals. Save
  serialization stays in F# via `Transform.toJson`, invoked by the React bridge;
  the F# system only signals intent.
- The React `Toolbar` is a pure `buttonEntities.map(ToolButton)`. The hidden
  `<input>` and the Open/Save IO bridge live in the Visualizer.

## App state

The app always shows **some** visualization, so `App`'s state is small:

```ts
{
  world: World;               // the current per-visualization World
  graph: FamilyGraph;         // the graph rendered into `world`
  error?: ImportError;        // set on a failed import; shown as a dismissible toast
  warnings?: ImportWarning[]; // set on a successful import with warnings; toast
}
```

A load is a single transition: build the new `FamilyGraph`, create a new
`World`, destroy the old one, and set `{ world, graph }` (plus `warnings` if
any). A failed import sets `error` without touching `world` / `graph`.

A loading indicator is **out of scope for v1**. If needed later, a delayed
spinner is a UI-only addition around the async `importFile` call.

## Component decomposition

```
<App />                        — owns the current World + graph; performs the
                                 World swap on import; renders the error/warning
                                 toasts; provides the World to its subtree.
 └─ <WorldProvider world={world} key={worldKey}>
      └─ <Visualizer graph={…} onFileSelected={importFile} />
           ├─ owns the 3D <Canvas /> (existing TreeScene contents)
           ├─ owns the <Toolbar /> — pure buttonEntities.map(ToolButton),
           │    hidden until control entities exist
           ├─ hidden <input type="file" accept=".json,application/json">
           ├─ IO bridge: on OpenFileRequested → input.click();
           │    on SaveRequested → Transform.toJson(graph) + Blob download
           └─ on mount: spawnControls + spawnScene(graph) + layoutNodes(graph)
```

- Keying `<WorldProvider>` by a monotonic per-load key makes a new `World`
  remount the subtree, disposing the old Canvas and spawning controls + scene
  into the new `World`. Teardown is the `World` being destroyed by `App`.

## Performance

Koota's `queriesHashMap` grows within a `World`'s lifetime: concrete-target
relation queries (e.g. `Movement.fs` querying by a specific moved entity) mint
permanent entries that are only cleared by tearing down the `World` (see
[`specs/queryhashmap-relation-leak.md`](./queryhashmap-relation-leak.md)). A
fresh `World` per load starts with an empty `queriesHashMap`, so that growth
cannot accumulate across loads; `destroy()` on the old `World` resets those maps
and frees the world-id slot.

**Follow-up (out of scope).** The same growth accrues **within a single
session** as the user drags nodes (each distinct moved target mints a query).
Per-load `World` recreation does not address that; it is tracked against
`specs/queryhashmap-relation-leak.md` (a Koota-side fix, or a periodic reset).

## Error & warning UI

- **Import error** (wrong type / unparseable / empty-people): the current
  visualization is left intact and a **dismissible error toast** is shown over
  it, formatted via `ImportError.toMessage`. No `World` is created or destroyed.
- **Import warnings** (successful import with non-empty `warnings`): enter the
  new visualization and show a dismissible toast summarizing them via
  `ImportWarning.summary`, with per-warning detail logged to the dev console.
- **Cancelled picker:** no-op.
- **Very large files:** no special handling in v1; rely on browser limits.

## Accessibility

- Toolbar buttons must be keyboard-focusable and labeled. This falls out of the
  design rather than needing special effort: an ECS `Button` is only data
  (`label`, `disabled`, `sortOrder`), and `ToolButton` renders it as a native
  `<button>` whose text — hence its accessible name — is `Button.label`. Native
  `<button>`s are keyboard-focusable and operable by default.
- Toasts use an appropriate live region (`role="alert"` for errors,
  `role="status"` for warnings) and a labeled dismiss control.

## Testing strategy

- Parser-level F# tests exist in `tests/Wilnaatahl.Core.Tests/Persistence/`
  (reader/writer, transform, integration) per `specs/json-parser.md`.
- F# tests for `ImportService.loadSampleGraph` (the `Initial` sample data builds
  a valid `FamilyGraph`) and `ImportService.importJsonText` (happy and error
  paths).
- F# tests for the message helpers (`ImportError.toMessage`,
  `ImportWarning.toMessage`, `ImportWarning.summary`) in `Wilnaatahl.ViewModel`.
- F# tests for the `FileCommands` system: a `ClickEvent` on the Open (resp.
  Save) button sets `OpenFileRequested` (resp. `SaveRequested`), on the mock
  `World`, mirroring the `UndoRedo` / `Selection` tests.
- Manual verification of the React/`World`-lifecycle seam: (a) load sample, a
  real file, and a malformed file; (b) load repeatedly (≫16 times) to confirm no
  `"Too many worlds created"` crash; (c) confirm the toolbar does not flash empty
  during a swap.

## Decisions confirmed with user

1. **No landing screen.** Boot directly into the sample visualization; "Open
   file…" opens the OS picker directly.
2. **No drag-and-drop.** Single import path via `<input type="file">`.
3. **`World` is per-visualization.** Each load creates a new `World`; the
   previous one is destroyed first (mandatory — Koota's 16-world pool throws
   otherwise).
4. **Load path is spawn-only.** The discarded `World` handles teardown; there is
   no per-entity scene teardown.
5. **No persistence.** No `localStorage`; the app always starts on sample data.
6. **Import errors** surface as a dismissible toast over the current
   visualization, which is left intact.
7. **Toolbar hidden until controls spawn** in the new `World`, to avoid a flash.
8. **Performance fix scoped to repeated loads.** The intra-session drag-time
   `queriesHashMap` growth is a documented follow-up.
9. **Import service lives in F#:** `ImportService.importJsonText` parses and
   constructs the `FamilyGraph` in one call; TS just reads the file and invokes
   it.
10. **DU ergonomics:** `ImportError` / `ImportWarning` keep their default Fable
    shape; F# helpers (`toMessage`, `summary`) are the only surface TS uses.
11. **File picker `accept`:** `.json,application/json`.
12. **All toolbar buttons are ECS `Button` entities**, including Open and Save;
    their side effects are fulfilled by a React bridge reacting to
    `OpenFileRequested` / `SaveRequested` signals from a `FileCommands` system.

## Implementation outline (todos)

Parser, `ImportError`, `ImportWarning`, `ImportResult`, and `Transform.fromJson`
are already implemented per `specs/json-parser.md`.

1. F# import service (`Wilnaatahl.Persistence.ImportService`): add
   `loadSampleGraph` (builds the `FamilyGraph` from `Initial` sample data) and
   `importJsonText` (calling `Transform.fromJson`, building `FamilyGraph`). Test
   the sample-graph build and the import happy/error paths.
2. F# message helpers (`Wilnaatahl.ViewModel`): add `ImportError.toMessage`,
   `ImportWarning.toMessage`, and `ImportWarning.summary`; test each. Regenerate
   TS via Fable.
3. F# toolbar/file commands: add `OpenFileButton` / `SaveButton` discriminators
   and `OpenFileRequested` / `SaveRequested` signals; have `spawnControls` spawn
   the Open/Save `Button` entities with `sortOrder`; add a `FileCommands` system
   wired into `Runner.fs`. Tests per the strategy above. Regenerate TS via Fable.
4. `App` owns the per-load `World`: create a `World` for the initial sample
   load; on import success create a new `World` and `destroy()` the previous
   one; provide the current `World` via a keyed `<WorldProvider>`. Handle
   StrictMode double-invocation safely (lazy create / cleanup destroy). Render
   `<App />` directly in `src/main.tsx`.
5. `<Visualizer />`: hidden `<input type="file" accept=".json,application/json">`;
   IO bridge reacting to the request signals — `OpenFileRequested` →
   `input.click()`, `SaveRequested` → `Transform.toJson(graph)` + Blob download,
   clearing the signal after. On mount spawn controls + scene + layout into the
   current `World`; hide the toolbar until control entities exist.
6. `<Toolbar />`: `[...buttonEntities].sort(...).map(ToolButton)` only.
7. Error & warning UI: dismissible error toast over the current visualization
   (via `ImportError.toMessage`); dismissible warnings toast on a successful
   import with warnings (via `ImportWarning.summary`), details to the console.
8. Accessibility pass (focus, labels, live regions on toasts).
9. Update README with the entry flow (boots into sample; "Open file…" opens the
   picker directly) and a pointer to `specs/json-parser.md` for the file format.
10. Manual verification incl. the repeated-load (≫16×) no-crash check and the
    no-toolbar-flash check; run `npm run coverage:check` to confirm no coverage
    regression.
