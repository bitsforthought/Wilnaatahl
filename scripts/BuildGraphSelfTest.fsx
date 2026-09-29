#r "nuget: Fake.Core.Target, 6.1.4"
#r "nuget: Fake.Core.Process, 6.1.4"

open System
open System.Collections.Concurrent
open System.IO
open Fake.Core
open Fake.Core.TargetOperators

let repositoryRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let scriptArguments = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList
let failureEnteredFileName = "failure-entered.txt"
let leftDependentFileName = "left-dependent-ran.txt"
let rightDependentFileName = "right-dependent-ran.txt"

let executionContext =
    Context.FakeExecutionContext.Create false "scripts/BuildGraphSelfTest.fsx" []

Context.setExecutionContext (Context.RuntimeContext.Fake executionContext)

let fail message =
    raise (InvalidOperationException message)

let runSchedulerProbe () =
    let events = ConcurrentQueue<string>()

    let temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"wilnaatahl-fake-order-{Guid.NewGuid()}")

    let sharedCompletedPath = Path.Combine(temporaryDirectory, "shared-completed.txt")
    let leftCompletedPath = Path.Combine(temporaryDirectory, "left-completed.txt")
    let rightCompletedPath = Path.Combine(temporaryDirectory, "right-completed.txt")

    Directory.CreateDirectory(temporaryDirectory) |> ignore

    try
        Target.create "SharedProbe" (fun _ ->
            events.Enqueue("shared-started")
            events.Enqueue("shared-finished")
            File.WriteAllText(sharedCompletedPath, "completed"))

        let runBranch name completedPath =
            if not (File.Exists sharedCompletedPath) then
                fail $"{name} started before its shared prerequisite completed."

            events.Enqueue($"{name}-started")
            events.Enqueue($"{name}-finished")
            File.WriteAllText(completedPath, "completed")

        Target.create "LeftProbe" (fun _ -> runBranch "left" leftCompletedPath)
        Target.create "RightProbe" (fun _ -> runBranch "right" rightCompletedPath)

        Target.create "EndProbe" (fun _ ->
            if not (File.Exists leftCompletedPath && File.Exists rightCompletedPath) then
                fail "The terminal target started before its dependent targets completed."

            events.Enqueue("end"))

        "SharedProbe" ==> "LeftProbe" |> ignore
        "SharedProbe" ==> "RightProbe" |> ignore
        "LeftProbe" ==> "EndProbe" |> ignore
        "RightProbe" ==> "EndProbe" |> ignore

        Target.run 2 "EndProbe" []

        let completedEvents = events.ToArray() |> Array.toList

        let expectedEvents =
            [ "shared-started"
              "shared-finished"
              "left-started"
              "left-finished"
              "right-started"
              "right-finished"
              "end" ]

        if List.sort completedEvents <> List.sort expectedEvents then
            fail $"FAKE did not run the shared prerequisite and each dependent exactly once: {completedEvents}."

    finally
        Directory.Delete(temporaryDirectory, true)

let runFailureProbeFromChild temporaryDirectory =
    let enteredPath = Path.Combine(temporaryDirectory, failureEnteredFileName)
    let leftDependentPath = Path.Combine(temporaryDirectory, leftDependentFileName)
    let rightDependentPath = Path.Combine(temporaryDirectory, rightDependentFileName)

    Target.create "FailureProbe" (fun _ ->
        File.WriteAllText(enteredPath, "entered")
        fail "Expected probe failure.")

    Target.create "LeftDependentProbe" (fun _ -> File.WriteAllText(leftDependentPath, "dependent ran"))
    Target.create "RightDependentProbe" (fun _ -> File.WriteAllText(rightDependentPath, "dependent ran"))
    Target.create "FailureEndProbe" (fun _ -> ())

    "FailureProbe" ==> "LeftDependentProbe" |> ignore
    "FailureProbe" ==> "RightDependentProbe" |> ignore
    "LeftDependentProbe" ==> "FailureEndProbe" |> ignore
    "RightDependentProbe" ==> "FailureEndProbe" |> ignore
    Target.run 2 "FailureEndProbe" []

match scriptArguments with
| [ "--failure-probe"; markerPath ] -> runFailureProbeFromChild markerPath
| [] ->
    runSchedulerProbe ()

    let temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"wilnaatahl-fake-graph-{Guid.NewGuid()}")

    Directory.CreateDirectory(temporaryDirectory) |> ignore
    let enteredPath = Path.Combine(temporaryDirectory, failureEnteredFileName)
    let leftDependentPath = Path.Combine(temporaryDirectory, leftDependentFileName)
    let rightDependentPath = Path.Combine(temporaryDirectory, rightDependentFileName)

    try
        let childArguments =
            [ "fsi"
              "--warnaserror"
              "--warnon:3886"
              Path.Combine(repositoryRoot, "scripts", "BuildGraphSelfTest.fsx")
              "--"
              "--failure-probe"
              temporaryDirectory ]

        let childResult =
            CreateProcess.fromRawCommand "dotnet" childArguments
            |> CreateProcess.withWorkingDirectory repositoryRoot
            |> Proc.run

        if childResult.ExitCode = 0 then
            fail "FAKE reported success after a target failure."

        if not (File.Exists enteredPath) then
            fail "The failure-propagation child exited before entering its FAKE target."

        if File.Exists leftDependentPath || File.Exists rightDependentPath then
            fail "FAKE ran a dependent target after the shared prerequisite failed."
    finally
        Directory.Delete(temporaryDirectory, true)

    printfn "FAKE graph self-test: passed."
| _ -> fail $"Unexpected BuildGraphSelfTest arguments: {scriptArguments}."
