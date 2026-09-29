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

| Step             | Command                  | Purpose                                                                                                                                                               |
| ---------------- | ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Build (full)     | `npm run build`          | Invokes Fable, TypeScript, ESLint, then Vite. Catches Fable-emitted invalid TS and lint violations that `dotnet test` cannot.                                         |
| Tests            | `npm test`               | Runs .NET xUnit tests, then the full Vitest suite, including Koota conformance.                                                                                       |
| TypeScript       | `npm run test:ts`        | Runs the full Vitest suite from `tests/typescript`, including Koota conformance.                                                                                      |
| Koota only       | `npm run test:koota`     | Runs only the portable Koota conformance suite; use for faster targeted iteration.                                                                                    |
| Coverage data    | `npm run coverage`       | Generates F# and TypeScript coverage data. Use `coverage:fsharp` or `coverage:ts` for one language.                                                                   |
| Coverage gate    | `npm run coverage:check` | Runs F# and TypeScript coverage separately, then applies both ratchets through the same `CheckCoverage.fsx` script. The baseline auto-updates when coverage improves. |
| Lint (enforced)  | `npm run lint`           | Fails on ESLint's type-aware findings in hand-written TypeScript; also runs during `npm run build`.                                                                   |
| Coverage reports | `npm run report`         | Generates fresh data and opens both language-specific HTML reports. Use `report:fsharp` or `report:ts` for one language.                                              |
| Format           | `npm run format`         | Prettier for TS, Fantomas for F#.                                                                                                                                     |

## Rules

- **Validate end-to-end before declaring done.** `dotnet test` alone is **not**
  sufficient — it only exercises the .NET-targeted F# build. Always run **`npm run
build`** and **`npm test`** before considering a change complete. Fable can emit
  invalid TypeScript for code that compiles cleanly under `dotnet test` (e.g.
  nested generics with concrete-plus-generic tuple element types — see
  `compareCouplesByEffectiveDate` and Fable issue fable-compiler/Fable#3586), so
  skipping the npm side lets those failures escape the change.
- **Check coverage after every change.** Run `npm run coverage:check` after making
  code changes and before committing. The TypeScript denominator is hand-written
  `src/**/*.ts`, excluding `src/generated/**`, `src/**/*.tsx`, and
  `src/vite-env.d.ts`; `.tsx` is excluded because it must contain no logic.
- **Enforce TypeScript lint after every change.** Run `npm run lint` directly
  for targeted feedback; `npm run build` also runs it after Fable generation and
  TypeScript checking. Rule violations, parser errors, and configuration failures
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
