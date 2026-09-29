#r "nuget: Fake.Core.Target, 6.1.4"
#r "nuget: Fake.Core.Process, 6.1.4"

open System
open System.ComponentModel
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text.Json
open Fake.Core
open Fake.Core.TargetOperators

type ProcessCommand =
    { Executable: string
      Arguments: string list
      WorkingDirectory: string
      RequiredFile: string option }

type Options =
    { Target: string option
      Ci: bool
      Parallel: int option
      SelfTest: bool
      Help: bool }

type BuildError =
    | MissingOptionValue of optionName: string
    | DuplicateOption of optionName: string
    | InvalidParallelValue of value: string option
    | ConflictingOptions of optionName: string
    | UnknownOption of optionName: string
    | UnknownTarget of targetName: string
    | LocalTargetInCi of targetName: string
    | MissingTool of path: string
    | ProcessFailure of executable: string * exitCode: int
    | ReportOpenFailure of path: string * message: string

type NpmAction =
    | Install
    | CiInstall
    | Version

type FormatMode =
    | Write
    | Check

type CoveragePlan =
    { CleanPaths: string list
      Commands: ProcessCommand list }

type Invocation =
    | SelfTest
    | Help
    | RunTarget of targetName: string * options: Options

let defaultOptions =
    { Target = None
      Ci = false
      Parallel = None
      SelfTest = false
      Help = false }

let availableTargets =
    [ "Init"
      "Format"
      "FormatCheck"
      "Fable"
      "PrepareEcs"
      "ValidateAgents"
      "Lint"
      "TypeCheck"
      "Bundle"
      "Build"
      "Dev"
      "BuildSelfTest"
      "TestFSharp"
      "TestTypeScript"
      "TestKoota"
      "Test"
      "CoverageFSharp"
      "CoverageTypeScript"
      "Coverage"
      "CoverageCheck"
      "Validate"
      "ReportFSharp"
      "ReportTypeScript"
      "Report" ]

let localOnlyTargets =
    [ "Format"; "Dev"; "ReportFSharp"; "ReportTypeScript"; "Report" ]

let defaultWorkerCount processorCount = min processorCount 5
let targetList = String.concat ", " availableTargets

let helpText () =
    String.concat
        Environment.NewLine
        [ "Usage: dotnet fsi scripts/Build.fsx -- [options]"
          "Options:"
          "  --target <target>       Select target (default: Build)"
          "  --ci                    Use check-only formatting and non-writing coverage"
          "  --parallel <workers>    Set FAKE worker limit (must be positive)"
          "  --self-test             Run build command self-tests"
          "  --help                  Show this help"
          $"Targets: {targetList}" ]

let parseArguments arguments =
    let rec parse remaining options =
        match remaining with
        | [] ->
            if
                options.SelfTest
                && (options.Target.IsSome || options.Ci || options.Parallel.IsSome || options.Help)
            then
                Error(ConflictingOptions "--self-test")
            elif options.Help && (options.Target.IsSome || options.Ci || options.Parallel.IsSome) then
                Error(ConflictingOptions "--help")
            else
                Ok options
        | "--target" :: target :: tail when options.Target.IsNone && target.Trim() <> "" ->
            parse tail { options with Target = Some target }
        | "--target" :: _ when options.Target.IsSome -> Error(DuplicateOption "--target")
        | "--target" :: _ -> Error(MissingOptionValue "--target")
        | "--ci" :: tail when not options.Ci -> parse tail { options with Ci = true }
        | "--ci" :: _ -> Error(DuplicateOption "--ci")
        | "--parallel" :: value :: tail when options.Parallel.IsNone ->
            match Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, workers when workers > 0 -> parse tail { options with Parallel = Some workers }
            | _ -> Error(InvalidParallelValue(Some value))
        | "--parallel" :: _ when options.Parallel.IsSome -> Error(DuplicateOption "--parallel")
        | "--parallel" :: _ -> Error(InvalidParallelValue None)
        | "--self-test" :: tail when not options.SelfTest -> parse tail { options with SelfTest = true }
        | "--self-test" :: _ -> Error(DuplicateOption "--self-test")
        | "--help" :: tail when not options.Help -> parse tail { options with Help = true }
        | "--help" :: _ -> Error(DuplicateOption "--help")
        | option :: _ -> Error(UnknownOption option)

    parse arguments defaultOptions

let validateOptions options =
    let target = options.Target |> Option.defaultValue "Build"

    if not (availableTargets |> List.contains target) then
        Error(UnknownTarget target)
    elif options.Ci && (localOnlyTargets |> List.contains target) then
        Error(LocalTargetInCi target)
    elif options.SelfTest then
        Ok SelfTest
    elif options.Help then
        Ok Help
    else
        Ok(RunTarget(target, options))

let formatError error =
    match error with
    | MissingOptionValue optionName -> $"Option {optionName} requires a value."
    | DuplicateOption optionName -> $"Option {optionName} may be used only once."
    | InvalidParallelValue _ -> "Option --parallel requires a positive integer."
    | ConflictingOptions optionName -> $"{optionName} cannot be combined with other options."
    | UnknownOption optionName -> $"Unknown option: {optionName}"
    | UnknownTarget targetName -> $"Unknown target '{targetName}'. Run with --help to list available targets."
    | LocalTargetInCi "Format" -> "Target Format writes files and cannot be combined with --ci; use FormatCheck."
    | LocalTargetInCi targetName -> $"Target {targetName} is local-only and cannot be combined with --ci."
    | MissingTool path -> $"Required local tool was not found: {path}. Run npm run init."
    | ProcessFailure(executable, exitCode) -> $"{executable} exited with code {exitCode}."
    | ReportOpenFailure(path, message) ->
        let detail = message.TrimEnd()
        let sentenceEnding = if detail.EndsWith('.') then "" else "."
        $"Could not open report at '{path}': {detail}{sentenceEnding} The report remains available at this path."

let repositoryRoot scriptDirectory =
    Path.GetFullPath(Path.Combine(scriptDirectory, ".."))

let processCommand workingDirectory executable arguments =
    { Executable = executable
      Arguments = arguments
      WorkingDirectory = workingDirectory
      RequiredFile = None }

let dotnetCommand root arguments = processCommand root "dotnet" arguments

let npmCommandFor isWindows root action =
    let arguments =
        match action with
        | Install -> [ "install" ]
        | CiInstall -> [ "ci" ]
        | Version -> [ "--version" ]

    if isWindows then
        let commandLine = "npm.cmd " + String.concat " " arguments
        processCommand root "cmd.exe" [ "/d"; "/s"; "/c"; commandLine ]
    else
        processCommand root "npm" arguments

let npmCommand root action =
    npmCommandFor (OperatingSystem.IsWindows()) root action

let nodeCommand root scriptPath arguments =
    let fullScriptPath = Path.Combine(root, scriptPath)

    { processCommand root "node" (fullScriptPath :: arguments) with
        RequiredFile = Some fullScriptPath }

let validateCommand command =
    match command.RequiredFile with
    | Some path when not (File.Exists path) -> Error(MissingTool path)
    | _ -> Ok command

let runProcess command =
    CreateProcess.fromRawCommand command.Executable command.Arguments
    |> CreateProcess.withWorkingDirectory command.WorkingDirectory
    |> Proc.run
    |> _.ExitCode

let runCheckedWith execute command =
    let exitCode = execute command

    if exitCode = 0 then
        Ok()
    else
        Error(ProcessFailure(command.Executable, exitCode))

let runOrFail command =
    match runCheckedWith runProcess command with
    | Ok() -> ()
    | Error error -> failwith (formatError error)

let runCommand command =
    command
    |> validateCommand
    |> function
        | Ok validatedCommand -> runOrFail validatedCommand
        | Error error -> failwith (formatError error)

let runNode root scriptPath arguments =
    nodeCommand root scriptPath arguments |> runCommand

let runCommands commands = commands |> List.iter runCommand

let formattingMode ci = if ci then Check else Write

let formattingCommands root mode =
    let checkOnly = mode = Check

    let fantomasArguments =
        if checkOnly then
            [ "fantomas"; "--check"; "." ]
        else
            [ "fantomas"; "." ]

    let prettierArguments = if checkOnly then [ "--check"; "." ] else [ "--write"; "." ]

    [ dotnetCommand root fantomasArguments
      nodeCommand root "node_modules/prettier/bin/prettier.cjs" prettierArguments ]

let agentValidationCommands root =
    let validator = "scripts/ValidateAgentDefinitions.fsx"
    let fsiArguments = [ "fsi"; "--warnaserror"; "--warnon:3886"; validator ]

    [ dotnetCommand root (fsiArguments @ [ "--self-test" ])
      dotnetCommand root fsiArguments ]

let fableCommands root =
    [ dotnetCommand
          root
          [ "fable"
            "--lang"
            "typescript"
            "--outDir"
            "../generated"
            "--cwd"
            "./src/Wilnaatahl.Core" ]
      dotnetCommand root [ "fsi"; "--warnaserror"; "--warnon:3886"; "scripts/PatchFableModules.fsx" ] ]

let ecsPreparationCommand root =
    dotnetCommand
        root
        [ "fable"
          "tests/Wilnaatahl.ECS.Tests"
          "--lang"
          "typescript"
          "--outDir"
          "tests/Wilnaatahl.ECS.Tests/out"
          "--noCache" ]

let fSharpTestCommand root = dotnetCommand root [ "test" ]

let typeScriptTestCommand root =
    nodeCommand root "node_modules/vitest/vitest.mjs" [ "run" ]

let kootaTestCommand root =
    nodeCommand root "node_modules/vitest/vitest.mjs" [ "run"; "kootaConformance" ]

let reportGeneratorCommand root reportPath targetDirectory reportTypes =
    dotnetCommand
        root
        [ "reportgenerator"
          $"-reports:{reportPath}"
          $"-targetdir:{targetDirectory}"
          $"-reporttypes:{reportTypes}" ]

let fSharpCoveragePlan root =
    let resultsDirectory = Path.Combine(root, "TestResults")
    let reportDirectory = Path.Combine(root, "coveragereport")
    let coverageGlob = Path.Combine(resultsDirectory, "**", "coverage.cobertura.xml")

    { CleanPaths = [ resultsDirectory; reportDirectory ]
      Commands =
        [ dotnetCommand
              root
              [ "test"
                "--collect"
                "XPlat Code Coverage"
                "--results-directory"
                "TestResults" ]
          reportGeneratorCommand root coverageGlob reportDirectory "JsonSummary" ] }

let typeScriptCoveragePlan root =
    let reportDirectory = Path.Combine(root, "coveragereport-ts")
    let coberturaPath = Path.Combine(reportDirectory, "cobertura-coverage.xml")

    { CleanPaths = [ reportDirectory ]
      Commands =
        [ nodeCommand root "node_modules/vitest/vitest.mjs" [ "run"; "--coverage" ]
          reportGeneratorCommand root coberturaPath reportDirectory "JsonSummary" ] }

let coverageCheckCommands root ci =
    let scriptArguments =
        [ "fsi"; "--warnaserror"; "--warnon:3886"; "scripts/CheckCoverage.fsx" ]

    let policyArguments = if ci then [ "--check-only" ] else []

    [ dotnetCommand root (scriptArguments @ [ "--self-test" ])
      dotnetCommand root (scriptArguments @ policyArguments) ]

let cleanOwnedDirectories paths =
    paths
    |> List.iter (fun path ->
        if Directory.Exists path then
            Directory.Delete(path, true))

let runCoveragePlan plan =
    cleanOwnedDirectories plan.CleanPaths
    runCommands plan.Commands

let openReport path =
    let startInfo = ProcessStartInfo(path)
    startInfo.UseShellExecute <- true

    try
        Process.Start(startInfo) |> ignore
    with :? Win32Exception as error ->
        failwith (formatError (ReportOpenFailure(path, error.Message)))

let runTargetWith run workers targetName =
    Environment.SetEnvironmentVariable("parallel-jobs", string workers)
    run workers targetName []

let assertEqual name expected actual =
    if expected <> actual then
        failwith $"Build self-test failed: {name}. Expected {expected}, got {actual}."

let runSelfTest root =
    assertEqual "default options" (Ok defaultOptions) (parseArguments [])
    assertEqual "default target" (Ok(RunTarget("Build", defaultOptions))) (validateOptions defaultOptions)

    assertEqual
        "help documents all accepted options and targets"
        (String.concat
            Environment.NewLine
            [ "Usage: dotnet fsi scripts/Build.fsx -- [options]"
              "Options:"
              "  --target <target>       Select target (default: Build)"
              "  --ci                    Use check-only formatting and non-writing coverage"
              "  --parallel <workers>    Set FAKE worker limit (must be positive)"
              "  --self-test             Run build command self-tests"
              "  --help                  Show this help"
              $"Targets: {targetList}" ])
        (helpText ())

    use packageManifest =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package.json")))

    let actualNpmScripts =
        packageManifest.RootElement.GetProperty("scripts").EnumerateObject()
        |> Seq.map (fun property -> property.Name, property.Value.GetString())
        |> Seq.toList

    let expectedNpmScripts =
        [ "init", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Init"
          "fake", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx --"
          "build", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Build"
          "dev", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Dev"
          "test", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Test"
          "validate", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Validate"
          "format", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Format"
          "report", "dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Report" ]

    assertEqual "npm exposes only the eight FAKE command wrappers" expectedNpmScripts actualNpmScripts

    assertEqual
        "target, CI, and parallel options"
        (Ok
            { defaultOptions with
                Target = Some "Fable"
                Ci = true
                Parallel = Some 1 })
        (parseArguments [ "--target"; "Fable"; "--ci"; "--parallel"; "1" ])

    assertEqual "missing target value" (Error(MissingOptionValue "--target")) (parseArguments [ "--target" ])

    assertEqual "empty target value" (Error(MissingOptionValue "--target")) (parseArguments [ "--target"; "" ])

    assertEqual
        "missing target value message"
        "Option --target requires a value."
        (formatError (MissingOptionValue "--target"))

    assertEqual "invalid parallel value" (Error(InvalidParallelValue(Some "0"))) (parseArguments [ "--parallel"; "0" ])

    assertEqual "missing parallel value" (Error(InvalidParallelValue None)) (parseArguments [ "--parallel" ])

    assertEqual
        "duplicate parallel option"
        (Error(DuplicateOption "--parallel"))
        (parseArguments [ "--parallel"; "1"; "--parallel"; "2" ])

    assertEqual
        "duplicate target"
        (Error(DuplicateOption "--target"))
        (parseArguments [ "--target"; "Build"; "--target"; "Dev" ])

    assertEqual "duplicate CI option" (Error(DuplicateOption "--ci")) (parseArguments [ "--ci"; "--ci" ])
    assertEqual "duplicate option message" "Option --ci may be used only once." (formatError (DuplicateOption "--ci"))

    assertEqual "unknown option" (Error(UnknownOption "--unknown")) (parseArguments [ "--unknown" ])
    assertEqual "unknown option message" "Unknown option: --unknown" (formatError (UnknownOption "--unknown"))

    assertEqual
        "invalid parallel value message"
        "Option --parallel requires a positive integer."
        (formatError (InvalidParallelValue(Some "0")))

    assertEqual
        "self-test cannot take options"
        (Error(ConflictingOptions "--self-test"))
        (parseArguments [ "--self-test"; "--ci" ])

    assertEqual "help cannot take options" (Error(ConflictingOptions "--help")) (parseArguments [ "--help"; "--ci" ])

    assertEqual
        "conflicting option message"
        "--help cannot be combined with other options."
        (formatError (ConflictingOptions "--help"))

    assertEqual
        "unknown target"
        (Error(UnknownTarget "Missing"))
        (validateOptions
            { defaultOptions with
                Target = Some "Missing" })

    assertEqual
        "unknown target message"
        "Unknown target 'Missing'. Run with --help to list available targets."
        (formatError (UnknownTarget "Missing"))

    assertEqual
        "CI rejects write-mode format"
        (Error(LocalTargetInCi "Format"))
        (validateOptions
            { defaultOptions with
                Target = Some "Format"
                Ci = true })

    assertEqual
        "format CI error message"
        "Target Format writes files and cannot be combined with --ci; use FormatCheck."
        (formatError (LocalTargetInCi "Format"))

    assertEqual
        "CI rejects dev server"
        (Error(LocalTargetInCi "Dev"))
        (validateOptions
            { defaultOptions with
                Target = Some "Dev"
                Ci = true })

    assertEqual
        "generic local-only target message"
        "Target Dev is local-only and cannot be combined with --ci."
        (formatError (LocalTargetInCi "Dev"))

    assertEqual
        "CI rejects report browser launch"
        (Error(LocalTargetInCi "Report"))
        (validateOptions
            { defaultOptions with
                Target = Some "Report"
                Ci = true })

    assertEqual "CI selects read-only formatting" Check (formattingMode true)
    assertEqual "local formatting writes" Write (formattingMode false)

    assertEqual
        "CI format commands check without writing"
        [ dotnetCommand root [ "fantomas"; "--check"; "." ]
          nodeCommand root "node_modules/prettier/bin/prettier.cjs" [ "--check"; "." ] ]
        (formattingCommands root Check)

    assertEqual
        "local format commands write"
        [ dotnetCommand root [ "fantomas"; "." ]
          nodeCommand root "node_modules/prettier/bin/prettier.cjs" [ "--write"; "." ] ]
        (formattingCommands root Write)

    assertEqual
        "Fable compilation is followed by patching"
        [ dotnetCommand
              root
              [ "fable"
                "--lang"
                "typescript"
                "--outDir"
                "../generated"
                "--cwd"
                "./src/Wilnaatahl.Core" ]
          dotnetCommand root [ "fsi"; "--warnaserror"; "--warnon:3886"; "scripts/PatchFableModules.fsx" ] ]
        (fableCommands root)

    assertEqual
        "ECS output keeps no-cache option"
        (dotnetCommand
            root
            [ "fable"
              "tests/Wilnaatahl.ECS.Tests"
              "--lang"
              "typescript"
              "--outDir"
              "tests/Wilnaatahl.ECS.Tests/out"
              "--noCache" ])
        (ecsPreparationCommand root)

    assertEqual "F# tests run the complete .NET test solution" (dotnetCommand root [ "test" ]) (fSharpTestCommand root)

    assertEqual
        "TypeScript tests include full Vitest suite"
        (nodeCommand root "node_modules/vitest/vitest.mjs" [ "run" ])
        (typeScriptTestCommand root)

    assertEqual
        "Koota target selects conformance tests"
        (nodeCommand root "node_modules/vitest/vitest.mjs" [ "run"; "kootaConformance" ])
        (kootaTestCommand root)

    let fSharpResultsPath = Path.Combine(root, "TestResults")
    let fSharpReportPath = Path.Combine(root, "coveragereport")
    let typeScriptReportPath = Path.Combine(root, "coveragereport-ts")

    let fSharpCoverageGlob =
        Path.Combine(fSharpResultsPath, "**", "coverage.cobertura.xml")

    let typeScriptCoveragePath =
        Path.Combine(typeScriptReportPath, "cobertura-coverage.xml")

    assertEqual
        "F# coverage cleans only its owned output and generates a summary"
        { CleanPaths = [ fSharpResultsPath; fSharpReportPath ]
          Commands =
            [ dotnetCommand
                  root
                  [ "test"
                    "--collect"
                    "XPlat Code Coverage"
                    "--results-directory"
                    "TestResults" ]
              dotnetCommand
                  root
                  [ "reportgenerator"
                    $"-reports:{fSharpCoverageGlob}"
                    $"-targetdir:{fSharpReportPath}"
                    "-reporttypes:JsonSummary" ] ] }
        (fSharpCoveragePlan root)

    assertEqual
        "TypeScript coverage cleans only its owned output and runs suite once"
        { CleanPaths = [ typeScriptReportPath ]
          Commands =
            [ nodeCommand root "node_modules/vitest/vitest.mjs" [ "run"; "--coverage" ]
              dotnetCommand
                  root
                  [ "reportgenerator"
                    $"-reports:{typeScriptCoveragePath}"
                    $"-targetdir:{typeScriptReportPath}"
                    "-reporttypes:JsonSummary" ] ] }
        (typeScriptCoveragePlan root)

    let cleanupDirectory =
        Path.Combine(Path.GetTempPath(), $"fake-coverage-cleanup-{Guid.NewGuid()}")

    Directory.CreateDirectory(cleanupDirectory) |> ignore
    let staleOutputPath = Path.Combine(cleanupDirectory, "stale-summary.json")
    File.WriteAllText(staleOutputPath, "{}")

    try
        cleanOwnedDirectories [ cleanupDirectory ]
        assertEqual "coverage cleanup removes stale output" false (Directory.Exists cleanupDirectory)
        cleanOwnedDirectories [ cleanupDirectory ]
        assertEqual "coverage cleanup accepts a missing output directory" false (Directory.Exists cleanupDirectory)
    finally
        if Directory.Exists cleanupDirectory then
            Directory.Delete(cleanupDirectory, true)

    assertEqual
        "local coverage ratchets and CI coverage checks without writing"
        [ dotnetCommand
              root
              [ "fsi"
                "--warnaserror"
                "--warnon:3886"
                "scripts/CheckCoverage.fsx"
                "--self-test" ]
          dotnetCommand root [ "fsi"; "--warnaserror"; "--warnon:3886"; "scripts/CheckCoverage.fsx" ] ]
        (coverageCheckCommands root false)

    assertEqual
        "CI coverage check uses non-writing mode"
        [ dotnetCommand
              root
              [ "fsi"
                "--warnaserror"
                "--warnon:3886"
                "scripts/CheckCoverage.fsx"
                "--self-test" ]
          dotnetCommand
              root
              [ "fsi"
                "--warnaserror"
                "--warnon:3886"
                "scripts/CheckCoverage.fsx"
                "--check-only" ] ]
        (coverageCheckCommands root true)

    assertEqual
        "F# HTML report generator"
        (dotnetCommand
            root
            [ "reportgenerator"
              $"-reports:{fSharpCoverageGlob}"
              $"-targetdir:{fSharpReportPath}"
              "-reporttypes:Html" ])
        (reportGeneratorCommand root fSharpCoverageGlob fSharpReportPath "Html")

    let fSharpReportIndex = Path.Combine(fSharpReportPath, "index.html")

    assertEqual
        "report opening failure identifies the generated report"
        $"Could not open report at '{fSharpReportIndex}': No application is associated with the specified file. The report remains available at this path."
        (formatError (ReportOpenFailure(fSharpReportIndex, "No application is associated with the specified file.")))

    assertEqual
        "report opening failure adds missing sentence punctuation"
        $"Could not open report at '{fSharpReportIndex}': Browser launch failed. The report remains available at this path."
        (formatError (ReportOpenFailure(fSharpReportIndex, "Browser launch failed")))

    assertEqual
        "report opening failure preserves an ellipsis"
        $"Could not open report at '{fSharpReportIndex}': Browser launch failed... The report remains available at this path."
        (formatError (ReportOpenFailure(fSharpReportIndex, "Browser launch failed...")))

    assertEqual
        "agent validation self-test precedes validation"
        [ dotnetCommand
              root
              [ "fsi"
                "--warnaserror"
                "--warnon:3886"
                "scripts/ValidateAgentDefinitions.fsx"
                "--self-test" ]
          dotnetCommand
              root
              [ "fsi"
                "--warnaserror"
                "--warnon:3886"
                "scripts/ValidateAgentDefinitions.fsx" ] ]
        (agentValidationCommands root)

    let spacedRoot = Path.Combine(Path.GetTempPath(), "fake build test with spaces")
    let viteScript = "node_modules/vite/bin/vite.js"
    let arguments = [ "--mode"; "ci config"; "--host"; "127.0.0.1" ]
    let expectedCommand = nodeCommand spacedRoot viteScript arguments

    assertEqual
        "node arguments retain order and spaces"
        ("node", Path.Combine(spacedRoot, viteScript) :: arguments)
        (expectedCommand.Executable, expectedCommand.Arguments)

    assertEqual "process working directory" spacedRoot expectedCommand.WorkingDirectory

    assertEqual
        "Windows npm CI install command"
        (processCommand root "cmd.exe" [ "/d"; "/s"; "/c"; "npm.cmd ci" ])
        (npmCommandFor true root CiInstall)

    assertEqual
        "portable npm CI install command"
        (processCommand root "npm" [ "ci" ])
        (npmCommandFor false root CiInstall)

    assertEqual
        "Windows npm local install command"
        (processCommand root "cmd.exe" [ "/d"; "/s"; "/c"; "npm.cmd install" ])
        (npmCommandFor true root Install)

    assertEqual
        "portable npm local install command"
        (processCommand root "npm" [ "install" ])
        (npmCommandFor false root Install)

    let missingTool =
        nodeCommand root "node_modules/not-installed/bin/tool.js" [] |> validateCommand

    let missingScript = Path.Combine(root, "node_modules/not-installed/bin/tool.js")

    assertEqual "missing tool is reported" (Error(MissingTool missingScript)) missingTool

    assertEqual
        "missing tool error message"
        $"Required local tool was not found: {missingScript}. Run npm run init."
        (formatError (MissingTool missingScript))

    assertEqual "successful process result" (Ok()) (runCheckedWith (fun _ -> 0) expectedCommand)

    assertEqual "failed process result" (Error(ProcessFailure("node", 7))) (runCheckedWith (fun _ -> 7) expectedCommand)

    assertEqual "process failure message" "node exited with code 7." (formatError (ProcessFailure("node", 7)))

    assertEqual
        "FAKE receives the requested worker count"
        ("4", (4, "Build", []))
        (runTargetWith
            (fun workers targetName arguments ->
                Environment.GetEnvironmentVariable("parallel-jobs"), (workers, targetName, arguments))
            4
            "Build")

    assertEqual
        "repository root resolves from script directory"
        (Path.GetFullPath(Path.Combine(root, "scripts")))
        (repositoryRoot (Path.Combine(root, "scripts", "nested")))

    assertEqual "default parallelism preserves small machines" 2 (defaultWorkerCount 2)
    assertEqual "default parallelism includes the cap boundary" 5 (defaultWorkerCount 5)
    assertEqual "default parallelism is capped at five" 5 (defaultWorkerCount 12)

    let spacedWorkingDirectory =
        Path.Combine(Path.GetTempPath(), $"fake-build-{Guid.NewGuid()}-with spaces")

    Directory.CreateDirectory(spacedWorkingDirectory) |> ignore

    try
        dotnetCommand spacedWorkingDirectory [ "--version" ] |> runOrFail
    finally
        Directory.Delete(spacedWorkingDirectory)

    npmCommand root Version |> runOrFail
    dotnetCommand root [ "--version" ] |> runOrFail
    printfn "Build self-test: passed."

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
    | _ -> false

let workerCount =
    match invocation with
    | RunTarget(_, options) ->
        options.Parallel
        |> Option.defaultValue (defaultWorkerCount Environment.ProcessorCount)
    | _ -> defaultWorkerCount Environment.ProcessorCount

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
    let coverageGlob = Path.Combine(resultsDirectory, "**", "coverage.cobertura.xml")

    reportGeneratorCommand repoRoot coverageGlob reportDirectory "Html" |> runOrFail

    openReport reportPath

let runTypeScriptReport () =
    let reportDirectory = Path.Combine(repoRoot, "coveragereport-ts")
    let coveragePath = Path.Combine(reportDirectory, "cobertura-coverage.xml")
    let reportPath = Path.Combine(reportDirectory, "index.html")

    reportGeneratorCommand repoRoot coveragePath reportDirectory "Html" |> runOrFail

    openReport reportPath

Target.create "BuildSelfTest" (fun _ -> runSelfTest repoRoot)

Target.create "Init" (fun _ ->
    npmCommand repoRoot (if ciMode then CiInstall else Install) |> runOrFail
    dotnetCommand repoRoot [ "restore" ] |> runOrFail
    dotnetCommand repoRoot [ "tool"; "restore" ] |> runOrFail)

Target.create "Format" (fun _ -> runFormatting Write)
Target.create "FormatCheck" (fun _ -> runFormatting Check)
Target.create "FormatByPolicy" (fun _ -> runFormatting (formattingMode ciMode))
Target.create "Fable" (fun _ -> runFable ())
Target.create "PrepareEcs" (fun _ -> runEcsPreparation ())
Target.create "ValidateAgents" (fun _ -> runAgentValidation ())

Target.create "Lint" (fun _ ->
    runNode
        repoRoot
        "node_modules/eslint/bin/eslint.js"
        [ "src"; "tests/typescript"; "vite.config.ts"; "vitest.config.ts" ])

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
| SelfTest -> runSelfTest repoRoot
| Help -> printfn "%s" (helpText ())
| RunTarget(target, _) -> runTargetWith Target.run workerCount target
