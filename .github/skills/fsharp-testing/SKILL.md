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
- **Compare whole values; use sets when order isn't guaranteed.** Assert on the
  entire record/DU/collection and let F#'s built-in structural equality do the
  work — don't compare field-by-field or hand-roll a "same elements" helper. When
  the result has no guaranteed order (a warning list, holdings across holders),
  compare as sets (`Set.ofList actual =! Set.ofList expected`); never sort both
  sides and compare lists (sorting is itself a tell that order isn't part of the
  contract).
- **Define a minimal/`Empty` record once; set only what matters with `with`.** For
  record types with many fields, declare one baseline value at the top of the file
  (e.g. `emptyRawPerson`, or a `minimalFile` with all-empty arrays) and build each
  case as `{ baseline with Field = … }`. Adding a field later then touches one
  baseline, not every literal.
  - ✅ `(readBack graph).People =! [ { defaultRawPerson with Id = 0 } ]`
  - ❌ a full record literal repeating every field in every test.
- **Assert on the whole result as one declarative expression.** Compare the value a
  function returns against an expected literal in a single `=!` — including its
  `Ok`/`Some` wrapper, so success _and_ shape are pinned at once. Do **not** unwrap
  a `Result`/`Option` with an inline `match … | Ok x -> <assertions> | Error _ ->
failwith …` and nest the assertions in the success branch: that is procedural
  transliteration, buries the assertions, and turns the error branch into
  boilerplate. When the expected value _can_ be a literal, assert it directly.
  - ✅ `Transform.toJson graph |> fromJson =! Ok { PeopleAndCoupleIds = [ … ]; Couples = []; Warnings = [] }`
  - ✅ `importJsonText json =! Error EmptyPeopleArray` (nullary error case — pin it directly)
  - ❌ `match Transform.toJson graph |> fromJson with | Ok r -> r.Warnings =! []; … | Error e -> failwithf "…" e`
  - When the result is only _partly_ comparable (an opaque `FamilyGraph`) or must
    be compared order-independently (sets), lift the value to the top level with a
    small unwrap-or-fail helper (`readBack`, `roundTrip`, `importOrFail`) — one
    reusable helper, not an inline `match` per test — then assert directly on it.
  - Verbosity is not the enemy: a large expected literal is easier to read than a
    terse chain of projections and pattern matches. Prefer the literal.
- **No magic numbers in tests.** Extract constants with descriptive names and
  comments explaining the chosen value (e.g., `let frameDelta = 0.016 // one frame
at 60 FPS`).
- **Pass `[]` for an empty `seq<_>` argument, not `Seq.empty`.** F# coerces a list
  literal to `seq<_>` at the call site, so `[]` reads as an ordinary empty
  collection and lines up with the non-empty literals the same parameter takes
  elsewhere in the file.
  - ✅ `createFamilyGraph testPeopleAndParents testCouples []`
  - ❌ `createFamilyGraph testPeopleAndParents testCouples Seq.empty`

## Choosing the right test attribute

**Repetition in a test file is the signal to reach for `[<Property>]` or
`[<Theory>]`.** Before writing a third `[<Fact>]` that varies one argument, stop
and ask which of the three attributes fits — and if you keep `[<Fact>]`, say in a
comment or the review notes why. In particular:

- Several facts that differ only in a **boolean or enum argument** are an
  enumerated table: use `[<Theory>]`, or — when the argument's contract is
  _universal_ ("this flag never affects any field but one") — a `[<Property>]`
  that states the contract once and subsumes all the rows.
- Several facts that differ only in **which constructor of a DU** they feed in are
  an enumerated table: use `[<Theory>]` with a `TheoryData` row per case.
- A fact that restates the implementation line-for-line usually has a property
  hiding behind it. Look for the invariant the example is an instance of
  (idempotence, "output differs only in field X", "never equals Y", round-trip).

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

A property is only as good as its generator. **Draw strings and names from a small
pool** when the behaviour under test turns on two inputs _colliding_ (e.g. a held
Name that equals a Wilp name): FsCheck's default string generator will essentially
never produce the collision, so the interesting branch stays untested. **Scope the
property to the cases where it actually holds** and say why in the doc comment —
an invariant quietly widened to cases it doesn't cover is worse than no property.

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
- **Never coin a Gitxsan Name, Wilp, or Pdeeḵ for test data.** Gitxsan Names are
  real hereditary property, not filler. Sample Names are ordinary, easily-understood
  English nicknames — `Tinker`, `Sparks`, `The Mayor`, `Lefty`, `Doc` — and Wilp
  names in fixtures are short opaque tokens like `"H"`, `"L"`, `"MM"`. Avoid
  Gitxsan-sounding coinages and the animal/clan imagery that model training data
  gravitates toward; a Gitxsan word used as throwaway data is wrong even when it
  happens to be spelled correctly. This governs _filler_ only: a Gitxsan string
  that is the subject of the test — a `Pdeeḵ` spelling fed to the parser, say — is
  the thing under test, not decoration.
- **Share test fixtures through `TestData.fs`.** When multiple tests need the same
  shape of setup (e.g. a prototypical childless Couple, or a handful of anonymous
  Persons to wire Couples between), factor it into `TestData.fs` rather than
  re-declaring per-test. This shrinks test bodies, makes each test's _intent_
  obvious, and keeps fixture tweaks from rippling through many files.
- **A helper belongs at the widest scope that uses it — declare it there the
  first time.** Repeating a multi-step sequence (drive a drag, click a button,
  find an entity by label) buries the one line that differs between tests in noise
  the reader has to diff by eye. Two failure modes to avoid, both seen in this
  repo:
  - _Introducing the helper inconsistently_ — factoring the sequence out in one
    test while the next spells it out longhand, so the file reads as though the
    two are doing different things when they aren't.
  - _Copying the helper instead of hoisting it_ — re-declaring the same `let
click btn = …` inside a second test rather than moving the first one up to
    the fixture. A local helper that a second test wants is the signal to hoist,
    not to duplicate.
    So: before writing a sequence, check whether a sibling test already has it; if
    it does, hoist that one to the fixture (or to module scope if it needs no
    world). **Check the shared modules too** — `TestData.fs` already carries helpers
    like `on year month day` for building an optional `DateOnly`, and re-spelling
    one inline (`Some(DateOnly(1950, 1, 1))`) is the same duplication with a wider
    radius. Prefer a helper that takes the varying value as a parameter
    (`dragNodeTo node x`) over one that closes over a specific entity.
- **Wait on the end state, not on a frame count.** A loop like `for _ in 1..100 do
runSystems world 0.1` asserts nothing and silently under-runs if the tuning
  changes. Loop until the condition the test depends on actually holds, with a
  bounded number of iterations and an assertion that it was reached — see
  `RunnerTests.runUntilSettled`.

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

- **Capitalize the first word of a test name, unless it is an identifier.** Test
  names read as sentences, so they start like one. The exception is a name that
  opens by naming the thing under test: leave a camelCase identifier exactly as it
  is spelled in the code, because changing its case makes it a different (and
  non-existent) identifier.
  - ✅ `` `A drag that ended where it began records nothing` ``
  - ✅ `` `runSystems keeps redo history when a stray drag end arrives` `` — the
    subject is the function `runSystems`.
  - ✅ `` `Command.create rejects an empty list of moves` `` — already capitalized,
    because the identifier is.
  - ❌ `` `a drag that ended where it began records nothing` ``
  - ❌ `` `RunSystems keeps redo history when a stray drag end arrives` ``
  - Older files predate this rule and are inconsistent; fix names in a file you are
    already changing, rather than in a sweep of its own.
- **Tests assert behaviour, not provenance.** Test names and comments should
  describe the behaviour being checked — not where the expected values came from,
  what the code used to look like, or what other implementation it matches.
  Phrasing like "matches the TS reference" or "after the port" decays into noise
  once the prior implementation is gone. If a test name doesn't obviously match
  what the body asserts, rewrite the body to demonstrate the named property (e.g.
  don't title a test "accumulates over multiple characters" if it only asserts a
  single hash value — show the accumulation step explicitly).
- **Name the concrete behaviour; no metaphors, no abstract placeholders.** A test
  name is read by someone who does not have the code in front of them, so it has
  to say what actually happens. Two habits to drop:
  - _Words that mean something else in software._ "threads X into Y", "gated by
    Z", "wires up W" — `thread` and `gate` have established technical meanings, so
    they read as claims about concurrency or control flow rather than as
    description. Say what the code produces: "puts a person's most recent held
    Name in the composed label", not "threads namesHeldBy into the label".
  - _Naming a parameter or condition without saying what it is._ Calling a
    Boolean "the gate" or "the flag" tells the reader nothing; name the condition
    it expresses ("a person outside the rendered Wilp").
