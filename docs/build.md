# Build and Validation

The npm scripts are a thin facade over the FAKE target graph in
`scripts/Build.fsx`.

## Commands

| Command            | Behaviour                                                                                                                                         |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm run init`     | Install npm packages and restore .NET packages and tools.                                                                                         |
| `npm run build`    | Apply enabled F# refactorings, format locally, validate agent definitions, type-check, lint, and bundle for deployment.                           |
| `npm run dev`      | Apply enabled F# refactorings, format locally, generate and bridge Fable output, and start Vite.                                                  |
| `npm test`         | Apply enabled F# refactorings and format locally, then run both complete test suites, including Koota conformance.                                |
| `npm run validate` | Apply enabled F# refactorings and format locally, then run build checks, infrastructure self-tests, instrumented suites, and coverage gates once. |
| `npm run format`   | Apply enabled F# refactorings, then format authored files with Fantomas and Prettier.                                                             |
| `npm run report`   | Apply enabled F# refactorings and format, then generate fresh coverage and open both HTML reports locally.                                        |

Run a focused target with:

```text
npm run fake "--" --target <Target>
```

Quoting the npm argument separator keeps this form consistent across PowerShell,
`cmd.exe`, and Unix-like shells. Run `npm run fake "--" --help` to list the
available targets and options.

Useful focused targets include `Refactor`, `RefactorCheck`, `TestFSharp`,
`TestTypeScript`, `TestKoota`, `Coverage`, `CoverageCheck`, `Lint`,
`FormatCheck`, `ReportFSharp`, and `ReportTypeScript`.

`Refactor` applies the enabled mechanical fixes. `RefactorCheck` performs
report-only analysis and writes fresh SARIF reports under the ignored
`refactor-reports/` directory. Both scan the tracked solution and F# scripts;
new authored scripts under `scripts/` are also included before they are tracked.
The solution analysis covers project compile inputs such as `.fsi` files.

The common analysis pass covers correctness, idiom, and cosmetic rules across
the solution and scripts. Performance analysis is a separate pass over the
production core, restricted to the explicit browser-performance allowlist in
`scripts/Build.fsx`. It defines `FABLE_COMPILER` and scopes the corresponding
MSBuild condition to that child process so CLR-only mock sources are excluded
from performance findings without excluding them from the common pass.
After this pass, the wrapper restores the normal CLR project assets because the
conditional compilation changes the dependency graph recorded by MSBuild.
The root config freezes the complete 180-rule catalog with performance disabled
by default, and keeps FR0060, FR0092, and FR0130 disabled. The allowlist does
not opt into .NET-specific overload, `Span`, sealing, or SIMD recommendations.

Findings are advisory in the current workflow: `--notes` makes fix-less
findings visible, but the wrapper does not pass `--fail-on-findings`. A
non-zero tool exit, missing or invalid SARIF, and other infrastructure failures
still fail the target. The tool supports a future strict gate using
`--fail-on-findings`; that mode returns exit code `3` for findings, which a
future wrapper must propagate as a distinct findings result rather than mistake
for an infrastructure failure.

## Generated TypeScript

TypeScript root-file discovery excludes the vendored Fable runtime and its
generated bridge, configured in `tsconfig.json`. Fable includes runtime modules
for optional features the application does not use, and the bridge re-exports
the whole runtime, so both trees must be excluded from root discovery.
First-party generated files remain roots, and runtime modules reached through
imports are still resolved and type-checked.

## F# Test Platform

The F# test projects run on Microsoft Testing Platform: `global.json` selects
it for `dotnet test`, and each test project builds as an executable. Coverage is collected by coverlet's Microsoft Testing Platform
extension, configured by the shared `tests/testconfig.json`. Running the tests
from an IDE requires a test explorer that supports Microsoft Testing Platform.

## CI Mode

Pass `--ci` through the quoted separator:

```text
npm run init "--" --ci
npm run validate "--" --ci
```

CI mode uses `npm ci`, runs F# refactoring report-only, checks formatting
without writing, prevents coverage baseline updates, and rejects targets that
launch a development server or open a report. The report-only `RefactorCheck`
target can also be run directly in CI mode. Local build, test, dev, and focused
F# targets apply enabled fixes before Fantomas. Local development should
normally use `npm run init` and `npm run validate` without this option.

## Continuous Integration

`.github/workflows/validate.yml` runs the CI-mode commands above on Ubuntu and
Windows for every pull request, every push to `main`, and on manual dispatch. It
installs the Node.js version in `.nvmrc` and the .NET SDK selected by
`global.json`, then delegates everything else to the FAKE target graph; build,
test, lint, and coverage steps are not duplicated in the workflow. Each OS
reports a `Validate (<os>)` check on the pull request. To reproduce a failure
locally, run the two CI-mode commands from the repository root.

Third-party actions are pinned to full commit SHAs, with the release tag in a
trailing comment. Dependabot (.github/dependabot.yml) checks for new action
releases weekly and opens one grouped pull request that updates both the SHA and
the comment.

## Parallelism

FAKE uses up to five workers by default. Override the worker limit when
diagnosing scheduling behaviour:

```text
npm run validate "--" --parallel 1
```

The option sets the maximum worker count. Actual concurrency is constrained by
the target graph, so targets that share an unfinished prerequisite may still run
serially.
