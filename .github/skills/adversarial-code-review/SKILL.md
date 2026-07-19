---
name: adversarial-code-review
description: >-
  The adversarial review rubric for Wilnaatahl changes — the high-signal,
  project-specific checks a reviewer applies to a diff (logic-vs-line coverage,
  F# idiom violations, weakened/tautological tests, doc-comment drift, dead
  code, exception-message contracts). Use when reviewing any change before it
  is declared done.
---

# Adversarial code review rubric

Review the change as an adversary trying to break it, not as its author. Surface
only issues that genuinely matter — bugs, logic errors, missing cases, idiom
violations, weakened tests. Do not comment on formatting (Prettier/Fantomas own
that) or trivia. For each finding, state the concrete failure it would cause.

This rubric is deliberately tuned to the three failure modes most often seen in
generated changes here.

## 1. Logic coverage vs line coverage

The change can pass `coverage:check` and still be under-tested. For every unit
touched, ask:

- Are all **equivalence classes** of input exercised, or only one happy path?
- Are **boundaries** covered (empty/single/many, min/max, first/last, off-by-one)?
- Is every **error/exception path** tested, asserting the **exact message** (not
  just "it threw")? See the `fsharp-testing` skill for the contract.
- Are tests **strong** — would each assertion actually fail if the code were
  broken? Flag tautological assertions (checking a value didn't change when
  nothing could have changed it).
- For `internal` primitives of fundamental types (e.g. `FamilyGraph`), is there a
  **direct** test, or only transitive coverage through callers?
- **A RED test that only failed to compile was never RED.** Confirm new tests
  fail for the right reason against a returning stub, not because a name is
  missing.
- **Imperative-style assertions.** Flag tests that unwrap a `Result`/`Option` with
  an inline `match … | Ok x -> <assertions> | Error _ -> failwith …` and nest the
  checks in the success branch, rather than asserting on the whole result in one
  declarative `=!` (e.g. `f x =! Ok expectedLiteral`, or lifting the value via a
  reusable unwrap-or-fail helper like `readBack`/`importOrFail` and asserting
  directly). Also flag whole-value checks re-expressed as terse projections/tuples
  where a plain expected literal would read more clearly — verbosity is fine when
  it aids understanding. See the `fsharp-testing` skill.
- **Repetitive facts that should be a `[<Theory>]` or `[<Property>]`.** Flag runs
  of `[<Fact>]`s that differ only in one boolean/enum argument, or only in which
  DU case they feed in. These are enumerated tables (`[<Theory>]` with
  `TheoryData`) or, when the argument's contract is universal ("this flag affects
  no field but one"), a single `[<Property>]`. Do not accept "the change didn't
  introduce PBT" as a reason to skip the question — a diff that adds repetitive
  facts must justify why a property or theory was rejected. Conversely, flag
  properties whose **generator can't reach the interesting branch** (e.g. default
  string generation where the behaviour turns on two strings colliding) and
  properties **asserted more widely than they hold**.

## 2. F# idiom violations (C#-in-F#)

Check the diff against the `fsharp-style` skill. Common regressions to hunt for:

- Optional parameters, out-params, or overloads used where required params /
  tuple returns / type annotations belong.
- Side effects, `exit`, or I/O pushed into functions that should be pure;
  errors thrown instead of returned via a DU `Result`.
- Records with invariants constructed directly instead of via a `Module.create`
  smart constructor.
- Declarations left `public` that should be `internal`; `private` used where
  assembly-scoped `internal` was meant.
- Abbreviated/Hungarian names (`mk*`, `m*`, `p*`, `kid`), single-letter record
  fields, magic numbers without a named binding.
- **`obj` / `box` / `unbox` / `:?>` used to erase types for convenience** where a
  shared base type or a flexible `#Base` parameter would keep static checking.
  Every `obj` (parameter, field, generic argument, collection key/value) and every
  downcast should trace to genuine heterogeneity, not avoidance of upcast ceremony
  — a mismatch on an erased type fails at runtime (a Fable throw in the browser),
  not at build. Flag the ones chosen for convenience.
- Legacy `.[ ]` indexing (use `expr[i]` / `expr[i..j]`); gratuitous `let mutable`
  / `while` where a fold, `takeWhile`/`choose`, or tail recursion fits.
- Unnecessary `Seq.toArray`/`List.ofSeq`; unnecessary type annotations; inline
  lambdas on per-frame hot paths; `open` statements mid-module.
- **Double traversal / repeated guards.** Two `List.filter`/`List.choose` passes
  that compute the matching and non-matching halves of one list (use
  `List.partition` or a single `fold`); a `Result`/`Choice` list unwrapped by
  `choose`-ing errors then `choose`-ing successes (sequence/traverse it once); the
  same `when …` guard or predicate copy-pasted across several matches instead of a
  shared helper/active pattern.
- **Subject not piped / inside-out application.** The `fsharp-style` "pipe the
  subject" rule is about the whole expression's data flow, and its two violations
  are easy to skim past because they don't look like an obvious `f x y` call:
  (a) nested/inside-out application — `Set.ofList (List.map fst xs)` instead of
  `xs |> List.map fst |> Set.ofList`; and (b) a subject left in application form as
  an **operand** of an operator — `actual =! List.map snd xs` instead of
  `actual =! (xs |> List.map snd)`. Check operator operands (both sides of `=!`,
  `=`, `@`, …) and innermost parens, not just top-level `let` calls. (Watch the
  precedence trap that hides the second shape: `|>` and `=!`/`=` share precedence
  and are left-associative, so the correct pipe on an operator's right needs
  parens — its absence is not an excuse to keep the application form. The same
  left-associativity means a pipeline on an operator's **left** needs no parens:
  flag `(xs |> f) =! y`, which should be `xs |> f =! y`.)
- `emitJsExpr` used where plain F# would work.
- **Dated idioms / unverified new syntax.** Flag clearly older constructs where
  the repo's modern convention applies (e.g. `expr.[i]`, `sprintf` for trivial
  interpolation where `$"…"` reads better, `(fun x -> x.P)` where `_.P` fits). The
  repo pins an explicit F# `<LangVersion>` (see `Directory.Build.props`); equally,
  be skeptical of _newer_-looking syntax that may not actually compile against the
  pin or under Fable — confirm the author verified it (`dotnet fsi --langversion:`
  the pinned version, and `npm run build` for `src/`) rather than trusting recall.
- **Hand-rolled where a standard library exists (build-vs-buy).** Flag bespoke
  parsing/serialization/format/algorithm code when a well-maintained library would
  be more robust and cheaper to maintain — especially in build scripts, where the
  full NuGet ecosystem is available via `#r "nuget:"`. Conversely, confirm a
  bespoke check is justified only after looking (no suitable library, or a
  Fable/browser constraint precludes it).

## 3. Doc comments, dead code, and drift

- Doc comments follow the `fsharp-doc-comments` skill: contract-not-consumer, no
  version numbers, no restating the signature, dependency→consumer direction.
- **Duplicated explanation is drift waiting to happen.** If the same mechanism is
  explained at more than one use site, flag it: the explanation belongs once, on
  the declaration the sites share. Grep the diff for a sentence repeated across
  modules.
- **Comment volume is a defect, not a matter of taste.** Every comment must be
  fact-checked by a human reviewer, so one that restates the code, explains
  another module's mechanism, or runs to a paragraph where a sentence would do is
  a real cost with no benefit. Flag comments that narrate the lines beneath them,
  and comments whose content belongs on a declaration elsewhere or in `AGENTS.md`.
  This applies inside function bodies, not only to `///` doc comments.
- **Dead-code removal is deep**: when code was deleted, were its
  data structures, derivation logic, and helper types removed too? Grep for
  leftover references.
- Comments/specs that assert a property ("single-pass", "matches spec rule X")
  are backed up by the code. Behaviour changes are reflected in comments; dead
  paths `failwith` rather than silently returning defaults.

## 4. Correctness and validation

- Does the change actually solve the stated problem, including edge cases?
- **Redundant state.** Flag any new trait/field that is recomputed each frame from
  other state — it is a cached second source of truth that can disagree with its
  inputs. Ask whether the consumer (including a TypeScript consumer, since Koota
  is queryable from both sides) could just derive it. Per `AGENTS.md`, avoiding
  redundant state outranks "F# is authoritative"; only keep the mirror for genuine
  domain logic, several repeating consumers, or measured cost.
- **Unconditional per-frame trait writes.** Koota's `set` notifies change
  subscribers without diffing, so a system that writes a recomputed value every
  frame re-renders subscribed components at 60 fps. Flag writes in per-frame
  systems that aren't guarded on an actual change.
- Were `npm run build`, `npm test`/`test:koota`, and `npm run coverage:check`
  run and green? (Fable can emit bad TS that `dotnet test` misses.)
- For ECS tests: are they portable across the .NET mock and real Koota, and
  constrained to the lowest common denominator Koota supports?

## Multi-model use

The review is a **multi-model** review and is **always** run — never skipped and
never gated on how "risky" the change looks. Run this rubric under **several
different models** (e.g. an Anthropic, an OpenAI, and a Google model) so the
review does not share a single model's blind spots; running the same rubric three
times on one model is not a substitute. **Wait for every panelist to report
before consolidating, addressing findings, or committing** — do not act on a
partial panel. The whole point of multi-model review is that any single model, or
even the majority, can be wrong: a lone dissenter is often the one who caught the
real bug, so a finding that lands after you think you're done still counts.
Consolidate and deduplicate the findings across the **full** panel, address every
genuine issue, then **re-solicit** a fresh multi-model pass on the updated diff.
Iterate this address-and-re-solicit loop for **at most three rounds**, stopping as
soon as a round surfaces no genuine findings. Only then is the change done.
