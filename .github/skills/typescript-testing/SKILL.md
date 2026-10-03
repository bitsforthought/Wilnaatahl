---
name: typescript-testing
description: >-
  Testing conventions for Wilnaatahl TypeScript and React code — strict TDD,
  Vitest matcher semantics, independent assertions, canonical defaults,
  reactive subscription boundaries, and geometry invariants. Use when writing,
  changing, or reviewing tests under tests/typescript/.
---

# TypeScript testing

TypeScript tests live under `tests/typescript/` and use Vitest. Mirror the source
layout, keep the default Node environment for pure logic, and opt into jsdom with
`// @vitest-environment jsdom` only for tests that require browser or React DOM
behaviour.

## Strict TDD

Write the failing test first and observe it fail for the intended behavioural
reason. A compile error does not prove the test can detect a broken
implementation; add the smallest type-correct stub needed to reach a failing
assertion before implementing the behaviour.

## Assertions

- **Choose matchers by contract.** `toBe` uses `Object.is`, so use it for primitive
  values and required reference identity. `toEqual` recursively compares values;
  `toStrictEqual` also pins strict shape and prototype semantics. Prefer one
  whole-value assertion when the complete value is the contract, but do not use
  `toStrictEqual` on library objects whose private runtime fields differ despite
  equivalent public state. For example, compare a Three.js quaternion's documented
  `[x, y, z, w]` array.
- **Every assertion must add an independent guarantee.** Remove comparisons that
  follow logically from stronger exact assertions, repeated assertions with no
  intervening state change, and checks that merely restate an input assigned by the
  test. Keep both value equality and `not.toBe` when the contract requires
  equal-but-independently-allocated defaults.
- **Assert canonical defaults, not only agreement between instances.** Two fresh
  values can be equally wrong. Compare a factory result with its named canonical
  constructor or explicit expected value, then separately assert reference
  independence when mutability makes that significant.
- **Assert mathematical invariants directly.** For vector, colour, or geometry
  assertions, test the invariant itself—for example, vector distance near zero for
  alignment—rather than relying on an unstated mathematical premise.

## Reactive code

Exercise subscription boundaries: test the first add-after-mount transition,
updates while the trait is present, and removal. Mounting only before or only after
the trait exists can miss a broken subscription path.

## Commands

- `npm run fake "--" --target TestTypeScript` runs the full TypeScript suite,
  including Koota conformance.
- `npm run fake "--" --target TestKoota` runs only the portable Koota conformance
  subset.
- `npm run fake "--" --target CoverageTypeScript` generates TypeScript coverage
  data.
