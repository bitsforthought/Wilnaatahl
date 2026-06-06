# Property-Based Testing in Wilnaatahl: Repo-Wide Analysis

**Author:** Copilot CLI session (handoff document)
**Date:** 2026-06-03
**Scope:** Should the Wilnaatahl test suite adopt FsCheck for property-based
testing (PBT)? If yes, where?
**Status:** Analysis only — no code changes made.

---

## TL;DR

**Adopting FsCheck repo-wide would be a coverage win, not a concision win.**
Best-case repo-wide line savings is ~1–3% (≈40–60 lines); worst-case is a
small _increase_ (~50–80 lines). The interesting story is _where_ the
coverage gains concentrate.

Recommendation: target three places and skip the rest.

| Priority                  | File                                                                               | Why                                                                                                           |
| ------------------------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| **1 (highest ROI)**       | `tests/Wilnaatahl.Core.Tests/ViewModel/VectorTests.fs`                             | Textbook math properties; flat lines, huge coverage jump                                                      |
| **1 (highest ROI)**       | `tests/Wilnaatahl.Core.Tests/ViewModel/LayoutBoxTests.fs`                          | Real geometric invariants (size composition, reframe round-trip); modest line savings                         |
| **2 (conditional)**       | `tests/Wilnaatahl.Core.Tests/Import/JsonParserTests.fs`                            | Only if a `RawFile → JsonValue` encoder is added; then a single round-trip property replaces most of the file |
| **3 (one property each)** | `tests/Wilnaatahl.Core.Tests/Systems/UndoRedoTests.fs`, `Systems/MovementTests.fs` | Single classic property each, ~10 lines added                                                                 |
| **Skip**                  | All other test files                                                               | See per-file analysis below                                                                                   |

The entire `Wilnaatahl.ECS.Tests` project (~1.4k lines, single biggest test
surface) is **excluded by the Fable portability constraint** — FsCheck is
not Fable-compatible and these tests are dual-targeted via
`#if FABLE_COMPILER`.

---

## Constraints and ground rules

Two project-specific constraints shape the recommendation:

1. **Fable portability.** `Wilnaatahl.ECS.Tests` runs against both the .NET
   mock ECS _and_ real Koota via Fable + vite-node. FsCheck's `Arb.Default`
   infrastructure depends on .NET reflection that Fable does not support.
   Any PBT adoption is .NET-only and limited to `Wilnaatahl.Core.Tests`.
2. **One-person team, single F# stack.** The maintainer has prior FsCheck
   experience, so staged piloting offers no benefit. Any "adopt FsCheck"
   decision should be implementable across the chosen files in one focused
   pass.

The analysis applies Scott Wlaschin's seven property categories
(<https://fsharpforfunandprofit.com/posts/property-based-testing-2/>):

- "Different paths, same destination" (commutativity)
- "There and back again" (round-trip / inverse)
- "Some things never change" (invariants)
- "The more things change, the more they stay the same" (idempotence)
- "Solve a smaller problem first" (structural induction)
- "Hard to prove, easy to verify"
- "The test oracle" (compare against reference implementation)

Wlaschin's main anti-pattern — _the property is just the implementation
restated_ — disqualifies more candidates than the categories qualify.

---

## Repo-wide tally

| File                          | Current LoC |   PBT estimate LoC | Coverage Δ | Verdict                     |
| ----------------------------- | ----------: | -----------------: | :--------: | --------------------------- |
| `ViewModel/VectorTests.fs`    |          74 |              70–90 |  **+++**   | **Adopt**                   |
| `ViewModel/LayoutBoxTests.fs` |         286 |            245–265 |   **++**   | **Adopt**                   |
| `Import/JsonParserTests.fs`   |         104 | 50–70 + ~30–50 src |   **++**   | **Adopt if adding encoder** |
| `ModelTests.fs`               |         283 |            290–330 |   **++**   | Skip (generator cost)       |
| `ViewModel/PaletteTests.fs`   |         253 |            240–250 |     +      | Skip                        |
| `Systems/MovementTests.fs`    |         162 |            145–155 |     +      | **Add one property**        |
| `Systems/UndoRedoTests.fs`    |         148 |            155–165 |     +      | **Add one property**        |
| `Import/ImportTests.fs`       |         466 |            490–530 |     +      | Skip                        |
| `ViewModel/SceneTests.fs`     |         367 |                367 |     –      | Skip                        |
| `Entities/*Tests.fs`          |        ~340 |               ~340 |     –      | Skip (ECS setup)            |
| Other `Systems/*Tests.fs`     |        ~440 |               ~440 |     –      | Skip (ECS scenarios)        |
| `Traits/EventsTests.fs`       |          67 |                 67 |     –      | Skip (trivial)              |
| **`Wilnaatahl.ECS.Tests/*`**  |   **~1.4k** |       **excluded** |     –      | **Out of scope (Fable)**    |
| **.NET-eligible totals**      |  **~2.99k** |   **~2.93k–3.04k** |   mixed    | ~flat ±2%                   |

Coverage Δ legend: `+++` major, `++` significant, `+` modest, `–` none.

---

## Tier 1: Adopt (highest ROI)

### `ViewModel/VectorTests.fs` (74 lines)

Pure math operations on `Vector3` — the textbook PBT case. Most existing
facts pin a single numeric example for an operation whose spec is a
universal property.

**Properties to replace existing facts:**

| Existing fact                                             | Replace with property                                                                                                                                                                                                  |
| --------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Addition of two vectors`                                 | `∀ a b, a + b = b + a` (commutativity); `∀ a b c, (a + b) + c = a + (b + c)` (associativity); `∀ a, a + Vector3.Zero = a` (identity)                                                                                   |
| `Subtraction of two vectors`                              | `∀ a b, (a + b) - b = a` ("There and back again")                                                                                                                                                                      |
| `Cross product of two vectors`                            | `∀ a b, a × b = -(b × a)` (anti-commutativity); `∀ a b, (a × b) · a = 0` and `(a × b) · b = 0` (orthogonality)                                                                                                         |
| `Scalar multiplication` (×2 facts)                        | `∀ a s, a * s = s * a` (commutativity); `∀ a, a * 0.0 = Vector3.Zero`; `∀ a, a * 1.0 = a`                                                                                                                              |
| `Dot product`                                             | `∀ a b, a · b = b · a` (commutativity); `∀ a, a · a = (length a)²`                                                                                                                                                     |
| `Division by scalar`                                      | `∀ a s where s ≠ 0, (a * s) / s = a` (inverse)                                                                                                                                                                         |
| `length of known vector`                                  | `∀ a, length a ≥ 0`; `length Vector3.Zero = 0`; `∀ a s, length (a * s) = abs s * length a`                                                                                                                             |
| `normalize of known vector`                               | `∀ a where length a > epsilon, abs (length (normalize a) - 1.0) < epsilon` (unit length); `normalize` preserves direction (`normalize a · a > 0` when `length a > 0`)                                                  |
| `normalize of zero vector` (edge case)                    | Keep as fact — boundary behaviour with specific expected value                                                                                                                                                         |
| `max` / `min` (component-wise)                            | `∀ a b, max a b ≥ a` and `≥ b` componentwise; `min a b ≤ a` and `≤ b`; `∀ a, max a a = a`; `∀ a b, max a b = max b a`                                                                                                  |
| `lerp at boundary and midpoint alphas` (already a Theory) | `∀ a b, lerp a b 0.0 = a`; `∀ a b, lerp a b 1.0 = b`; `∀ a b α, lerp a b α = lerp b a (1.0 - α)`                                                                                                                       |
| `damp approaches target`                                  | `∀ from to λ δt where λ > 0 ∧ δt > 0, length (to - damp from to λ δt) ≤ length (to - from)` (monotonic convergence); `∀ from to λ, damp from to λ 0.0 = from`; `damp` is bounded between `from` and `to` componentwise |

**Floating-point care:** All equality assertions on derived values must use
an epsilon comparison, not `=!`. Define a single helper:

```fsharp
let private approxEq (epsilon: float) (a: float) (b: float) =
    abs (a - b) < epsilon

let private vec3ApproxEq epsilon (a: Vector3) (b: Vector3) =
    approxEq epsilon a.x b.x
    && approxEq epsilon a.y b.y
    && approxEq epsilon a.z b.z
```

Generators must restrict floats to a safe range (e.g. `-1e6 ≤ x ≤ 1e6`) so
that intermediate squared values don't overflow or accumulate intolerable
rounding. FsCheck's default `float` generator produces `NaN`, `infinity`,
and subnormals — filter or use a custom `Gen.choose`-based generator. Skip
properties for `normalize` when `length a < epsilon` (undefined-behaviour
case is already covered by the existing zero-vector fact).

**Line count:** ~74 → ~70–90. Approximately flat. **Coverage:** dramatic —
from ~17 cherry-picked vectors to thousands of arbitrary inputs covering
sign, magnitude, and near-zero edge cases.

**Effort:** ~half a day. Lowest-risk file to start with.

---

### `ViewModel/LayoutBoxTests.fs` (286 lines)

Geometric composition over typed-unit vectors (`<w>`, `<u>`, `<l>`). Several
real invariants exist that the current example-driven tests only sample.

**Properties:**

- **`reframe` round-trip ("There and back again"):**
  `∀ v: LayoutVector<w>, ∀ k: float<u/w> where k ≠ 0.0<_>, reframe (1.0<w/u> / k) (reframe k v) ≈ v`
  (and likewise for `LayoutBox.reframe`, which is the recursive case — this
  property exercises the whole tree-reframing path with arbitrary nested
  structures).
- **`attachHorizontally` size composition ("Some things never change"):**
  `∀ left right, (attachHorizontally left right).Size.X = left.Size.X + right.Size.X`
  and the Y/Z components equal `max` of the inputs (whatever the actual
  rule is — verify against the implementation, then encode as a property).
  Existing `Theory` blocks with `[<InlineData(0.0<w>, 3.0<w>, ...)>]`
  collapse into one bounded-real property.
- **`attachAbove` symmetry:** the upper-vs-lower distinction is asymmetric
  by design, but `connectX` should respect documented rules across all
  inputs.
- **Follower count preservation:** `∀ box1 box2, (attachX box1 box2).Followers.Length = box1.Followers.Length + box2.Followers.Length + correction`
  (verify the correction term against the implementation).
- **`createLeaf`/`createComposite` are constructors** with no logic to
  verify beyond record equality — keep as facts.

**Generator considerations:** `LayoutBox` is recursive (Composite → list of
follower boxes). Use FsCheck's size parameter to bound recursion depth.
Restrict component widths to the units-of-measure `<w>`, `<u>`, `<l>` the
production code uses; FsCheck handles UoM via a custom `Arbitrary` wrapper.

**Line count:** ~286 → ~245–265. Net **~20–40 line saving** with much
broader coverage of widths and nesting depths.

**Effort:** ~1 day, including generator setup.

---

## Tier 2: Conditional (worth doing if you're already changing the surface)

### `Import/JsonParserTests.fs` (104 lines)

The dominant test pattern is "construct JSON string, parse it, assert
fields." The natural PBT property is **round-trip** ("There and back
again"): `∀ rawFile, parseJson (encode rawFile) = Ok rawFile`. But
`Wilnaatahl.Import` does not currently have an encoder — only a parser.

**Two paths:**

1. **Add a `RawFile → string` encoder** (~30–50 lines in
   `src/Wilnaatahl.Core/Import/JsonParser.fs`). Then a single round-trip
   property + 2–3 explicit facts for "ignores unknown fields" and
   "malformed JSON returns Error" replaces ~80 of the 104 lines.
   - **Net codebase Δ:** roughly flat (test savings offset by new src).
   - **Coverage Δ:** large. Catches asymmetries the current forward-only
     tests can't detect (e.g. a field rename in one direction).
   - **Independently useful:** an encoder enables JSON export of edited
     trees, golden-file regression tests, and integration tests that
     construct JSON from typed F# values.
2. **Skip if no encoder is desired.** Current example tests are concise and
   correct; a property-only rewrite without an encoder would have to
   generate raw JSON strings, which is awkward and brittle.

**Recommendation:** add the encoder if and only if there's product value
(export feature, integration testing). Don't add it solely to enable PBT.

**Effort:** 1–2 days including encoder.

---

## Tier 3: Add one property each (small, high-value additions)

### `Systems/UndoRedoTests.fs` (148 lines)

The textbook property `∀ world ops, redo (undo (apply world ops)) = apply world ops`
(undo · redo = identity) belongs here. The rest of the file is
event-sequencing scenarios (drag-start enables button, drag-end captures
end state) that require scripted ECS world setup and are hard to generate
meaningfully.

**Action:** add one `[<Property>]` for the identity round-trip. Keep all
existing facts. **Effort:** ~half a day including a small `Arbitrary` for
operation sequences.

### `Systems/MovementTests.fs` (162 lines)

The "parallel-line preservation" rule (already covered by an `InlineData`
Theory) generalizes well: `∀ line, ∀ offset δ, move line δ` preserves the
direction vector. Specific-geometry tests stay as examples.

**Action:** one property added; the existing Theory could be retired if its
two rows are subsumed. **Effort:** ~2 hours.

---

## Skip — coverage neutral or generator cost dominates

### `ModelTests.fs` (283 lines)

Strong candidate properties exist (`findPerson` round-trip, `huwilp` set
invariant, child enumeration determinism), but generating an arbitrary
_valid_ `FamilyGraph` requires a generator with cross-record validity
(couple members must be valid `PersonId`s in the graph; parent `CoupleId`s
on persons must reference real couples). That generator is ~40–80 lines.
The existing fixture-based tests using `TestData.testPeopleAndParents` are
already very concise. **Coverage win significant; concision is a wash or
slight loss.** Re-evaluate if the model gains new invariants (e.g.,
acyclic-parents check) that benefit from generated counterexamples.

### `ViewModel/PaletteTests.fs` (253 lines)

The `djb2Hash` accumulator property is already encoded as one Fact. A real
PBT property worth adding:
`∀ s, lightnessOffsetFromHash (djb2Hash s) ∈ [-wobble, +wobble] \ [-floor, +floor]`
(range and exclusion-band invariant). The kinship→colour mapping tests
anchor specific colours and don't generalize meaningfully. **~5–15 line
savings; modest coverage win.** Not worth standalone effort but include if
adopting PBT anyway.

### `Systems/MovementTests.fs` (162 lines) — partial

See Tier 3 above. Only one property is a strong fit; rest stay as examples.

### `Import/ImportTests.fs` (466 lines)

Per prior analysis (full notes preserved in session history): the three
PBT-friendly clusters (pdeek normalization, duplicate handling, date
parsing) would _grow_ the file by ~25–65 lines because the existing
`TheoryData` approach is already at minimum per-case cost (~1 line/row),
while PBT adds fixed overhead (generators, `Prop.forAll` harnesses,
separate properties per assertion shape). The one genuine bug-finding win
— untested orthographic equivalences in pdeek normalization — could be
captured with ~3 properties at the cost of ~10–20 lines added on top of
the existing table. **Worth doing only if the team values bug-finding
coverage of the orthography normalizer specifically.**

### `ViewModel/SceneTests.fs` (367 lines)

Family extraction logic against specific tree shapes. Generating arbitrary
graphs that exercise the _interesting_ branches (mixed childless +
procreative couples, multi-Wilp sibling groups, ordering rules) requires
shrink-rich generators that effectively encode the test taxonomy. Still
need explicit examples for the specific topologies the layout depends on.
**No saving.**

### `Entities/{BoundingBox,Connectors,Line,People}Tests.fs` (~340 lines combined)

ECS spawn-and-inspect tests. The "property" of spawning a Line is "after
`spawn`, the entity has these N traits" — which is the implementation
restated. Wlaschin's main anti-pattern. **No saving, no coverage gain.**

### Other `Systems/{Animation,Dragging,Layout,LifeCycle,Runner,Selection}Tests.fs` (~440 lines combined)

ECS scenario tests with scripted event sequences. World state is hard to
generate meaningfully; existing tests are scenario-driven and concise.
**No saving.**

### `Traits/EventsTests.fs` (67 lines)

Trait round-trip and tagging — trivial setup verification. **No saving.**

### `Wilnaatahl.ECS.Tests/*` (~1.4k lines)

**Excluded by Fable constraint.** FsCheck depends on .NET reflection that
Fable doesn't support. Even good properties exist for the Koota
conformance suite ("trackers are idempotent across drain", "Added then
Removed = no observable change"), but capturing them would require a
Fable-compatible mini-framework. Out of scope.

---

## Adoption sequence (if proceeding)

If the maintainer chooses to adopt:

1. **Add the NuGet dependency** in `tests/Wilnaatahl.Core.Tests/Wilnaatahl.Core.Tests.fsproj`:
   `FsCheck.Xunit` (the xUnit 3 integration package). Verify it integrates
   with the existing `[<Theory>]` machinery without conflict.
2. **Land Tier 1.A: `VectorTests.fs`.** Lowest-risk, no custom generators
   needed beyond a bounded `float` Arb. Run `npm run coverage:check` to
   confirm baseline holds.
3. **Land Tier 1.B: `LayoutBoxTests.fs`.** Requires UoM Arb wrappers and a
   size-bounded recursive generator for `LayoutBox`. Re-run coverage.
4. **(Optional) Tier 2: `JsonParserTests.fs`** if the encoder lands.
5. **(Optional) Tier 3:** one property each in `UndoRedoTests.fs` and
   `MovementTests.fs`.
6. **Document the convention.** Add a short note to `AGENTS.md` describing
   when to reach for `[<Property>]` vs `[<Theory>]` vs `[<Fact>]`. Suggest
   keeping the rule simple: **PBT for universal mathematical properties;
   typed `TheoryData` for enumerated equivalence classes; `Fact` for
   single-scenario examples.**

---

## Risks and trade-offs to flag for the next agent

1. **Floating-point flakiness.** Any property over `Vector3` math must use
   epsilon comparison, not exact equality. Generators must restrict float
   range to avoid overflow and reject `NaN`/`infinity`. The first failing
   property due to a too-tight epsilon will be a credibility hit — pick
   bounds carefully.
2. **Generator validity is half the work.** For `FamilyGraph`,
   `RawFile`, and similar cross-referencing types, _invalid_ generated
   inputs will exercise error paths rather than the intended property.
   Generators must construct _only valid_ values, which is non-trivial.
   This is the reason `ModelTests.fs` and `ImportTests.fs` are skipped.
3. **Shrinking matters.** Bad shrinkers produce unintelligible
   counterexamples. FsCheck's default shrinkers work well for primitives
   and small records but degrade for recursive types. Verify shrinks are
   readable on a deliberate failing property before committing.
4. **Fable constraint is non-negotiable.** Do not attempt to bring FsCheck
   into `Wilnaatahl.ECS.Tests`. The portable-test contract there is more
   important than the PBT opportunity.
5. **Coverage baseline.** `npm run coverage:check` enforces a baseline. PBT
   typically _improves_ coverage but with non-deterministic seeds the
   exact line count of hit branches may vary slightly run-to-run. If this
   causes baseline drift, pin FsCheck's seed in `xunit.runner.json` or via
   the `[<Property>]` attribute.
6. **Don't trust this analysis blindly.** Verify a few of the line counts
   and the structure of the candidate files before committing to scope —
   the analysis was performed by sampling representative files rather than
   reading every test top to bottom. The high-confidence claims are about
   `VectorTests.fs`, `LayoutBoxTests.fs`, and `JsonParserTests.fs`, which
   were read in full or near-full.

---

## Open questions for the maintainer

1. Is there product value in a JSON encoder (export, golden-file testing,
   round-trip testing)? If yes, Tier 2 becomes Tier 1.
2. Is the orthography normalizer in `ImportTests.fs` considered "stable"
   or are new spellings likely to be added? If the latter, the
   `~10–20 line` cost of adding pdeek properties is worth it for the
   bug-finding value even though it doesn't shrink the file.
3. Is there appetite for adding `FsCheck.Xunit` as a test-only dependency
   given the maintenance footprint (one more thing to track for SDK/.NET
   version compatibility)?
