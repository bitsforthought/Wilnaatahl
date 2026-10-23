#r "nuget: Fake.Core.Target, 6.1.4"
#load "BuildFramework.fsx" "FSharpRefactor.fsx"

open System
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open BuildFramework
open FSharpRefactor

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
        | Help -> false

    let sourceWriters = sourceWriterDependencies ciMode
    let validateDependencies = BuildCommands.validateDependencies ciMode

    let workerCount =
        match invocation with
        | RunTarget(_, options) ->
            options.Parallel
            |> Option.defaultValue (defaultWorkerCount Environment.ProcessorCount)
        | Help -> defaultWorkerCount Environment.ProcessorCount

    let runFormatting mode =
        formattingCommands repoRoot mode |> runCommands

    let runAgentValidation () =
        agentValidationCommands repoRoot |> runCommands

    let runAgentValidationSelfTest () =
        agentValidationSelfTestCommand repoRoot |> runCommand

    let runFableCompileSrc () =
        fableCompileSrcCommands repoRoot |> runCommands

    let runFableBridgeSelfTest () =
        fableBridgeSelfTestCommand repoRoot |> runCommand

    let runFableCompileTests () =
        fableCompileTestsCommand repoRoot |> runOrFail

    let runFSharpCoverage () =
        fSharpCoveragePlan repoRoot |> runCoveragePlan

    let runTypeScriptCoverage () =
        typeScriptCoveragePlan repoRoot |> runCoveragePlan

    let runCoverageCheck () =
        coverageCheckCommands repoRoot ciMode |> runCommands

    let runCoverageCheckSelfTest () =
        coverageCheckSelfTestCommand repoRoot |> runCommand

    let runPostMergeMaintenanceSelfTest () =
        postMergeMaintenanceSelfTestCommand repoRoot |> runCommand

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
    Target.create "ValidateAgentsSelfTest" (fun _ -> runAgentValidationSelfTest ())
    Target.create "FableBridgeSelfTest" (fun _ -> runFableBridgeSelfTest ())
    Target.create "CheckCoverageSelfTest" (fun _ -> runCoverageCheckSelfTest ())
    Target.create "PostMergeMaintenanceSelfTest" (fun _ -> runPostMergeMaintenanceSelfTest ())
    Target.create "SelfTest" (fun _ -> printfn "Infrastructure self-tests completed.")

    Target.create "Init" (fun _ ->
        npmCommand repoRoot (installAction ciMode) |> runOrFail
        dotnetCommand repoRoot [ "restore" ] |> runOrFail
        dotnetCommand repoRoot [ "tool"; "restore" ] |> runOrFail)

    Target.create "Format" (fun _ -> runFormatting Write)
    Target.create "FormatCheck" (fun _ -> runFormatting Check)
    Target.create "Refactor" (fun _ -> FSharpRefactor.run repoRoot Apply)
    Target.create "RefactorCheck" (fun _ -> FSharpRefactor.run repoRoot ReportOnly)
    Target.create "RewriteSource" (fun _ -> printfn "Refactorings applied and files formatted.")
    Target.create "FableCompileSrc" (fun _ -> runFableCompileSrc ())
    Target.create "FableCompileTests" (fun _ -> runFableCompileTests ())
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
    Target.create "Build" (fun _ -> printfn "Build completed.")
    Target.create "Dev" (fun _ -> runNode repoRoot "node_modules/vite/bin/vite.js" [])
    Target.create "TestFSharp" (fun _ -> fSharpTestCommand repoRoot |> runOrFail)
    Target.create "TestTypeScript" (fun _ -> typeScriptTestCommand repoRoot |> runCommand)
    Target.create "TestKoota" (fun _ -> kootaTestCommand repoRoot |> runCommand)
    Target.create "Test" (fun _ -> printfn "All test suites completed.")
    Target.create "TestFSharpWithCoverage" (fun _ -> runFSharpCoverage ())
    Target.create "TestTypeScriptWithCoverage" (fun _ -> runTypeScriptCoverage ())
    Target.create "TestWithCoverage" (fun _ -> printfn "Test coverage data collected.")
    Target.create "TestWithCoverageCheck" (fun _ -> runCoverageCheck ())
    Target.create "Validate" (fun _ -> printfn "Build, tests, and coverage validation completed.")
    Target.create "ReportFSharp" (fun _ -> runFSharpReport ())
    Target.create "ReportTypeScript" (fun _ -> runTypeScriptReport ())
    Target.create "Report" (fun _ -> printfn "Coverage reports generated and opened.")

    "Format" <=? "Refactor"
    "RewriteSource" <== [ "Refactor"; "Format" ]
    "FormatCheck" <== [ "RefactorCheck" ]
    "BuildSelfTest" <== sourceWriters
    "ValidateAgentsSelfTest" <== sourceWriters
    "FableBridgeSelfTest" <== sourceWriters
    "CheckCoverageSelfTest" <== sourceWriters
    "PostMergeMaintenanceSelfTest" <== sourceWriters

    "SelfTest"
    <== [
        "BuildSelfTest"
        "ValidateAgentsSelfTest"
        "FableBridgeSelfTest"
        "CheckCoverageSelfTest"
        "PostMergeMaintenanceSelfTest"
    ]

    "FableCompileSrc" <== sourceWriters
    "FableCompileTests" <== sourceWriters
    // RefactorCheck and both Fable compilations share production projects' obj/fable outputs.
    "FableCompileSrc" <=? "RefactorCheck"
    "FableCompileTests" <=? "RefactorCheck"
    "TypeCheck" <== [ "FableCompileSrc"; "FableCompileTests" ]
    "Lint" <== [ "FableCompileSrc"; "FableCompileTests" ]
    "Bundle" <== [ "FableCompileSrc" ]
    "ValidateAgents" <== sourceWriters
    "Build" <== [ "TypeCheck"; "Lint"; "Bundle"; "ValidateAgents" ]
    "Dev" <== (sourceWriters @ [ "FableCompileSrc" ])
    "TestFSharp" <== sourceWriters
    "TestTypeScript" <== [ "FableCompileSrc"; "FableCompileTests" ]
    "TestKoota" <== [ "FableCompileSrc"; "FableCompileTests" ]
    "Test" <== [ "TestFSharp"; "TestTypeScript" ]
    "TestFSharpWithCoverage" <== sourceWriters
    // RefactorCheck's common pass and F# coverage both use Core's CLR Debug intermediates.
    "TestFSharpWithCoverage" <=? "RefactorCheck"
    "TestTypeScriptWithCoverage" <== [ "FableCompileSrc"; "FableCompileTests" ]

    "TestWithCoverage"
    <== [ "TestFSharpWithCoverage"; "TestTypeScriptWithCoverage" ]

    "TestWithCoverageCheck" <== [ "TestWithCoverage" ]
    "Validate" <== validateDependencies
    "ReportFSharp" <== [ "TestFSharpWithCoverage" ]
    "ReportTypeScript" <== [ "TestTypeScriptWithCoverage" ]
    "Report" <== [ "ReportFSharp"; "ReportTypeScript" ]

    match invocation with
    | Help -> printfn "%s" (helpText ())
    | RunTarget(target, _) -> runTargetWith Target.run workerCount target
