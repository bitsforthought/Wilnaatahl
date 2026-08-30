# Copilot Instructions for Wilnaatahl

## Project Overview

Wilnaatahl visualizes genealogical relationships of Gitxsan huwilp members. It is
a cross-platform, web-based tool with a React/TypeScript frontend and a core
domain model in F# (compiled to JS via Fable). The architecture enforces a strict
separation between UI, ECS, and domain logic, with F# as the source of truth for
all business rules and data structures.

## Gitxsan Terminology

The domain uses Gitxsan terms as identifiers. Pluralization in Gitxsan does not
follow the English `+s` convention, so use the correct plural form in identifiers
rather than appending `s`:

- **Wilp** (singular) / **Huwilp** (plural) — a matrilineal House. E.g. a
  `Map<WilpName, Wilp>` keyed by Wilp name should be named `HuwilpByName`, not
  `WilpsByName`.
- **Pdeek** (Clan) — each Wilp belongs to exactly one Pdeek (`LaxGibuu`/Wolf,
  `LaxSkiik`/Eagle, `Ganeda`/Frog, `Giskaast`/Fireweed).

Sim Algyax (Gitxsanimx) is **not** a Canadian language. The Gitxsan nation
precedes Canada by more than 10,000 years; do not frame the language or people as
Canadian.

Avoid the word **"band"** in code, comments, and specs — it carries a specific
legal meaning under Canada's _Indian Act_ that does not apply here and invites
confusion. Prefer **"group"** (or a more precise domain term) when naming a
partition or category.

## Architecture & Data Flow

- **Frontend:** React (TypeScript) in `src/react-components/` and `src/main.tsx`.
- **Core Logic:** F# domain model, view model, ECS traits, entities, and systems
  in `src/Wilnaatahl.Core/`, compiled to JS via Fable.
- **Interop:** TypeScript types in `src/generated/` are auto-generated from F# for
  type-safe interop. Never hand-edit these files — regenerate with `npm run fable`.
- **State Management:** Uses [Koota](https://github.com/pmndrs/koota), an ECS
  library. `src/main.tsx` provides a Koota `World` via `<WorldProvider>`. React
  components access state through Koota hooks (`useWorld()`, `useQuery()`,
  `useTrait()`, `useActions()`).
- **A system encapsulates one _behaviour_, not one kind of entity.** Decompose
  systems by what the app _does_, not by what data they touch: removing a system
  should switch off exactly that behaviour and leave every other one working. So
  "the mode determines which controls are available" belongs wholly to
  `Systems/ViewMode.fs` — including hiding the undo/redo and select-mode buttons —
  even though those buttons are owned by other systems, which declare their own
  button `MoveModeOnly` and never read the mode themselves. Two consequences:
  a system that reads another system's state for _chrome_ is usually a misplaced
  behaviour, whereas one that reads it because the state genuinely changes what
  its own behaviour means (View mode changes what clicking a node does) is
  correct; and a system needing to run at two different points in the frame is a
  sign it holds two behaviours.
- **Cross-cutting traits belong in `Traits/`, not in the system that writes
  them.** A system module may declare traits that are private to it (its own
  button discriminators) or that are outward signals only the TS layer consumes
  (`FileCommands.OpenFileRequested`). But once _another F# system_ reads a trait,
  put it in `Traits/` — otherwise every reader has to `open` the writing system's
  module, which reads as a dependency on that system's behaviour when it is only a
  dependency on shared state (`ViewTraits.CurrentMode`: written by
  `Systems.ViewMode`, read by `Selection` and the view layer).
- **Order systems in `Runner.fs` so derived values are recomputed after their
  inputs.** A system that derives state from another system's output must run
  after it, or the derived value lags a frame. Better still, remove the
  constraint: a system that needs to run at _two_ points in the frame is holding
  two behaviours, and the fix is usually to hand the behaviour to whoever owns the
  input rather than to split the system across the pipeline. Constraints that
  remain are real behaviour: state them in the `runSystems` comment and pin them
  with a test that drives whole frames through `runSystems`, not one that calls a
  single system.
- **Resolve same-frame input in the system that owns each behaviour.** Events live
  for the whole frame (`cleanupEvents` runs last), so several inputs can be raised
  before any system runs. Each click carries the `AppMode` that was live when the
  view layer raised it. `Selection` and `UndoRedo` fold their `Clicked` events in
  queue order; `Dragging` folds raw drag input in queue order according to its own
  semantics. `handleDragStart` still discards queued clicks and background misses
  because a drag and those inputs cannot be applied together.
- **Never write a trait value that hasn't changed.** Koota's `set` notifies change
  subscribers unconditionally — it does not diff old against new — so a system
  that recomputes a value each frame and writes it unconditionally re-renders
  every subscribed React component at 60 fps. Guard the write on an actual change;
  where several systems write the same field, factor the guard into one helper
  (see `Systems.Controls.setButtonDisabled`). Adding or removing a **tag** is safe
  — Koota's `add`/`remove` no-op when the trait is already in the desired state.
- **ECS bridge:** `src/ecs/koota/kootaWrapper.ts` bridges Koota's TypeScript API
  and the F# ECS interfaces. F# systems (layout, animation, dragging, movement,
  selection, undo/redo) run each frame via `useFrame()` in `TreeScene.tsx`.
- **3D Rendering:** `@react-three/fiber` + Three.js. Scene components in
  `src/react-components/` (TreeScene, HuwilpGroup, TreeNodeMesh, ElbowSphereMesh,
  LineMesh). The rendering system in `src/ecs/rendering.ts` synchronizes Koota
  trait changes to Three.js meshes.
- **Styling:** All styles in `src/style.css` (global CSS with light/dark mode).
- **F# is authoritative — but that is a principle, not a law.** All business logic
  and data structures originate in F# and are generated to TypeScript via Fable.
  React components must use the F#-generated view model and ECS systems for state
  and actions — do not reimplement domain logic in TypeScript.
- **Avoiding redundant state outranks "F# is authoritative".** Koota is a bridge:
  the same world is queryable from both F# and TypeScript. So when a value is a
  one-line derivation of world state both sides can already see (e.g. "the overlay
  shows" = in View mode **and** exactly one node `Selected`), derive it where it is
  consumed rather than mirroring it into an extra trait that F# recomputes each
  frame. A cached copy is a second source of truth that can disagree with the
  values it came from; that cost is real, whereas "the rule lives in F#" is only a
  default. Keep the derivation in F# when it is genuinely domain logic (more than
  a trivial predicate over traits), when several consumers would otherwise repeat
  it, or when recomputing it per consumer is measurably expensive. Note the price:
  the TypeScript layer has **no unit tests yet**, so moving a derivation there
  trades automated coverage for the removal of the cached copy — take that trade
  only for derivations simple enough to verify by reading.
- **Presentation formatting is a view-layer (TS) concern, not domain logic.**
  Locale-dependent **date/number formatting** belongs in the TypeScript view layer
  (e.g. `Intl.DateTimeFormat`), not F#. **Translatable language strings** come from
  a shared locale catalog authored in F# and consumed on both sides via Fable (one
  `Locale` type, no .NET `CultureInfo`), so F# (e.g. import messages) and TS (UI
  chrome) localize the same way. The F# view model stays presentation-neutral
  (e.g. a `DateOnly` plus any raw-text fallback), leaving date formatting to TS.
  Gitxsan **data values** (a specific House's/clan's name, a person's Name) are
  never translated; Gitxsan words used as UI **labels** (e.g. "Wilp", "Pdeeḵ") are
  chrome that may in future be localized (an all-English vs all-Gitxsan UI), so
  they belong in the shared catalog too.
- **Production runtime is Fable-generated JS in a browser**, not the CLR. Don't
  assume a CLR runtime when reasoning about runtime behaviour.
- **Licensing:** AGPL-3.0 with a non-commercial restriction (see `LICENSE`).

## Developer Workflows

- **Setup:** `npm run init` (installs npm packages, restores .NET tools/packages)
- **Dev server:** `npm run dev` (runs Fable then Vite with hot reload)
- **Build for deploy:** `npm run build`
- **Unit tests:** `npm test` (.NET xUnit, then Koota conformance via Fable + vite-node)
- **Koota tests only:** `npm run test:koota`
- **Coverage gate:** `npm run coverage:check`
- **Coverage report:** `npm run report --coveragefile=<path-to-xml>`
- **Format code:** `npm run format` (Prettier for TS, Fantomas for F#)

## Keeping token cost down

Every tool call re-sends the whole context window, so keeping the working context
lean directly lowers cost. When working in this repo:

- **Delegate heavy investigation to subagents.** Route multi-file research or broad
  exploration to an `explore`/`research` subagent so its tokens come back as a
  summary instead of accreting raw file contents into the main context that every
  later turn re-sends.
- **Prefer the built-in `view`/`grep`/`glob` over shelling out** to
  `Get-Content`/`Select-String`/`Get-ChildItem`; they return capped, structured
  output instead of dumping raw text into context.
- **Read only what you need.** Use `view` with a line range on large files instead
  of reading them whole, and don't re-read a file already in context.
- **Search narrowly.** Prefer a targeted `glob`/`grep` over a broad repo-wide scan,
  and batch independent reads/searches into a single step.

## Key Files & Directories

- `src/Wilnaatahl.Core/Model.fs` – Domain model (people, relationships, family graph)
- `src/Wilnaatahl.Core/ViewModel/` – View model, scene, layout utilities, vector math
- `src/Wilnaatahl.Core/Traits/` – F# ECS trait definitions
- `src/Wilnaatahl.Core/Entities/` – Entity factories
- `src/Wilnaatahl.Core/Systems/` – F# ECS systems (add new systems to `Runner.fs`)
- `src/Wilnaatahl.Core/ECS/` – ECS interfaces and Koota bindings
- `src/generated/` – Auto-generated TS from F# (do not edit)
- `src/ecs/` – TypeScript ECS layer: Koota wrapper, traits, rendering, hooks
- `src/react-components/` – React UI components
- `tests/Wilnaatahl.Core.Tests/` – .NET-only F# tests for domain, view model, and
  app/ECS-**system** logic (exercised against the .NET mock). Most tests live here.
- `tests/Wilnaatahl.ECS.Tests/` – Portable tests that run on **both** the .NET mock
  and real Koota (via Fable). Their sole purpose is to prove the mock and the Koota
  wrapper are behaviourally equivalent; keep this surface **minimal**. That
  equivalence is what lets all other F# logic be tested with confidence in
  `Core.Tests` — do **not** add app or system tests here.
- `scripts/` – Build/CI/codegen scripts (`.fsx` via `dotnet fsi`)
- `specs/` – Design specs

## Mandatory dev loop (definition of done)

Detailed conventions live in **skills** (`.github/skills/`) and specialized
**agents** (`.github/agents/`). A change is not done until it has been through this
loop:

```
plan (tests before implementation, never batched to the end)
  → RED   (test compiles, runs, and fails for the right reason against a stub)
  → GREEN (smallest idiomatic-F# change that passes)
  → REFACTOR (deep dead-code removal, truthful comments)
  → npm run build          (Fable can emit bad TS that `dotnet test` misses)
  → npm test / test:koota
  → npm run coverage:check
  → MANDATORY multi-model adversarial review  (run the `adversarial-reviewer`
    agent under several different models — e.g. an Anthropic, an OpenAI, and a
    Google model — never skipped, never gated on perceived risk; WAIT for every
    panelist to report before consolidating — a late/lone dissenter often caught
    the real bug)
  → address every genuine finding, re-validate, re-solicit a fresh pass
    (iterate ≤ 3 rounds, stopping early once a round is clean) → only then "done"
```

The adversarial review is **not optional** and **not something to wait to be
prompted for** — it is part of every change.

## Committing and source hygiene

- **Wrap commit messages at a maximum of 80 columns.** `git commit -m` keeps each
  `-m` argument as one unwrapped line; use `git commit -F <file>` (or `\n` inside
  `-m`) to wrap properly. Include the `Co-authored-by: Copilot` trailer.
- **Source files use LF line endings and spaces (never tabs) for indentation**,
  enforced by `.editorconfig` (plus `.gitattributes` for line endings). Don't
  fight the formatters — Prettier owns `.ts`/`.tsx`, Fantomas owns `.fs`/`.fsx`.

## When to use which skill / agent

| Doing this                                                                        | Use                                                            |
| --------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| Implementing/modifying F# core (Model, ViewModel, Traits, Entities, Systems, ECS) | `fsharp-implementer` agent                                     |
| Writing/editing any `.fs` file                                                    | `fsharp-style` + `fsharp-doc-comments` skills                  |
| Writing or strengthening F# tests                                                 | `fsharp-testing` skill                                         |
| Running the build/test/coverage gate                                              | `tdd-coverage-loop` skill                                      |
| Reviewing a change (the mandatory step)                                           | `adversarial-reviewer` agent / `adversarial-code-review` skill |
| Adding/editing build/CI/codegen scripts                                           | `infra-scripts-fsharp` skill                                   |
| Writing/modifying TypeScript / React / Three.js                                   | `typescript-implementer` agent / `typescript-style` skill      |

Skills load automatically when Copilot judges them relevant (driven by each
skill's `description`). The routing table above and each agent's own instructions
name the skills to apply; if a skill does not auto-load when its trigger applies,
load it explicitly from the table.

## Examples

- **Add a domain property:** Update F# in `Model.fs`, run `npm run fable` to
  regenerate TS types, and use via the view model or ECS traits in React.
- **Add a UI feature:** Add or update a React component that reads Koota traits via
  hooks, with logic driven by F# systems.
- **Add an ECS system:** Define the system in F# under `Systems/`, add it to
  `Runner.fs`, and Fable will generate the TS entry point.
