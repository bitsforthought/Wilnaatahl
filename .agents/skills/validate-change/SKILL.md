---
name: validate-change
description: >-
  Validate a Wilnaatahl change against its acceptance criteria and the
  build/test/coverage gate. Use for focused checks, final validation, or
  deciding whether existing validation evidence can be reused.
---

# Validation gate (build, test, coverage, lint)

Input: acceptance criteria and the candidate being checked. Output: the candidate
identity, command, mode, result, and log path for each check. For uncommitted
changes, identify the content as well as the base commit; `HEAD` alone is not
the candidate.

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
- Run the full gate on the stable completion candidate. Repeat after executable
  code, tests, dependencies, build configuration, or tool-consumed instructions
  change, or a failure/unresolved concern invalidates earlier evidence.
- Documentation-only changes need no build/test gate unless documentation checks
  exist. Prose/comment-only review fixes retain prior passing evidence and need
  only applicable formatting, link, or documentation checks. Tool-consumed
  directives and configuration are not prose.
- Reuse evidence for unchanged candidate content across handoffs; record its
  identity before and after generation/formatting. A commit can preserve checked
  content, but changed or newly combined content needs applicable checks. This
  skill does not certify review approval.
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

See [Build and Validation](../../../docs/build.md) for target details and
argument forwarding. This skill establishes validation evidence, not review
approval or permission to publish.
