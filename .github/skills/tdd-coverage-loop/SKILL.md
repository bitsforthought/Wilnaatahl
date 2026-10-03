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

Use `Coverage` followed by `CoverageCheck` when iterating specifically on
coverage.

Focused commands do not replace `npm run validate`: .NET tests alone cannot catch
invalid TypeScript emitted by Fable, and TypeScript tests alone do not exercise
the .NET implementation.

Warnings fail the build. Fix them rather than suppressing their exit status.
