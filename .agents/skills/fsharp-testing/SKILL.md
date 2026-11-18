---
name: fsharp-testing
description: >-
  F# test assertions, test forms, fixtures, and runtime selection. Use when
  writing or reviewing F# tests.
---

# F# testing

Read [shared testing standards](../../../docs/testing.md) when authoring or
reviewing tests; they own test-first evidence, logical coverage, and the
intentional internal-seam and checked-fake exceptions to the `tdd` skill.

## Select the runtime

- `Wilnaatahl.Core.Tests`: .NET-only domain, view-model, and app/system tests
  against the ECS fake. Most tests belong here.
- `Wilnaatahl.ECS.Tests`: minimal mock/wrapper conformance on both .NET and real
  Koota through Fable. Use `[<Fact>]` only: the portable harness invokes each
  method once without data arguments. No theories, FsCheck, or Unquote quotations.
  When changing or reviewing these tests, read [portable ECS contracts](portable-ecs.md).

## Assertions

Use Unquote operators (`=!`, `<>!`, `>!`, `<!`, `>=!`, `<=!`) for single-operator
assertions. Reserve `test <@ expr @>` for complex multi-operator booleans whose
separate assertions would reduce readability; it is unavailable in portable tests.

Compare whole records, DUs, tuples, and collections with structural equality,
including the `Ok`/`Some` wrapper. Prefer a plain expected literal to projections,
field-by-field checks, or an inline success/error `match` around assertions:

```fsharp
importJsonText json =! Error EmptyPeopleArray
```

For partly comparable results (an opaque `FamilyGraph`) or unordered output,
use a shared unwrap-or-fail helper (`readBack`, `roundTrip`, `importOrFail`).
Compare unordered collections as sets, not sorted lists or a custom
"same elements" helper. A large expected literal is preferable to a terse chain
of projections. For `Vector3`, assert `=! Vector3.FromComponents(x, y, z)`;
for frozen Position records, use `=! Line3.pos x y z`, not deconstructed tuples.

Fail-fast guards and error paths must assert the exact exception message, not
only that an exception occurred. Pin shared expected messages in a constant
(for example `TestInfra.relationValueUpdateEachError`) and use
`captureExceptionMessage` with `message =! Some expected`. This distinguishes
error ordering and paths that both throw.

For large records, define one minimal/`Empty` baseline and use `{ baseline with
Field = ... }` per case. Pass `[]`, not `Seq.empty`, for an empty `seq<_>` argument.

## Select a test form

Use `[<Fact>]` for a single scenario/boundary, `[<Theory>]` for enumerated
equivalence classes, and `[<Property>]` for universal invariants in Core.Tests.
Before a third fact varying one argument, consider a theory or property;
justify retaining separate facts in a comment or review notes. Portable
conformance tests retain the Fact-only exception above.

When adding/reviewing a theory, property, repeated facts, or a candidate invariant,
read [test forms and generators](test-forms.md) for discovery, directionality,
generator quality, and F# visibility constraints.

## Fixtures and names

When constructing, changing, or reviewing test setup, sample data, shared helpers, or
frame-driving code, read [fixtures and test data](fixtures.md).

Capitalize test sentences except when they start with an exact camelCase
identifier: `` `runSystems keeps redo history when a stray drag end arrives` ``.
Correct names in files already being edited rather than sweeping unrelated files.
Name concrete behaviour, not provenance or migration history. Avoid metaphors
such as "threads", "gated", or "wired"; state the condition and result instead
of calling an argument "the flag". Ensure the body demonstrates the named
property, not merely a special case.
