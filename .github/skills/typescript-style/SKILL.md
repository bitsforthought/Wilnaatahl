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
- **Use named functions for hot-path callbacks.** ECS/query callbacks (e.g.
  Koota `updateEach`) called per frame per entity should reference named
  functions, not inline lambdas, so the closure is allocated once.
- **All `import` statements at the top of the file.** No inline imports
  (`import("...").T`). Group them at the top so dependencies are visible at a
  glance.

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
