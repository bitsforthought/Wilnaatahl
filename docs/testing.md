# Testing standards

Authors and reviewers use these standards together with the relevant
`fsharp-testing` or `typescript-testing` skill. Use the `tdd` skill for behavioural
test design and vertical red/green slices. The local contracts below take
precedence over its public-only seam and blanket owned-double restrictions.

## Test-first evidence

Plan tests before the corresponding implementation, then execute one behavioural
increment at a time. RED must compile, run, and fail for the intended behavioural
reason. Add the smallest type-correct returning stub needed to reach that failure;
a missing symbol or compiler error does not establish test sensitivity.
Implement only enough to pass, then repeat. A theory or property may express one
increment over many cases; independent guarantees may require multiple assertions.

Use the spec or plan's approved Testing Decisions for seams. Reuse approval for
additional cases at the same seam. If approval is absent or a new seam is needed,
the coordinating agent confirms it with the user before authoring tests; a worker
returns the question to the coordinator. `to-spec` already provides that step.

## Observable contracts

Exercise behaviour through intentional interfaces rather than internal call
counts or copies of implementation formulas. Expected values come from an
independent oracle: a worked example, specification, or known-good literal.
When testing that an API passes a value through unchanged, compare the received
value directly with the supplied fixture input.
Every assertion must detect a plausible broken implementation, not merely
restate an assigned input or an unchanged value nothing could have changed.

For each changed contract enumerate equivalence classes, boundaries
(empty/single/many, min/max, first/last), and relevant error paths. A passing line
coverage threshold does not demonstrate logical coverage. Retain tests of
enduring behaviour, including current regression and compatibility guarantees;
remove tests whose only purpose is history or behaviour no longer required.
Git records migration chronology. Explain removed or weakened assertions rather
than silently dropping guarantees to make tests pass.

### Intentional internal contracts

All public F# members require direct tests, including edge cases. Fundamental
internal primitives such as those of `FamilyGraph` also require direct tests,
not only transitive coverage through product callers.

F# `internal` describes assembly accessibility, not whether a function has an
intentional contract. Such a function can be an approved test seam without
becoming public. Preserve encapsulation: this exception does not make every
private helper a test target or justify exposing helpers just for tests.

### The checked ECS fake

The .NET ECS fake is a deliberate exception to a prohibition on test doubles
for owned code: its contract is checked against real Koota. See
[ADR 0004](adr/0004-portable-ecs-equivalence-tests.md) for the architecture.
This does not justify tests that merely assert interactions with internal
collaborators. The F# testing skill selects the app versus conformance suite
and its runtime restrictions.

## Shared test helpers

Search sibling tests and shared modules before adding a helper. Keep a helper
local until another test needs it, then hoist it to their nearest shared scope
rather than copying it or repeating its setup inline.

## Test data

Never coin a Gitxsan Name, Wilp, or Pdeeḵ for filler. Names are hereditary
property. Use easily understood English nicknames (`Tinker`, `Sparks`, `The Mayor`,
`Lefty`, `Doc`) and opaque Wilp tokens (`"H"`, `"L"`, `"MM"`). Avoid
Gitxsan-sounding coinages and animal/clan imagery. A real Gitxsan spelling that
is itself the subject of a parser test is not filler.

## Review evidence

Review the same contracts used during authoring. Check that the RED evidence
demonstrates the intended failure, generators reach the interesting cases, and
assertions could disagree with an incorrect result. Consult the language skill
for assertion, fixture, and runtime rules rather than duplicating them in the
review rubric. Acceptance tests and direct inspection establish requested
behaviour; `validate-change` establishes the repository-wide validation evidence.
