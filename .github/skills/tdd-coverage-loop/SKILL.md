---
name: tdd-coverage-loop
description: >-
  The build/test/coverage validation gate for Wilnaatahl — the exact command
  sequence to run before declaring any code change done, plus why each step is
  required. Use whenever validating a change or deciding whether work is
  complete.
---

# Validation gate (build, test, coverage, lint)

Run these in order before considering any code change complete. Each catches a
class of failure the others miss.

## Commands

| Step          | Command                                                                        | Purpose                                                                                          |
| ------------- | ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| Full gate     | `npm run validate`                                                             | Build checks, infrastructure self-tests, both full suites once with coverage, and both ratchets. |
| Build only    | `npm run build`                                                                | Fable output, TypeScript, ESLint, and Vite.                                                      |
| Tests only    | `npm test`                                                                     | .NET xUnit and the full Vitest suite, including Koota conformance.                               |
| Focused tests | `npm run fake -- --target TestFSharp`, `TestTypeScript`, or `TestKoota`        | Run only the selected suite.                                                                     |
| Coverage only | `npm run fake -- --target CoverageFSharp`, `CoverageTypeScript`, or `Coverage` | Generate fresh coverage data without running the ratchet.                                        |
| Coverage gate | `npm run fake -- --target CoverageCheck`                                       | Apply F# and TypeScript ratchets to existing summaries.                                          |
| Lint          | `npm run fake -- --target Lint`                                                | Run ESLint on application, tests, and configuration.                                             |
| Reports       | `npm run report`                                                               | Generate fresh data and open both HTML reports locally.                                          |
| Format        | `npm run format`                                                               | Fantomas for F# and Prettier for authored files.                                                 |

On Windows PowerShell, use `npm.cmd` instead of `npm` when forwarding FAKE
options such as `--target` or `--ci`; the PowerShell shim may consume them.

## Rules

- **Validate end-to-end before declaring done.** `dotnet test` alone is **not**
  sufficient — it only exercises the .NET-targeted F# build. Run **`npm run
validate`** before considering a change complete; it includes the deployment
  build checks, full tests with coverage, and coverage ratchets without running
  the suites a second time. Fable can emit
  invalid TypeScript for code that compiles cleanly under `dotnet test` (e.g.
  nested generics with concrete-plus-generic tuple element types — see
  `compareCouplesByEffectiveDate` and Fable issue fable-compiler/Fable#3586), so
  skipping the npm side lets those failures escape the change.
- **Check coverage after every change.** `npm run validate` runs the coverage
  gate after fresh instrumented suites. For a focused check, generate reports
  with `Coverage` and then run `CoverageCheck`. The TypeScript denominator is hand-written
  `src/**/*.ts`, excluding `src/generated/**`, `src/**/*.tsx`, and
  `src/vite-env.d.ts`; `.tsx` is excluded because it must contain no logic.
- **Enforce TypeScript lint after every change.** `npm run validate` and
  `npm run build` run lint after Fable generation and TypeScript checking. Use
  `npm run fake -- --target Lint` for targeted feedback. Rule violations, parser errors, and configuration failures
  all fail the gate. Fix findings rather than suppressing the exit status.
- **Run the smallest targeted selection that covers the change**, then escalate to
  full-suite runs only when targeted validation shows they're needed.
- **Allow Prettier to update `.md` files.** That's part of its job; keep its
  markdown reformatting rather than reverting it as "unrelated".
- **Warnings fail the build.** `dotnet build`/`dotnet test` run with
  `TreatWarningsAsErrors` and every `dotnet fsi` script with `--warnaserror` (both
  with FS3886 — the `[ a, b ]` single-tuple-list typo — elevated via `WarnOn` /
  `--warnon:3886`), so warnings can't accumulate silently. Fix the warning rather
  than suppressing it. `TreatWarningsAsErrors` is an MSBuild property, so it also
  covers restore/NuGet warnings and Fable's `dotnet msbuild` project crack — only
  Fable's F#→TS emission is exempt (that output is checked by `tsc`). The
  NuGetAudit warnings (NU1900–NU1905) are kept non-fatal via `WarningsNotAsErrors`
  so an external CVE advisory — or an audit that couldn't run (feed unreachable, or
  a source with no vulnerability database) — can't redden a green build with no code
  change.
