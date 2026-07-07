---
name: fsharp-testing
description: >-
  Testing philosophy for Wilnaatahl F# tests — strict TDD, writing tests that
  cover logic (not just lines), exception-message contracts, the right test
  attribute, fixtures, and portability of ECS tests. Use when writing, adding,
  or strengthening any test in Wilnaatahl.Core.Tests or Wilnaatahl.ECS.Tests.
---

# Testing Philosophy

All public F# members must have direct test coverage, including edge cases. Use
direct equality assertions (e.g., `x =! y`), not pattern matching or mutation.

## Strict TDD

- **TDD is strict.** Write failing tests first, observe the failure, then
  implement. Don't skip the red phase — it validates the test itself. When
  planning, structure todos so that tests for each unit of work come _before_ the
  corresponding implementation — don't batch all tests into a single step at the
  end.
- **A RED test must compile and fail for the right reason.** A test that fails to
  compile is not RED — that only proves a name is missing. Add a returning-stub
  implementation (e.g. `let f _ = 0u`) so the test compiles, runs, and fails its
  assertion because the behaviour isn't yet implemented. Only then is the test
  verified strong enough to catch an absent or trivial implementation.

## Logic coverage, not line coverage

Hitting a line is not the same as exercising its logic. A test suite that passes
coverage thresholds can still miss fundamental cases. For every unit under test,
deliberately enumerate:

- **Equivalence classes** of inputs (each distinct behaviour branch), not just
  one happy-path value.
- **Boundaries** (empty, single, many; min/max; first/last; off-by-one edges).
- **Error/exception paths** with their exact messages (see below).
- **Tests must be strong.** Every assertion should verify observable behavior
  that would fail if the code-under-test were broken. Avoid tautological tests
  (e.g., checking a value didn't change when nothing could have changed it).
- **Don't silently weaken tests.** If an assertion is removed, explain why it was
  necessary. Removing assertions to make things compile is not acceptable.
- **Primitives of fundamental types deserve direct tests.** The "all public F#
  members have direct test coverage" rule extends to `internal` primitives of
  fundamental building blocks like `FamilyGraph`. Don't rely on transitive
  coverage from product callers — a single direct test that names the primitive
  and exercises its edge cases is far more diagnostic when something breaks later.

## Exception messages are part of the contract

- **Assert on them.** For fail-fast guards and other error paths, assert the
  _exact_ thrown message, not merely that an exception was raised. A "throws vs.
  doesn't" test can't distinguish two code paths that both throw, so it silently
  misses message/ordering regressions. Pin the message in a shared constant (e.g.
  `TestInfra.relationValueUpdateEachError`) and assert `message =! Some expected`
  via a portable capture helper (`captureExceptionMessage`). In portable ECS
  tests this also cross-checks that the independent .NET mock (`TestECS.fs`) and
  TypeScript wrapper (`kootaWrapper.ts`) produce byte-identical messages on both
  backends.

## Assertions

- **Use Unquote operators** (`=!`, `<>!`, `>!`, `<!`, `>=!`, `<=!`) for
  single-operator assertions. Use `test <@ expr @>` only for complex
  multi-operator boolean expressions where splitting into individual assertions
  would reduce readability. Never use `test <@` in portable ECS tests (Fable
  doesn't support quotations).
- **Use F# structural equality.** Compare records, tuples, and other structured
  types as whole values rather than deconstructing them into components. For
  `Vector3` (returned by `Line3.getPositions`), use `=! Vector3.FromComponents(x,
y, z)`. For frozen Position values (anonymous records from `get Position`), use
  `=! Line3.pos x y z`. Do not deconstruct structured data into a tuple only to
  compare the tuple.
- **No magic numbers in tests.** Extract constants with descriptive names and
  comments explaining the chosen value (e.g., `let frameDelta = 0.016 // one frame
at 60 FPS`).

## Choosing the right test attribute

Use `[<Property>]` (FsCheck) for _universal_ properties — mathematical laws,
round-trip/inverse pairs (e.g. `JsonReader.read ∘ JsonWriter.write`), and
metamorphic invariants (e.g. pdeek orthography normalization); use `[<Theory>]`
with `TheoryData`/`MemberData` for _enumerated_ equivalence classes (a fixed table
of input→output cases); use `[<Fact>]` for single-scenario examples and boundary
cases. Keep a few concrete facts alongside a round-trip/inverse property when the
property can't pin direction or wire format (a two-directional rename passes
round-trip). For guidance on _which_ properties to look for, see Scott Wlaschin's
["Choosing properties for property-based
testing"](https://fsharpforfunandprofit.com/posts/property-based-testing-2/).

PBT is **.NET-only** via `FsCheck.Xunit.v3` (3.3.x with xunit.v3); the F# API —
`Gen`, `Arb`, `Prop`, `gen { }`, `==>` — lives under `open FsCheck.FSharp`. Never
add FsCheck to the Fable-portable `Wilnaatahl.ECS.Tests`. For float properties use
epsilon comparisons and bounded, NaN/infinity-free generators; over `internal`
types use `Prop.forAll` so the type stays out of the public property signature (a
public `[<Property>]` can't take an `internal` parameter — FS0410); don't pin the
FsCheck seed (random seeds find more bugs, and failures still print a reproducible
replay seed).

xUnit `MemberData` sources must be non-private `let` bindings in F# modules (xUnit
needs public static members). Use a **typed** `TheoryData<...>` with `struct
(...)` tuple rows so the theory parameters stay strongly typed — no `obj[]` arrays
and no per-cell `box`. See `TransformTests.fs` (e.g. `TheoryData<string, Pdeek>`,
`TheoryData<DateField, string option, DateOnly option, ImportWarning list>`).

## Fixtures and test data

- **Use xUnit class fixtures** for tests with repetitive setup. Shared setup goes
  in the constructor; cleanup via `IDisposable`. See `TrackingTests` and
  `PeopleTests` for the pattern. Module-level functions are fine for tests with
  minimal shared setup.
- **Test data independence.** Tests in `Wilnaatahl.Core.Tests` should use
  `TestData.testPeopleAndParents` (stable test data), not `Initial.peopleAndParents`
  (app seed data that may change).
- **Share test fixtures through `TestData.fs`.** When multiple tests need the same
  shape of setup (e.g. a prototypical childless Couple, or a handful of anonymous
  Persons to wire Couples between), factor it into `TestData.fs` rather than
  re-declaring per-test. This shrinks test bodies, makes each test's _intent_
  obvious, and keeps fixture tweaks from rippling through many files.

## Portability of ECS tests

- **The portable surface is minimal and purpose-specific.**
  `Wilnaatahl.ECS.Tests` exists **solely** to prove the .NET mock (`TestECS.fs`)
  and the Koota wrapper (`kootaWrapper.ts`) are behaviourally equivalent, so tests
  there must run against both backends. That equivalence is precisely what lets
  everything else — domain, view model, and F# app/ECS-**system** logic — be
  tested with confidence in .NET-only `Wilnaatahl.Core.Tests`. Keep the portable
  surface to the minimum needed to establish wrapper/mock equivalence; do **not**
  port app or system tests into it just because they touch the ECS.
- **Koota is the gold standard.** The mock must match Koota's behavior exactly,
  even when that behavior appears buggy or inconsistent. Document known Koota bugs
  with issue links, but replicate them faithfully.
- **Test the lowest common denominator.** When the mock is more permissive than
  Koota, constrain tests to what Koota supports (e.g., object schemas instead of
  primitive values for traits).
- **Minimize conditional compilation.** Encapsulate platform differences in shared
  infrastructure types rather than sprinkling `#if` throughout test bodies.

## Naming

- **Tests assert behaviour, not provenance.** Test names and comments should
  describe the behaviour being checked — not where the expected values came from,
  what the code used to look like, or what other implementation it matches.
  Phrasing like "matches the TS reference" or "after the port" decays into noise
  once the prior implementation is gone. If a test name doesn't obviously match
  what the body asserts, rewrite the body to demonstrate the named property (e.g.
  don't title a test "accumulates over multiple characters" if it only asserts a
  single hash value — show the accumulation step explicitly).
