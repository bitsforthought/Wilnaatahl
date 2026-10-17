#r "nuget: Fake.Core.Target, 6.1.4"
#load "BuildFramework.fsx"

open System
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open BuildFramework

module FSharpRefactor =
    open System.Text.Json
    open ProcessExecution

    /// Refactor applies fixes locally; RefactorCheck reports them with --dry-run so CI never rewrites sources.
    type AnalysisMode =
        | Apply
        | ReportOnly

    // Both configuration files are hand-edited and carry comments explaining each rule decision.
    let private jsoncOptions =
        JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)

    let parsePerformanceCodes (configuration: string) =
        use document = JsonDocument.Parse(configuration, jsoncOptions)

        let codes =
            document.RootElement.EnumerateArray() |> Seq.map _.GetString() |> Seq.toList

        // performanceCommand forwards these via --codes, and an empty --codes means "no filter" to the tool,
        // which would silently enable every performance rule instead of none.
        if List.isEmpty codes || codes |> List.exists String.IsNullOrWhiteSpace then
            failwith "Performance codes must be a nonempty JSON array of nonblank strings."

        codes

    let private isRuleSetting (setting: JsonElement) =
        let isBoolean (value: JsonElement) =
            value.ValueKind = JsonValueKind.True || value.ValueKind = JsonValueKind.False

        match setting.ValueKind with
        | JsonValueKind.Object ->
            match setting.TryGetProperty "enabled" with
            | true, enabled -> isBoolean enabled
            | false, _ -> false
        | _ -> isBoolean setting

    let private hasRuleSettings (root: JsonElement) =
        root.ValueKind = JsonValueKind.Object
        && match root.TryGetProperty "rules" with
           | true, rules when rules.ValueKind = JsonValueKind.Object ->
               rules.EnumerateObject() |> Seq.forall (fun rule -> isRuleSetting rule.Value)
           | _ -> false

    /// fsharp-refactor fails open: it silently falls back to its defaults when fsharprefactor.json is malformed,
    /// which would quietly re-enable every rule we disabled. Checking the shape it reads makes that a build error.
    let validateConfiguration (configuration: string) =
        let isValid =
            try
                use document = JsonDocument.Parse(configuration, jsoncOptions)
                hasRuleSettings document.RootElement
            with :? JsonException ->
                false

        if not isValid then
            failwith
                "fsharprefactor.json must contain a \"rules\" object whose values are booleans or objects with a boolean \"enabled\"."

    // Build outputs, restored packages and Fable-generated code are not authored here; fixing them would be
    // overwritten or would rewrite third-party sources. Deliberately name-based so new projects need no registration.
    let private skippedDirectoryNames =
        set [ ".git"; "bin"; "obj"; "node_modules"; "fable_modules"; "generated" ]

    let rec private filesUnder extensions directory =
        seq {
            yield!
                Directory.EnumerateFiles directory
                |> Seq.filter (fun path -> extensions |> Set.contains (Path.GetExtension path))

            yield!
                Directory.EnumerateDirectories directory
                |> Seq.filter (fun child -> skippedDirectoryNames |> Set.contains (Path.GetFileName child) |> not)
                |> Seq.collect (filesUnder extensions)
        }

    /// Projects bring their compiled sources, so individual .fs/.fsi files are not separate inputs. Analyzing
    /// through the project also gives the tool typed compiler arguments, which many rules need to fire safely.
    let analysisInputs root =
        filesUnder (set [ ".fsproj"; ".fsx" ]) root
        |> Seq.map (fun path -> Path.GetRelativePath(root, path))
        |> Seq.sort
        |> Seq.toList

    let private modeArguments mode =
        match mode with
        | Apply -> []
        | ReportOnly -> [ "--dry-run" ]

    // Advisory-only findings have no automatic fix, so print them alongside the fixes that gate the build.
    let private createCommand root mode target categories extraArguments environmentVariables =
        let arguments =
            [ "tool"; "run"; "fsharp-refactor"; "--"; target ]
            @ modeArguments mode
            @ [ "--categories"; categories ]
            @ extraArguments
            @ [ "--notes"; "--fail-on-findings" ]

        {
            (dotnetCommand root arguments) with
                EnvironmentVariables = environmentVariables
        }

    // Excluding the performance category here keeps performance rules confined to production code even if one is
    // later enabled in fsharprefactor.json; tests and build scripts gain nothing from them.
    let commonCommand root mode target =
        createCommand root mode target "correctness,idiom,cosmetic" [] Map.empty

    /// Production code runs as Fable-generated JavaScript, so CLR-specific performance advice does not apply to it.
    /// This pass therefore analyses the browser build flavour: --define FABLE_COMPILER selects the #if FABLE_COMPILER
    /// source branches, and the environment variable (which MSBuild reads as a property) selects Fable's package
    /// references and excludes the CLR-only ECS mock. Directory.Build.props gives that flavour its own bin/obj.
    let performanceCommand root mode codes =
        let productionSourceDirectory = Path.Combine("src", "Wilnaatahl.Core")
        let selectedCodes = String.concat "," codes

        if List.isEmpty codes then
            failwith "Performance analysis requires nonempty codes."

        createCommand
            root
            mode
            productionSourceDirectory
            "performance"
            [ "--codes"; selectedCodes; "--define"; "FABLE_COMPILER" ]
            (Map [ ("FABLE_COMPILER", "true") ])

    let run root mode =
        // Both configuration files are checked before any analysis, so a bad edit cannot leave sources partly rewritten.
        Path.Combine(root, "fsharprefactor.json")
        |> File.ReadAllText
        |> validateConfiguration

        // The CLI has no named profiles or --config option; it only reads the nearest fsharprefactor.json. A
        // production-only rule selection therefore lives in its own file and is forwarded with --codes.
        let codes =
            Path.Combine(root, ".config", "fsharp-refactor-performance.json")
            |> File.ReadAllText
            |> parsePerformanceCodes

        analysisInputs root
        |> List.map (commonCommand root mode)
        |> List.iter ProcessExecution.runCommand

        performanceCommand root mode codes |> ProcessExecution.runCommand


module RefactorSelfTest =
    open SelfTestAssertions
    open FSharpRefactor
    open ProcessExecution

    let runSelfTest root =
        for invalidCodes in
            [
                ""
                "[]"
                "null"
                "{}"
                """[""]"""
                """["   "]"""
                """[null]"""
                """[42]"""
                "{ malformed"
            ] do
            assertEqual
                $"invalid performance codes fail closed: {invalidCodes}"
                true
                (failureFrom (fun () -> parsePerformanceCodes invalidCodes |> ignore)
                 |> Option.isSome)

        let describedCodes =
            parsePerformanceCodes
                """
                // Rules selected for the Fable-conditional pass.
                [
                    // First example rule.
                    "EXAMPLE1",
                    /* Second example rule. */
                    "EXAMPLE2",
                ]
                """

        assertEqual
            "performance codes accept described entries with a trailing comma"
            [ "EXAMPLE1"; "EXAMPLE2" ]
            describedCodes

        let configurationError =
            Some
                "fsharprefactor.json must contain a \"rules\" object whose values are booleans or objects with a boolean \"enabled\"."

        let ruleConfiguration ruleSetting =
            $$"""
            // Rule policy with a described rule.
            {
                "publicApi": true,
                "rules": {
                    "kept": { "enabled": false, "description": "Explains the decision." },
                    "example": {{ruleSetting}},
                }
            }
            """

        for validRule in [ "true"; "false"; """{ "enabled": true }""" ] do
            assertEqual
                $"well-formed rule setting is accepted: {validRule}"
                None
                (failureFrom (fun () -> validateConfiguration (ruleConfiguration validRule))
                 |> Option.map _.Message)

        for invalidConfiguration in
            [
                ""
                "{ malformed"
                "[]"
                "{}"
                """{ "rules": [] }"""
                ruleConfiguration "\"false\""
                ruleConfiguration "null"
                ruleConfiguration "{}"
                ruleConfiguration """{ "enabled": "true" }"""
            ] do
            assertEqual
                $"configuration the tool would silently ignore is rejected: {invalidConfiguration}"
                configurationError
                (failureFrom (fun () -> validateConfiguration invalidConfiguration)
                 |> Option.map _.Message)

        let fixtureRoot = Path.Combine(root, $"refactor-self-test-{Guid.NewGuid():N}")

        let writeFixture (relativePath: string) =
            let path = Path.Combine(fixtureRoot, relativePath)
            Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore
            File.WriteAllText(path, "")

        try
            [
                "Fixture.slnx"
                "App/App.fsproj"
                "App/Program.fs"
                "tests/Lib.Tests/Lib.Tests.fsproj"
                "tools/nested/NewUntracked.fsx"
                "tools/nested/README.md"
                "App/bin/Debug/Copied.fsproj"
                "App/obj/Generated.fsx"
                "node_modules/package/Package.fsproj"
                ".git/hooks/Hook.fsx"
                "src/generated/Generated.fsx"
                "src/generated/Generated.fsproj"
                "src/generated/fable_modules/Library/Library.fsproj"
            ]
            |> List.iter writeFixture

            assertEqual
                "filesystem discovery finds each project and authored script, skipping generated and dependency trees"
                [
                    Path.Combine("App", "App.fsproj")
                    Path.Combine("tests", "Lib.Tests", "Lib.Tests.fsproj")
                    Path.Combine("tools", "nested", "NewUntracked.fsx")
                ]
                (analysisInputs fixtureRoot)

            Directory.CreateDirectory(Path.Combine(fixtureRoot, ".config")) |> ignore

            let performancePath =
                Path.Combine(fixtureRoot, ".config", "fsharp-refactor-performance.json")

            let configurationPath = Path.Combine(fixtureRoot, "fsharprefactor.json")

            File.WriteAllText(performancePath, """[ "EXAMPLE1" ]""")
            File.WriteAllText(configurationPath, "{ malformed")

            assertEqual
                "invalid rule configuration fails before discovery or source writes"
                configurationError
                (failureFrom (fun () -> run fixtureRoot Apply) |> Option.map _.Message)

            File.WriteAllText(configurationPath, ruleConfiguration "false")
            File.WriteAllText(performancePath, "[]")

            assertEqual
                "invalid performance selection fails before discovery or source writes"
                (Some "Performance codes must be a nonempty JSON array of nonblank strings.")
                (failureFrom (fun () -> run fixtureRoot Apply) |> Option.map _.Message)
        finally
            Directory.Delete(fixtureRoot, true)

        let commonApply = commonCommand root Apply "Wilnaatahl.slnx"
        let commonCheck = commonCommand root ReportOnly "Wilnaatahl.slnx"
        let performanceApply = performanceCommand root Apply describedCodes
        let performanceCheck = performanceCommand root ReportOnly describedCodes

        for command in [ commonApply; performanceApply ] do
            assertEqual "applying fixes never requests dry-run" false (command.Arguments |> List.contains "--dry-run")

        for command in [ commonCheck; performanceCheck ] do
            assertEqual "report-only check requests dry-run" true (command.Arguments |> List.contains "--dry-run")

        for command in [ commonApply; commonCheck; performanceApply; performanceCheck ] do
            assertEqual "analysis surfaces advisory notes" true (command.Arguments |> List.contains "--notes")

            assertEqual
                "configured findings fail the target"
                true
                (command.Arguments |> List.contains "--fail-on-findings")

        assertEqual
            "performance analysis scopes to the production project"
            (Path.Combine("src", "Wilnaatahl.Core"))
            (performanceCheck.Arguments |> List.item 4)

        assertEqual
            "performance pass forwards the configured rules"
            "EXAMPLE1,EXAMPLE2"
            (performanceCheck.Arguments |> List.skipWhile ((<>) "--codes") |> List.item 1)

        assertEqual
            "performance pass supplies the Fable preprocessor define"
            "FABLE_COMPILER"
            (performanceCheck.Arguments |> List.skipWhile ((<>) "--define") |> List.item 1)

        assertEqual
            "only the performance child receives the Fable conditional"
            (Map [ ("FABLE_COMPILER", "true") ])
            performanceCheck.EnvironmentVariables

        assertEqual
            "common pass does not inherit performance-only environment"
            Map.empty
            commonCheck.EnvironmentVariables

        assertEqual
            "empty performance selection cannot broaden analysis"
            (Some "Performance analysis requires nonempty codes.")
            (failureFrom (fun () -> performanceCommand root ReportOnly [] |> ignore)
             |> Option.map _.Message)

        let fableCompilerBeforeProcess =
            Environment.GetEnvironmentVariable("FABLE_COMPILER")

        let fableEnvironmentProbe =
            processCommand root "node" [ "-e"; "if (process.env.FABLE_COMPILER !== 'true') process.exit(9)" ]

        let fableEnvironmentProbe = {
            fableEnvironmentProbe with
                EnvironmentVariables = Map [ ("FABLE_COMPILER", "true") ]
        }

        assertEqual "Fable analysis symbol is scoped to its child process" 0 (runProcess fableEnvironmentProbe)

        assertEqual
            "Fable analysis environment does not leak to the build process"
            fableCompilerBeforeProcess
            (Environment.GetEnvironmentVariable("FABLE_COMPILER"))


module Build =
    open CommandLine
    open ProcessExecution
    open BuildCommands
    open TargetExecution
    open FSharpRefactor

    let repoRoot = repositoryRoot __SOURCE_DIRECTORY__

    let isCachedExecution = false

    let executionContext =
        Context.FakeExecutionContext.Create isCachedExecution "scripts/Build.fsx" []

    Context.setExecutionContext (Context.RuntimeContext.Fake executionContext)

    let scriptArguments = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList

    let invocation =
        match parseArguments scriptArguments |> Result.bind validateOptions with
        | Error error ->
            eprintfn "%s" (formatError error)
            exit 2
        | Ok invocation -> invocation

    let ciMode =
        match invocation with
        | RunTarget(_, options) -> options.Ci
        | SelfTest
        | Help -> false

    let workerCount =
        match invocation with
        | RunTarget(_, options) ->
            options.Parallel
            |> Option.defaultValue (defaultWorkerCount Environment.ProcessorCount)
        | SelfTest
        | Help -> defaultWorkerCount Environment.ProcessorCount

    let runFormatting mode =
        formattingCommands repoRoot mode |> runCommands

    let runAgentValidation () =
        agentValidationCommands repoRoot |> runCommands

    let runFable () = fableCommands repoRoot |> runCommands

    let runEcsPreparation () =
        ecsPreparationCommand repoRoot |> runOrFail

    let runFSharpCoverage () =
        fSharpCoveragePlan repoRoot |> runCoveragePlan

    let runTypeScriptCoverage () =
        typeScriptCoveragePlan repoRoot |> runCoveragePlan

    let runCoverageCheck () =
        coverageCheckCommands repoRoot ciMode |> runCommands

    let runFSharpReport () =
        let resultsDirectory = Path.Combine(repoRoot, "TestResults")
        let reportDirectory = Path.Combine(repoRoot, "coveragereport")
        let reportPath = Path.Combine(reportDirectory, "index.html")
        let coverageGlob = fSharpCoverageGlob resultsDirectory

        reportGeneratorCommand repoRoot coverageGlob reportDirectory "Html" |> runOrFail

        openReport reportPath

    let runTypeScriptReport () =
        let reportDirectory = Path.Combine(repoRoot, "coveragereport-ts")
        let coveragePath = Path.Combine(reportDirectory, "cobertura-coverage.xml")
        let reportPath = Path.Combine(reportDirectory, "index.html")

        reportGeneratorCommand repoRoot coveragePath reportDirectory "Html" |> runOrFail

        openReport reportPath

    let runSelfTests root =
        RefactorSelfTest.runSelfTest root
        BuildSelfTest.runSelfTest root

    Target.create "BuildSelfTest" (fun _ -> runSelfTests repoRoot)

    Target.create "Init" (fun _ ->
        npmCommand repoRoot (installAction ciMode) |> runOrFail
        dotnetCommand repoRoot [ "restore" ] |> runOrFail
        dotnetCommand repoRoot [ "tool"; "restore" ] |> runOrFail)

    Target.create "Format" (fun _ -> runFormatting Write)
    Target.create "FormatCheck" (fun _ -> runFormatting Check)
    Target.create "FormatByPolicy" (fun _ -> runFormatting (formattingMode ciMode))
    Target.create "Refactor" (fun _ -> FSharpRefactor.run repoRoot Apply)
    Target.create "RefactorCheck" (fun _ -> FSharpRefactor.run repoRoot ReportOnly)
    Target.create "RefactorByPolicy" (fun _ -> FSharpRefactor.run repoRoot (if ciMode then ReportOnly else Apply))
    Target.create "Fable" (fun _ -> runFable ())
    Target.create "PrepareEcs" (fun _ -> runEcsPreparation ())
    Target.create "ValidateAgents" (fun _ -> runAgentValidation ())

    Target.create "Lint" (fun _ ->
        runNode repoRoot "node_modules/eslint/bin/eslint.js" [
            "src"
            "tests/typescript"
            "vite.config.ts"
            "vitest.config.ts"
        ])

    Target.create "TypeCheck" (fun _ -> runNode repoRoot "node_modules/typescript/bin/tsc" [])
    Target.create "Bundle" (fun _ -> runNode repoRoot "node_modules/vite/bin/vite.js" [ "build" ])
    Target.create "Build" (fun _ -> printfn "Build checks completed.")
    Target.create "Dev" (fun _ -> runNode repoRoot "node_modules/vite/bin/vite.js" [])
    Target.create "TestFSharp" (fun _ -> fSharpTestCommand repoRoot |> runOrFail)
    Target.create "TestTypeScript" (fun _ -> typeScriptTestCommand repoRoot |> runCommand)
    Target.create "TestKoota" (fun _ -> kootaTestCommand repoRoot |> runCommand)
    Target.create "Test" (fun _ -> printfn "All test suites completed.")
    Target.create "CoverageFSharp" (fun _ -> runFSharpCoverage ())
    Target.create "CoverageTypeScript" (fun _ -> runTypeScriptCoverage ())
    Target.create "Coverage" (fun _ -> printfn "Coverage reports generated.")
    Target.create "CoverageCheck" (fun _ -> runCoverageCheck ())
    Target.create "Validate" (fun _ -> printfn "Build, tests, and coverage validation completed.")
    Target.create "ReportFSharp" (fun _ -> runFSharpReport ())
    Target.create "ReportTypeScript" (fun _ -> runTypeScriptReport ())
    Target.create "Report" (fun _ -> printfn "Coverage reports generated and opened.")

    "Format" <== [ "Refactor" ]
    "FormatCheck" <== [ "RefactorCheck" ]
    "FormatByPolicy" <== [ "RefactorByPolicy" ]
    "BuildSelfTest" <== [ "FormatByPolicy" ]
    "Fable" <== [ "FormatByPolicy" ]
    "PrepareEcs" <== [ "Fable" ]
    "TypeCheck" <== [ "PrepareEcs" ]
    "Lint" <== [ "PrepareEcs" ]
    "Bundle" <== [ "TypeCheck"; "Lint"; "Fable" ]
    "ValidateAgents" <== [ "FormatByPolicy" ]
    "Build" <== [ "TypeCheck"; "Lint"; "Bundle"; "ValidateAgents" ]
    "Dev" <== [ "FormatByPolicy"; "Fable" ]
    "TestFSharp" <== [ "FormatByPolicy" ]
    "TestFSharp" <=? "PrepareEcs"
    "TestTypeScript" <== [ "FormatByPolicy"; "Fable"; "PrepareEcs" ]
    "TestKoota" <== [ "FormatByPolicy"; "Fable"; "PrepareEcs" ]
    "Test" <== [ "TestFSharp"; "TestTypeScript" ]
    "CoverageFSharp" <== [ "FormatByPolicy" ]
    "CoverageFSharp" <=? "PrepareEcs"
    "CoverageTypeScript" <== [ "FormatByPolicy"; "Fable"; "PrepareEcs" ]
    "Coverage" <== [ "CoverageFSharp"; "CoverageTypeScript" ]
    "CoverageCheck" <== [ "CoverageFSharp"; "CoverageTypeScript" ]
    "Validate" <== [ "Build"; "BuildSelfTest"; "CoverageCheck" ]
    "ReportFSharp" <== [ "CoverageFSharp" ]
    "ReportTypeScript" <== [ "CoverageTypeScript" ]
    "Report" <== [ "ReportFSharp"; "ReportTypeScript" ]

    match invocation with
    | SelfTest -> runSelfTests repoRoot
    | Help -> printfn "%s" (helpText ())
    | RunTarget(target, _) -> runTargetWith Target.run workerCount target
