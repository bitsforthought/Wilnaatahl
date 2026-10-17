---
name: fsharp-refactor-findings
description: >-
  Resolve fsharp-refactor findings and strict-gate failures while preserving
  behaviour and configured scope. Use for automatic fixes, blocking manual
  advisories, held out-of-scope suggestions, or review feedback on refactor rules.
---

# Address refactoring findings

## Establish the finding's scope

Use the existing command facade described in [Build and Validation](../../../docs/build.md).
Identify the rule code, affected source, and whether the output is an automatic
fix, a blocking advisory without a fix, or a suggestion held outside scope.
The effective rules come from `fsharprefactor.json`, its per-rule options,
analysis categories, and the production performance allowlist. A rule disabled
in the main file can still run when selected by the performance pass's `--codes`.
Widening these controls or forcing disabled rules requires explicit approval of
that policy change, not merely permission to address a finding.

Local analysis applies automatic fixes but still fails for findings encountered;
inspect the edits and rerun until clean. CI analysis is report-only. A strict
failure can stop analysis before later inputs, so fixing the first reported
finding does not establish that the remaining inputs are clean.

## Evaluate the proposed change

- A finding is not proof of a defect or an improvement. Inspect the producer,
  consumers, actual collection types, and relevant runtime before changing code.
  For production, reason about Fable-generated browser code, not CLR assumptions.
- Consider evaluation order, laziness, repeated computation, allocation, public
  contracts, and error handling. Do not substitute eager materialization or a
  different traversal merely to quiet a diagnostic. Do not claim a measured
  performance gain without measurements.
- Review automatic edits too. Follow [F# style](../fsharp-style/SKILL.md),
  including semantic naming; a mechanically generated rename is not necessarily
  a good name. Preserve existing exception messages and independent assertions.
- Use the executor and testing skills routed in [AGENTS.md](../../../AGENTS.md).
  Test new behaviour before implementation. A mechanical-preservation test may
  already pass against the old implementation; describe that evidence honestly.

## Require permission for policy and out-of-scope changes

**Address advisory-only findings held outside configured scope only with explicit
user permission.** Explain the specific suggestion and any public/API implications
before asking. Without permission, leave it unchanged and report the non-blocking
output; repeated console output is not authorization to change it.

Permission to fix a particular held finding does not authorize changing
`publicApi`, `apiChanges`, passing `--api-changes`, or otherwise widening scope.
Make the specifically approved change manually under the existing policy.

For an unsafe, disputed, or unsuitable in-scope advisory, explain the evidence
and ask the user rather than disabling the gate, suppressing the finding,
introducing a findings baseline, or weakening tests. Rule disables also require
explicit approval. Apply an approved decision in every owning configuration,
including the performance allowlist when applicable, and reverse its associated
changes while preserving any changes the user explicitly chose to keep. Record
the concrete rationale and trade-offs in the PR.

## Validate the result

Run focused checks, then follow the [validation gate](../tdd-coverage-loop/SKILL.md)
and [adversarial review](../adversarial-code-review/SKILL.md). Confirm the intended
finding disappears without changing unrelated policy or behaviour. A changed
coverage percentage after a refactoring or reversion is not permission to lower
a ratchet; explain the measurement and obtain approval for a baseline reduction.
