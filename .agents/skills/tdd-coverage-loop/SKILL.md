---
name: tdd-coverage-loop
description: >-
  The build/test/coverage validation gate for Wilnaatahl — the exact command
  sequence to run before declaring any code change done, plus why each step is
  required. Use whenever validating a change or deciding whether work is
  complete.
---

# Validation gate (build, test, coverage, lint)

Run the full validation target before considering any code change complete:

```text
npm run validate
```

This target runs formatting policy, infrastructure self-tests, Fable generation,
TypeScript checking, ESLint, the production bundle, both instrumented test suites,
and both coverage ratchets. It runs each test suite once.

Use `npm run validate "--" --ci` for the non-writing CI policy.

## Scope and reuse

- Check the acceptance criteria with focused tests and direct inspection before
  the full gate. A green build alone does not establish the requested behaviour.
- Follow `AGENTS.md`'s validation scope and evidence-reuse policy. Record the
  candidate, command, mode, result, and log path for each check.
- Run both local and CI policy only when the change affects their differences or
  acceptance criteria require both. Capture performance baselines before edits,
  but defer repeated comparison measurements until correctness fixes are settled;
  repeat measurements only to meet stated criteria or resolve uncertainty.

## Focused Iteration

Run the smallest target that covers the active change, then finish with the full
gate:

```text
npm run fake "--" --target TestFSharp
npm run fake "--" --target TestTypeScript
npm run fake "--" --target TestKoota
npm run fake "--" --target Lint
npm run fake "--" --target FormatCheck
```

Use `TestWithCoverageCheck` to run both instrumented suites and check their coverage
baselines. Use `TestWithCoverage` only when you need the coverage data without
checking the baselines.

Focused commands do not replace `npm run validate`: .NET tests alone cannot catch
invalid TypeScript emitted by Fable, and TypeScript tests alone do not exercise
the .NET implementation.

Warnings fail the build. Fix them rather than suppressing their exit status.
