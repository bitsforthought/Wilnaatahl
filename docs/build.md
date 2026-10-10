# Build and Validation

The npm scripts are a thin facade over the FAKE target graph in
`scripts/Build.fsx`.

## Commands

| Command            | Behaviour                                                                                  |
| ------------------ | ------------------------------------------------------------------------------------------ |
| `npm run init`     | Install npm packages and restore .NET packages and tools.                                  |
| `npm run build`    | Format locally, validate agent definitions, type-check, lint, and bundle for deployment.   |
| `npm run dev`      | Format locally, generate, patch, and bridge Fable output, and start Vite.                  |
| `npm test`         | Run both complete test suites, including Koota conformance.                                |
| `npm run validate` | Run build checks, infrastructure self-tests, instrumented suites, and coverage gates once. |
| `npm run format`   | Format authored files with Fantomas and Prettier.                                          |
| `npm run report`   | Generate fresh coverage and open both HTML reports locally.                                |

Run a focused target with:

```text
npm run fake "--" --target <Target>
```

Quoting the npm argument separator keeps this form consistent across PowerShell,
`cmd.exe`, and Unix-like shells. Run `npm run fake "--" --help` to list the
available targets and options.

Useful focused targets include `TestFSharp`, `TestTypeScript`, `TestKoota`,
`Coverage`, `CoverageCheck`, `Lint`, `FormatCheck`, `ReportFSharp`, and
`ReportTypeScript`.

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

CI mode uses `npm ci`, checks formatting without writing, prevents coverage
baseline updates, and rejects targets that launch a development server or open a
report. Local development should normally use `npm run init` and
`npm run validate` without this option.

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
