---
name: typescript-style
description: >-
  TypeScript, React, and Three.js conventions for the Wilnaatahl frontend —
  the thin UI/ECS-bridge layer that consumes the F#-generated view model. Use
  when writing or modifying any `.ts`/`.tsx` file under `src/` (react-components,
  ecs). This skill is the home for TypeScript guidance as that surface grows.
---

# TypeScript / React / Three.js style

The TypeScript layer is deliberately thin. All business logic and data structures
originate in F# and are generated to `src/generated/` via Fable. React components
and the ECS bridge **consume** that view model and the F# ECS systems — they must
not reimplement domain logic.

## General TS conventions

- **Never hand-edit `src/generated/`.** Regenerate with `npm run fable` after F#
  changes.
- **No duplication of business logic.** React components use the F#-generated view
  model and ECS systems for state and actions; do not reimplement domain rules in
  TypeScript.
- **Keep code moves mechanical by default.** When relocating existing code,
  preserve its named functions, helper boundaries, declaration order, and useful
  explanatory comments. A move should be easy to review as the same code in a new
  home; do not inline, restructure, rename, or delete comments unless the task
  explicitly requires that separate refactor or the original text is no longer
  correct.
- **Keep `.tsx` presentation-only.** JSX may wire hooks, state, props, and styles,
  but arithmetic, string composition, branching, and I/O sequences belong in F#
  when they are domain/layout logic, or in a tested `.ts` sibling when they are
  browser-specific. See `AGENTS.md` for the canonical boundary; `.tsx` is outside
  the TypeScript coverage denominator.
- **Avoid redundant state and unconditional trait writes.** Follow the canonical
  Koota guidance in `AGENTS.md`: derive trivial predicates from queryable traits,
  and guard changed-value writes. TypeScript derivations need direct Vitest
  coverage.
- **Use named functions for hot-path callbacks.** ECS/query callbacks (e.g.
  Koota `updateEach`) called per frame per entity should reference named
  functions, not inline lambdas, so the closure is allocated once.
- **All `import` statements at the top of the file.** No inline imports
  (`import("...").T`). Group them at the top so dependencies are visible at a
  glance.
- **Prefer named public types over type-query indirection.** Import `World`,
  `Entity`, `IWorld`, and other exported types directly instead of spelling them as
  `ReturnType<typeof createWorld>`, `ReturnType<World["spawn"]>`, or
  `Parameters<typeof render>[0]`. Use `ReturnType`/`Parameters` only when the
  relevant shape genuinely has no named public type.
- **No magic numbers.** Extract a named `const` whose name says what the value
  _means_, and put the reasoning in a comment on the constant rather than at each
  use site. This matters most for values from a domain the reader may not know —
  graphics maths, camera and projection constants, animation tunings — where the
  literal alone carries no clue. A number repeated across a formula is the
  clearest signal.
  - **Name the role, and check the name is arithmetically true.** The same literal
    playing two roles needs two constants. In `TreeScene.tsx`'s NDC-to-pixel
    conversion, `0.5` is both a scale (one over the -1..1 span) and an offset
    (shifting the origin to the edge), so it is `NDC_TO_UNIT_SCALE` and
    `NDC_TO_UNIT_OFFSET` — not one shared name. A plausible-sounding name that is
    wrong (`0.5` is not the "half-extent" of a -1..1 range; that is `1`) is worse
    than the bare literal, because it reads as verified.
- **Name representation conversions.** Replace unexplained transforms such as
  repeated `slice(1)` with a helper whose name states the representation it
  returns.
- **Name unlabelled boolean arguments at the call site.** TypeScript has no named
  parameters, so a call like `mesh.updateWorldMatrix(true, false)` tells the reader
  nothing. Bind a `const` per argument (`UPDATE_PARENTS`/`UPDATE_CHILDREN`) so the
  call documents itself without a comment.
- **Define acronyms once, at their first appearance in the file.** Domain-standard
  acronyms are fine to use, but expand the first one — and note that "first" means
  first in reading order, so a comment on a constant declared _above_ a function
  beats that function's doc comment. Declaring such constants inside the function
  they serve is often the tidier fix, since it puts the doc comment first. When a
  function implements non-obvious maths, say in a sentence what it is doing and why
  it works, and link an external reference — the arithmetic is visible in the code;
  the concept is not.

## TypeScript tests

- **Choose matchers by contract.** `toBe` uses `Object.is`, so use it for primitive
  values and required reference identity. `toEqual` recursively compares values;
  `toStrictEqual` also pins strict shape and prototype semantics. Prefer one
  whole-value assertion over separate field assertions when the complete value is
  the contract, but do not use `toStrictEqual` on library objects whose private
  runtime fields differ despite equivalent public state (for example, compare a
  Three.js quaternion's documented `[x, y, z, w]` array).
- **Every assertion must add an independent guarantee.** Remove comparisons that
  follow logically from stronger exact assertions, repeated assertions with no
  intervening state change, and checks that merely restate an input assigned by
  the test. Keep both value equality and `not.toBe` when the contract requires
  equal-but-independently-allocated defaults.
- **Assert canonical defaults, not only agreement between instances.** Two fresh
  values can be equally wrong. Compare a factory result with its named canonical
  constructor or explicit expected value, then separately assert reference
  independence when mutability makes that significant.
- **Exercise subscription boundaries.** For reactive hooks, test the first
  add-after-mount transition as well as updates and removals of an already-present
  trait. Mounting only before or only after the trait exists can miss a broken
  subscription path.
- **Assert mathematical invariants directly.** For vector, colour, or geometry
  assertions, test the invariant itself (for example, vector distance near zero
  for alignment) rather than relying on an unstated mathematical premise.

## React + Three.js

- **Don't drive per-frame visual state through React `useState`.** A `useState`
  value combined with `useFrame(setValue)` re-renders the component every frame
  and resets to the initial value on remount, causing a one-frame flash of the
  default. Mutate the DOM/Three.js object directly via `useRef`, or use a built-in
  mechanism that does so internally (e.g. drei `<Html distanceFactor>` for
  camera-distance label scaling).
- **drei `<DragControls>` is one-instance-per-draggable-subtree.** Per-node
  `<DragControls>` instances mis-attribute the drag transform (labels move, meshes
  don't). For per-node drag behaviour, use raw `@use-gesture/react`'s `useDrag`
  (which `<DragControls>` is itself a thin wrapper around).
- **Koota tag-trait membership via `useTrait`.** `useTrait(entity, TagTrait)`
  returns `{}` (truthy) when the tag is present and `undefined` when it isn't, and
  re-renders on add/remove. Use `useTrait(entity, TagTrait) !== undefined` for a
  reactive "is this tag set?" check.
- **Distinguish architectural improvements from performance fixes.** Don't
  speculate about "GPU resource churn" or "GC pressure" without measuring; the
  React layer is deliberately thin and per-frame work happens in F# systems and
  Three.js, so React structural choices rarely dominate per-frame cost. Land
  structural refactors on architectural justification (preventing future bugs,
  simplifying lifecycle reasoning), not speculative perf wins.

## Notes

- Styling lives in `src/style.css` (global CSS with light/dark mode support).
- The Koota wrapper `src/ecs/koota/kootaWrapper.ts` bridges Koota's TS API and the
  F# ECS interfaces; the rendering system in `src/ecs/rendering.ts` synchronizes
  Koota trait changes to Three.js meshes.
