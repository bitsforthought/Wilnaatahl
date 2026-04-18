# Copilot Instructions for Wilnaatahl

## Project Overview

Wilnaatahl visualizes genealogical relationships of Gitxsan huwilp members. It is a cross-platform, web-based tool with a React/TypeScript frontend and a core domain model in F# (compiled to JS via Fable). The architecture enforces a strict separation between UI, ECS, and domain logic, with F# as the source of truth for all business rules and data structures.

## Gitxsan Terminology

The domain uses Gitxsan terms as identifiers. Pluralization in Gitxsan does not follow the English `+s` convention, so use the correct plural form in identifiers rather than appending `s`:

- **Wilp** (singular) / **Huwilp** (plural) — a matrilineal House. E.g. a `Map<WilpName, Wilp>` keyed by Wilp name should be named `HuwilpByName`, not `WilpsByName`.
- **Pdeek** (Clan) — each Wilp belongs to exactly one Pdeek (`LaxGibuu`/Wolf, `LaxSkiik`/Eagle, `Ganeda`/Frog, `Giskaast`/Fireweed).

## Architecture & Data Flow

- **Frontend:** React (TypeScript) in `src/react-components/` and `src/main.tsx`.
- **Core Logic:** F# domain model, view model, ECS traits, entities, and systems in `src/Wilnaatahl.Core/`, compiled to JS via Fable.
- **Interop:** TypeScript types in `src/generated/` are auto-generated from F# for type-safe interop. Never hand-edit these files—regenerate with `npm run fable`.
- **State Management:** Uses [Koota](https://github.com/pmndrs/koota), an ECS library. `src/main.tsx` provides a Koota `World` via `<WorldProvider>`. React components access state through Koota hooks (`useWorld()`, `useQuery()`, `useTrait()`, `useActions()`).
- **ECS bridge:** `src/ecs/koota/kootaWrapper.ts` bridges Koota's TypeScript API and the F# ECS interfaces. F# systems (layout, animation, dragging, movement, selection, undo/redo) run each frame via `useFrame()` in `TreeScene.tsx`.
- **3D Rendering:** `@react-three/fiber` + Three.js. Scene components in `src/react-components/` (TreeScene, HuwilpGroup, TreeNodeMesh, ElbowSphereMesh, LineMesh). The rendering system in `src/ecs/rendering.ts` synchronizes Koota trait changes to Three.js meshes.
- **Styling:** All styles in `src/style.css` (global CSS with light/dark mode support).

## Developer Workflows

- **Setup:** `npm run init` (installs npm packages, restores .NET tools and packages)
- **Dev server:** `npm run dev` (runs Fable then Vite with hot reload)
- **Build for deploy:** `npm run build`
- **Unit tests:** `npm test` (runs .NET tests via xUnit, then Koota conformance tests via Fable + vite-node)
- **Koota tests only:** `npm run test:koota` (compiles ECS tests to TS via Fable, runs against real Koota)
- **Code coverage:**
  - `npm run coverage` (generates coverage XML)
  - `npm run report --coveragefile=<path-to-xml>` (generates HTML report in `coveragereport/`)
- **Format code:** `npm run format` (Prettier for TS, Fantomas for F#)

## Project Conventions & Patterns

- **F# is authoritative:** All business logic and data structures originate in F#. TypeScript types and logic are generated from F# via Fable.
- **No duplication of business logic:** React components must use the F#-generated view model and ECS systems for state and actions. Do not reimplement domain logic in TypeScript.
- **Interop:** Regenerate `src/generated/` via Fable after F# changes. Never edit these files by hand.
- **Testing:**
  - ECS tests in `tests/Wilnaatahl.ECS.Tests/` are portable — they run against both the .NET mock and real Koota via Fable.
  - Non-ECS tests in `tests/Wilnaatahl.Core.Tests/` (Model, ViewModel) are .NET-only.
  - All public F# members must have direct test coverage, including edge cases. Use direct equality assertions (e.g., `x =! y`), not pattern matching or mutation.
  - JS/TS tests (if any) should be colocated with components.
- **Functional style:** F# code is functional-first, minimal, and avoids mutation. Prefer direct equality checks over pattern matching in tests.
- **Licensing:** AGPL-3.0 with a non-commercial restriction (see `LICENSE`).

## Key Files & Directories

- `src/Wilnaatahl.Core/Model.fs` – Domain model (people, relationships, family graph)
- `src/Wilnaatahl.Core/ViewModel/` – View model, scene, layout utilities, vector math
- `src/Wilnaatahl.Core/Traits/` – F# ECS trait definitions (people, connectors, space, view, events)
- `src/Wilnaatahl.Core/Entities/` – Entity factories (people, connectors, bounding boxes, lines)
- `src/Wilnaatahl.Core/Systems/` – F# ECS systems (layout, animation, dragging, movement, selection, undo/redo)
- `src/Wilnaatahl.Core/ECS/` – ECS interfaces and Koota bindings
- `src/generated/` – Auto-generated TS from F# (do not edit)
- `src/ecs/` – TypeScript ECS layer: Koota wrapper, traits, rendering, hooks
- `src/react-components/` – React UI components
- `tests/Wilnaatahl.Core.Tests/` – F# unit tests (Model, ViewModel)
- `tests/Wilnaatahl.ECS.Tests/` – Portable ECS tests (run on .NET mock and Koota via Fable)

## Examples

- **Add a domain property:** Update F# in `Model.fs`, run `npm run fable` to regenerate TS types, and use via the view model or ECS traits in React.
- **Add a UI feature:** Add or update a React component that reads Koota traits via hooks, with logic driven by F# systems.
- **Add an ECS system:** Define the system in F# under `Systems/`, add it to `Runner.fs`, and Fable will generate the TS entry point.

## Agent Guidelines

These reflect the owner's priorities, learned from prior sessions.

### Testing Philosophy

- **Koota is the gold standard.** The mock must match Koota's behavior exactly, even when that behavior appears buggy or inconsistent. Document known Koota bugs with issue links, but replicate them faithfully.
- **TDD is strict.** Write failing tests first, observe the failure, then implement. Don't skip the red phase — it validates the test itself.
- **Tests should be portable by default.** ECS tests should run against both the .NET mock and real Koota unless technically impossible (e.g., Fable doesn't support quotations).
- **Test the lowest common denominator.** When the mock is more permissive than Koota, constrain tests to what Koota supports (e.g., object schemas instead of primitive values for traits).
- **Use xUnit class fixtures** for tests with repetitive setup. Shared setup goes in the constructor; cleanup via `IDisposable`. See `TrackingTests` and `PeopleTests` for the pattern. Module-level functions are fine for tests with minimal shared setup.
- **Tests must be strong.** Every assertion should verify observable behavior that would fail if the code-under-test were broken. Avoid tautological tests (e.g., checking a value didn't change when nothing could have changed it).
- **Use Unquote operators** (`=!`, `<>!`, `>!`, `<!`, `>=!`, `<=!`) for single-operator assertions. Use `test <@ expr @>` only for complex multi-operator boolean expressions where splitting into individual assertions would reduce readability. Never use `test <@` in portable ECS tests (Fable doesn't support quotations).
- **Use F# structural equality** for assertions. Compare records, tuples, and other structured types as whole values rather than deconstructing them into components. For `Vector3` (returned by `Line3.getPositions`), use `=! Vector3.FromComponents(x, y, z)`. For frozen Position values (anonymous records from `get Position`), use `=! Line3.pos x y z`. Do not deconstruct structured data into a tuple only to compare the tuple.
- **No magic numbers in tests.** Extract constants with descriptive names and comments explaining the chosen value (e.g., `let frameDelta = 0.016 // one frame at 60 FPS`).
- **Test data independence.** Tests in `Wilnaatahl.Core.Tests` should use `TestData.testPeopleAndParents` (stable test data), not `Initial.peopleAndParents` (app seed data that may change).

### Code Style

- **Separate pure and impure code.** Keep I/O (file reads, console output, `exit`) at the top level or in a thin shell. Functions called by the top level should be pure — no side effects, no `exit`, no file I/O. Communicate errors via discriminated union return types (e.g., `Result<'T, Error>`) rather than exceptions or early exits.
- **Use F# idioms, not workarounds.** Prefer bare `_` discards over `_prefixed` names where the language allows. Use tuple-style `TryRemove` returns instead of `&` out-params. Don't over-qualify record constructors when the type can be inferred.
- **Don't use `emitJsExpr` when F# works.** Default to pure F#; only use JS interop when the F# standard library can't express it.
- **No optional parameters.** Optional parameters are an OOP/C#-interop feature, not idiomatic F#. Make all parameters required. If F# type inference fails without an overload, add type annotations at the point of declaration rather than introducing optional parameters or leaving dead overloads.
- **Don't introduce unnecessary abstractions.** Avoid over-generalizing (e.g., predicate callbacks when a concrete parameter suffices).
- **Don't leave dead code.** Remove unused overloads, unreachable branches, and orphaned helpers. If removing code causes a compile error, fix the root cause (e.g., add type annotations) rather than keeping the dead code as a workaround.
- **Avoid unnecessary type annotations.** F# infers types well in most cases. Only add annotations when needed for disambiguation or to fix inference failures.
- **PascalCase file names.** F# source files and scripts should use PascalCase (e.g., `CheckCoverage.fsx`, `Model.fs`), not kebab-case or camelCase.
- **Cross-assembly anonymous records.** Anonymous records created in one assembly are a different type from those in another. Use helper functions in the source assembly (e.g., `Line3.pos`) to create anonymous records that can be used in test assertions.

### Process

- **Check coverage after every change.** Run `npm run coverage:check` after making code changes and before committing. This runs all tests with coverage collection, generates a summary, and fails if line coverage drops below the baseline in `coverage-baseline.json`. The baseline auto-updates when coverage improves.
- **Don't silently weaken tests.** If an assertion is removed, explain why it was necessary. Removing assertions to make things compile is not acceptable.
- **Preserve existing comments.** Don't delete comments from code being refactored unless they're factually wrong.
- **When behavior changes, update comments to match.** Dead code paths should `failwith`, not silently return defaults.
- **Investigate before assuming.** When empirical results conflict with documentation, check the source code and issue tracker before concluding something is a bug or intended behavior.
- **Minimize conditional compilation.** Encapsulate platform differences in shared infrastructure types rather than sprinkling `#if` throughout test bodies.
- **Line endings must be LF**, not CRLF.
