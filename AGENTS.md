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
- **ECS bridge:** `src/ecs/koota/kootaWrapper.ts` bridges Koota's TypeScript API
  and the F# ECS interfaces. F# systems (layout, animation, dragging, movement,
  selection, undo/redo) run each frame via `useFrame()` in `TreeScene.tsx`.
- **3D Rendering:** `@react-three/fiber` + Three.js. Scene components in
  `src/react-components/` (TreeScene, HuwilpGroup, TreeNodeMesh, ElbowSphereMesh,
  LineMesh). The rendering system in `src/ecs/rendering.ts` synchronizes Koota
  trait changes to Three.js meshes.
- **Styling:** All styles in `src/style.css` (global CSS with light/dark mode).
- **F# is authoritative.** All business logic and data structures originate in F#
  and are generated to TypeScript via Fable. React components must use the
  F#-generated view model and ECS systems for state and actions — do not
  reimplement domain logic in TypeScript.
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
