#r "nuget: YamlDotNet, 16.3.0"
#load "BuildFramework.fsx"

open System
open System.ComponentModel
open System.IO
open System.Runtime.InteropServices
open System.Text.Json
open System.Text.RegularExpressions
open YamlDotNet.RepresentationModel
open BuildFramework

module E2EContainer =
    open CommandLine
    open ProcessExecution

    type SnapshotMode =
        | Verify
        | Update

    module internal WorkflowContract =
        type private Node =
            | Scalar of string
            | Sequence of Node list
            | Mapping of Map<string, Node>
            | Other

        let rec private fromYaml (node: YamlNode) =
            match node with
            | :? YamlScalarNode as scalar -> Scalar(if isNull scalar.Value then "" else scalar.Value)
            | :? YamlSequenceNode as sequence -> sequence.Children |> Seq.map fromYaml |> Seq.toList |> Sequence
            | :? YamlMappingNode as mapping ->
                mapping.Children
                |> Seq.choose (fun pair ->
                    match pair.Key with
                    | :? YamlScalarNode as key -> Some(key.Value, fromYaml pair.Value)
                    | _ -> None)
                |> Map.ofSeq
                |> Mapping
            | _ -> Other

        let private parse text =
            use reader = new StringReader(text)
            let stream = YamlStream()
            stream.Load reader

            if stream.Documents.Count <> 1 then
                failwith "The E2E workflow must contain exactly one YAML document."

            fromYaml stream.Documents[0].RootNode

        let private asMapping (node: Node) : Map<string, Node> option =
            match node with
            | Mapping mapping -> Some mapping
            | _ -> None

        let private asSequence (node: Node) : Node list option =
            match node with
            | Sequence items -> Some items
            | _ -> None

        let private asScalar (node: Node) : string option =
            match node with
            | Scalar value -> Some value
            | _ -> None

        let private field (name: string) (node: Node) : Node option =
            node |> asMapping |> Option.bind (Map.tryFind name)

        let private fieldScalar (name: string) (node: Node) =
            node |> field name |> Option.bind asScalar

        let private fieldSequence (name: string) (node: Node) =
            node |> field name |> Option.bind asSequence |> Option.defaultValue []

        let private hasOnlyExpectedTriggers triggers =
            match triggers with
            | Mapping events ->
                let triggerNames = events |> Map.keys |> Set.ofSeq
                triggerNames = Set.ofList [ "push"; "schedule"; "workflow_dispatch" ]
            | _ -> false

        let private hasTrigger (name: string) (triggers: Node) =
            match triggers with
            | Mapping events -> Map.containsKey name events
            | Sequence events -> events |> List.exists (fun event -> asScalar event = Some name)
            | Scalar event -> event = name
            | Other -> false

        let rec private containsScalar (expected: string) (node: Node) =
            match node with
            | Scalar value -> value = expected
            | Sequence items -> items |> List.exists (containsScalar expected)
            | Mapping fields -> fields |> Map.exists (fun _ value -> containsScalar expected value)
            | Other -> false

        let private hasActionRepository (expectedRepository: string) (actionReference: string) =
            let separatorIndex = actionReference.LastIndexOf '@'

            separatorIndex > 0
            && separatorIndex < actionReference.Length - 1
            && actionReference.Substring(0, separatorIndex) = expectedRepository

        let private hasStepUsing expectedRepository steps =
            steps
            |> List.exists (fun step ->
                fieldScalar "uses" step
                |> Option.exists (hasActionRepository expectedRepository))

        let private stepInput inputName expectedRepository steps =
            steps
            |> List.tryPick (fun step ->
                let uses = fieldScalar "uses" step |> Option.defaultValue ""

                if hasActionRepository expectedRepository uses then
                    step |> field "with" |> Option.bind (field inputName) |> Option.bind asScalar
                else
                    None)

        let private stepArtifactName expectedRepository steps =
            stepInput "name" expectedRepository steps

        let private hasRunEqual (expected: string) steps =
            steps
            |> List.exists (fun step ->
                fieldScalar "run" step
                |> Option.exists (fun command -> command.Trim() = expected))

        let private hasRunContaining (text: string) steps =
            steps
            |> List.exists (fun step ->
                fieldScalar "run" step
                |> Option.exists (fun command -> command.Contains(text, StringComparison.Ordinal)))

        let private allActionsPinned node =
            let rec check =
                function
                | Mapping fields ->
                    fields
                    |> Map.forall (fun key value ->
                        let currentActionIsPinned =
                            key <> "uses"
                            || (value
                                |> asScalar
                                |> Option.exists (fun action -> Regex.IsMatch(action, ".+@[0-9a-f]{40}$")))

                        currentActionIsPinned && check value)
                | Sequence items -> items |> List.forall check
                | Scalar _
                | Other -> true

            check node

        let validate (imageReference: string) (text: string) =
            let workflow = text |> parse |> asMapping |> Option.defaultValue Map.empty
            let triggers: Node = workflow |> Map.tryFind "on" |> Option.defaultValue Other

            let pushTargetsOnlyMain =
                match triggers with
                | Mapping events ->
                    match events |> Map.tryFind "push" |> Option.bind asMapping with
                    | Some push when Map.count push = 1 ->
                        match Map.tryFind "branches" push with
                        | Some(Sequence [ Scalar "main" ])
                        | Some(Scalar "main") -> true
                        | _ -> false
                    | _ -> false
                | _ -> false

            let schedule = triggers |> fieldSequence "schedule"
            let jobs = workflow |> Map.tryFind "jobs" |> Option.defaultValue Other
            let buildJob = jobs |> field "build" |> Option.defaultValue Other
            let browserJob = jobs |> field "browser" |> Option.defaultValue Other

            let permissionsAreReadOnly permissions =
                permissions
                |> asMapping
                |> Option.exists (fun values ->
                    Map.count values = 1
                    && (Map.tryFind "contents" values |> Option.bind asScalar) = Some "read")

            let jobPermissionOverridesAreReadOnly =
                jobs
                |> asMapping
                |> Option.exists (fun allJobs ->
                    allJobs
                    |> Map.forall (fun _ job -> job |> field "permissions" |> Option.forall permissionsAreReadOnly))

            let allPermissionScopesAreReadOnly =
                (workflow |> Map.tryFind "permissions" |> Option.exists permissionsAreReadOnly)
                && jobPermissionOverridesAreReadOnly

            let hasNightlySchedule =
                schedule = [ Mapping(Map.ofList [ ("cron", Scalar "15 3 * * *") ]) ]

            let buildSteps = fieldSequence "steps" buildJob
            let browserSteps = fieldSequence "steps" browserJob
            let browserRuns = browserSteps |> List.choose (fieldScalar "run")
            let buildArtifactPath = stepInput "path" "actions/upload-artifact" buildSteps
            let browserArtifactPath = stepInput "path" "actions/download-artifact" browserSteps

            let reportUpload =
                browserSteps
                |> List.exists (fun step ->
                    let options = step |> field "with" |> Option.defaultValue Other

                    let includesDiagnostics =
                        fieldScalar "path" options
                        |> Option.exists (fun path -> path.Contains(".e2e-output", StringComparison.Ordinal))

                    hasStepUsing "actions/upload-artifact" [ step ]
                    && fieldScalar "if" step = Some "always()"
                    && includesDiagnostics
                    && fieldScalar "include-hidden-files" options = Some "true")

            let image =
                browserJob
                |> field "container"
                |> Option.bind (field "image")
                |> Option.bind asScalar

            let buildArtifact = stepArtifactName "actions/upload-artifact" buildSteps
            let browserArtifact = stepArtifactName "actions/download-artifact" browserSteps
            let diagnosticsArtifact = stepArtifactName "actions/upload-artifact" browserSteps

            let artifactNames =
                [ buildArtifact; browserArtifact; diagnosticsArtifact ] |> List.choose id

            let artifactsAreRunAttemptScoped =
                artifactNames.Length = 3
                && (artifactNames
                    |> List.forall (fun name -> name.Contains("${{ github.run_attempt }}", StringComparison.Ordinal)))

            let browserUsesHostBuild =
                browserRuns
                |> List.exists (fun command ->
                    command.Contains("dotnet", StringComparison.Ordinal)
                    || command.Contains("npm run fake", StringComparison.Ordinal))

            [
                if pushTargetsOnlyMain |> not then
                    yield "E2E workflow must run on pushes to main."

                if
                    hasTrigger "workflow_dispatch" triggers
                    && (hasOnlyExpectedTriggers triggers |> not)
                then
                    yield "E2E workflow must use only push, schedule, and manual-dispatch triggers."

                if hasNightlySchedule |> not then
                    yield "E2E workflow must run nightly at 03:15 UTC."

                if hasTrigger "workflow_dispatch" triggers |> not then
                    yield "E2E workflow must support manual dispatch."

                if hasTrigger "pull_request" triggers || hasTrigger "pull_request_target" triggers then
                    yield "E2E workflow must not run on pull requests."

                if allPermissionScopesAreReadOnly |> not then
                    yield "E2E workflow permissions must be limited to read-only contents access."

                if buildSteps |> hasStepUsing "actions/upload-artifact" |> not then
                    yield "E2E build job must upload the compiled application artifact."

                if
                    buildArtifactPath
                    |> Option.exists (fun path -> path.Replace('\\', '/') = "dist-e2e/")
                    |> not
                then
                    yield "E2E build job must upload dist-e2e/."

                if hasRunEqual "npm run fake \"--\" --target BuildE2E --ci" buildSteps |> not then
                    yield "E2E build job must invoke the BuildE2E target."

                if browserJob |> field "needs" |> Option.exists (containsScalar "build") |> not then
                    yield "E2E browser job must depend on the build job."

                if image <> Some imageReference then
                    yield "E2E browser job must use the pinned Playwright image."

                if browserArtifact.IsNone || browserArtifact <> buildArtifact then
                    yield "E2E browser job must download the artifact produced by the build job."

                if
                    browserArtifactPath
                    |> Option.exists (fun path -> path.Replace('\\', '/') = "dist-e2e/")
                    |> not
                then
                    yield "E2E browser job must extract the build artifact to dist-e2e/."

                if artifactsAreRunAttemptScoped |> not then
                    yield "Workflow artifact names must include the GitHub run attempt."

                if hasRunContaining "npm ci" browserSteps |> not then
                    yield "E2E browser job must install dependencies with npm ci."

                if
                    hasRunContaining "node node_modules/@playwright/test/cli.js test" browserSteps
                    |> not
                then
                    yield "E2E browser job must invoke the installed Playwright test CLI."

                if browserUsesHostBuild then
                    yield "E2E browser job must not invoke .NET or the FAKE build."

                if hasRunContaining "--update-snapshots=none" browserSteps |> not then
                    yield "E2E browser job must verify snapshots without updating them."

                if reportUpload |> not then
                    yield "E2E browser job must upload diagnostics even after failure."

                if allActionsPinned (Mapping workflow) |> not then
                    yield "E2E workflow actions must be pinned to immutable commit SHAs."
            ]

    let imageReference =
        "mcr.microsoft.com/playwright:v1.64.1-noble@sha256:47b510dd9230bf0b9bfc9b20102241e577ea7a99334ab4c3ca19f19770e9b8ea"

    let buildCommand root =
        nodeCommand root "node_modules/vite/bin/vite.js" [
            "build"
            "--mode"
            "e2e"
            "--outDir"
            "dist-e2e"
            "--emptyOutDir"
        ]

    let snapshotArguments mode =
        match mode with
        | Verify -> [ "--update-snapshots=none" ]
        | Update -> [ "--update-snapshots=all" ]

    let private dockerRunCommand root volumeName mode user entrypoint commandArguments =
        let repositoryPath = Path.GetFullPath root
        let outputPath = Path.Combine(repositoryPath, ".e2e-output")

        let repositoryMount =
            match mode with
            | Verify -> $"type=bind,source={repositoryPath},target=/work,readonly"
            | Update -> $"type=bind,source={repositoryPath},target=/work"

        let userArguments =
            match user with
            | None -> []
            | Some user -> [ "--user"; user; "--env"; "HOME=/tmp" ]

        processCommand
            root
            "docker"
            ([ "run"; "--rm"; "--init"; "--ipc=host"; "--platform=linux/amd64" ]
             @ userArguments
             @ [
                 "--workdir"
                 "/work"
                 "--mount"
                 repositoryMount
                 "--mount"
                 $"type=bind,source={outputPath},target=/work/.e2e-output"
                 "--mount"
                 $"type=volume,source={volumeName},target=/work/node_modules"
                 "--entrypoint"
                 entrypoint
                 imageReference
             ]
             @ commandArguments)

    let containerCommands root volumeName mode user =
        let copyPackageFilesCommand =
            dockerRunCommand root volumeName mode None "cp" [
                "/work/package.json"
                "/work/package-lock.json"
                "/work/node_modules"
            ]

        let installCommand =
            dockerRunCommand root volumeName mode None "npm" [ "ci"; "--prefix"; "/work/node_modules" ]

        let copyDependenciesCommand =
            dockerRunCommand root volumeName mode None "cp" [
                "-a"
                "/work/node_modules/node_modules/."
                "/work/node_modules/"
            ]

        let removeInstallFilesCommand =
            dockerRunCommand root volumeName mode None "rm" [
                "-rf"
                "/work/node_modules/node_modules"
                "/work/node_modules/package.json"
                "/work/node_modules/package-lock.json"
            ]

        let testCommand =
            dockerRunCommand
                root
                volumeName
                mode
                user
                "node"
                ([ "node_modules/@playwright/test/cli.js"; "test" ] @ snapshotArguments mode)

        [
            copyPackageFilesCommand
            installCommand
            copyDependenciesCommand
            removeInstallFilesCommand
            testCommand
        ]

    let private volumeCreateCommand root volumeName =
        processCommand root "docker" [
            "volume"
            "create"
            "--label"
            "io.wilnaatahl.operation=playwright-e2e"
            volumeName
        ]

    let private volumeRemoveCommand root volumeName =
        processCommand root "docker" [ "volume"; "rm"; volumeName ]

    module private UnixUser =
        [<DllImport("libc", EntryPoint = "getuid")>]
        extern uint32 getuid()

        [<DllImport("libc", EntryPoint = "getgid")>]
        extern uint32 getgid()

        let current =
            if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                None
            else
                Some $"{getuid ()}:{getgid ()}"

    let rec internal missingDockerExecutable (error: exn) =
        match error with
        | :? Win32Exception as processError when processError.NativeErrorCode = 2 -> Some processError.NativeErrorCode
        | _ -> error.InnerException |> Option.ofObj |> Option.bind missingDockerExecutable

    let runWithVolume runCommand createCommand removeCommand runTests =
        runCommand createCommand

        try
            runTests ()
        finally
            runCommand removeCommand

    let internal runDockerCommandWith runCommand command =
        try
            runCommand command
        with error when missingDockerExecutable error |> Option.isSome ->
            failwith
                $"Could not start Docker. Install and start a Linux-container engine as described in README.md. {error.Message}"

    let private runDockerCommand = runDockerCommandWith runCommand

    let run root mode =
        let bundlePath = Path.Combine(root, "dist-e2e")
        let outputPath = Path.Combine(root, ".e2e-output")

        if not (Directory.Exists bundlePath) then
            failwith $"E2E application output was not found at '{bundlePath}'. Run the BuildE2E target first."

        Directory.CreateDirectory(outputPath) |> ignore

        let volumeName = "wilnaatahl-playwright-" + Guid.NewGuid().ToString("N")

        runWithVolume
            runDockerCommand
            (volumeCreateCommand root volumeName)
            (volumeRemoveCommand root volumeName)
            (fun () ->
                containerCommands root volumeName mode UnixUser.current
                |> List.iter runDockerCommand)

module internal E2EContainerSelfTest =
    open CommandLine
    open ProcessExecution
    open SelfTestAssertions
    open E2EContainer

    let runSelfTest root =
        let workflowPath = Path.Combine(root, ".github", "workflows", "e2e.yml")
        let workflowText = File.ReadAllText workflowPath
        let contractFailures = WorkflowContract.validate imageReference workflowText

        assertEqual "E2E workflow satisfies its non-gating and artifact contract" [] contractFailures

        let replaceOccurrences
            (name: string)
            (expectedOccurrences: int)
            (oldValue: string)
            (newValue: string)
            (text: string)
            =
            let normalizedText = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            let occurrences = Regex.Matches(normalizedText, Regex.Escape oldValue).Count

            assertEqual
                $"workflow self-test mutation '{name}' has the expected match count"
                expectedOccurrences
                occurrences

            normalizedText.Replace(oldValue, newValue, StringComparison.Ordinal)

        let replaceExactlyOnce (name: string) (oldValue: string) (newValue: string) (text: string) =
            replaceOccurrences name 1 oldValue newValue text

        let assertMutationProducesFailure
            (name: string)
            (oldValue: string)
            (newValue: string)
            (expectedFailure: string)
            =
            let mutatedWorkflow = replaceExactlyOnce name oldValue newValue workflowText

            assertEqual
                $"workflow contract rejects {name}"
                [ expectedFailure ]
                (WorkflowContract.validate imageReference mutatedWorkflow)

        let assertMutationIncludesFailure
            (name: string)
            (oldValue: string)
            (newValue: string)
            (expectedFailure: string)
            =
            let mutatedWorkflow = replaceExactlyOnce name oldValue newValue workflowText

            assertEqual
                $"workflow contract rejects {name}"
                true
                (WorkflowContract.validate imageReference mutatedWorkflow
                 |> List.contains expectedFailure)

        let workflowWithoutRunAttempt =
            replaceOccurrences "artifact run-attempt scoping" 3 "${{ github.run_attempt }}" "0" workflowText

        assertEqual
            "workflow contract rejects artifacts shared by reruns"
            [ "Workflow artifact names must include the GitHub run attempt." ]
            (WorkflowContract.validate imageReference workflowWithoutRunAttempt)

        assertMutationProducesFailure
            "an incorrect build artifact path"
            "path: dist-e2e/\n          if-no-files-found: error"
            "path: wrong-output/\n          if-no-files-found: error"
            "E2E build job must upload dist-e2e/."

        assertMutationIncludesFailure
            "a near-prefix build upload action"
            "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4\n        with:\n          name: dist-e2e"
            "actions/upload-artifact@invalid@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4\n        with:\n          name: dist-e2e"
            "E2E build job must upload the compiled application artifact."

        assertMutationProducesFailure
            "a missing BuildE2E invocation"
            "--target BuildE2E"
            "--target Build"
            "E2E build job must invoke the BuildE2E target."

        assertMutationProducesFailure
            "a prefix-sharing but invalid build target"
            "npm run fake \"--\" --target BuildE2E --ci"
            "npm run fake \"--\" --target BuildE2EBackup --ci"
            "E2E build job must invoke the BuildE2E target."

        assertMutationProducesFailure
            "an extra build command argument"
            "npm run fake \"--\" --target BuildE2E --ci"
            "npm run fake \"--\" --target BuildE2E --ci --help"
            "E2E build job must invoke the BuildE2E target."

        assertMutationProducesFailure
            "an incorrect browser artifact path"
            "path: dist-e2e/\n      - run: npm ci"
            "path: wrong-output/\n      - run: npm ci"
            "E2E browser job must extract the build artifact to dist-e2e/."

        assertMutationIncludesFailure
            "a near-prefix browser download action"
            "actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4\n        with:\n          name: dist-e2e"
            "actions/download-artifact@invalid@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4\n        with:\n          name: dist-e2e"
            "E2E browser job must download the artifact produced by the build job."

        assertMutationProducesFailure
            "a missing Playwright test invocation"
            "node node_modules/@playwright/test/cli.js test"
            "node node_modules/vite/bin/vite.js"
            "E2E browser job must invoke the installed Playwright test CLI."

        assertMutationProducesFailure
            "an artifact name mismatch"
            "name: dist-e2e-${{ github.run_id }}-${{ github.run_attempt }}\n          path: dist-e2e/\n      - run: npm ci"
            "name: mismatched-bundle-${{ github.run_attempt }}\n          path: dist-e2e/\n      - run: npm ci"
            "E2E browser job must download the artifact produced by the build job."

        assertMutationProducesFailure
            "a missing npm ci installation"
            "npm ci"
            "npm install"
            "E2E browser job must install dependencies with npm ci."

        assertMutationProducesFailure
            "a browser job that invokes the host build"
            "node node_modules/@playwright/test/cli.js test"
            "dotnet node node_modules/@playwright/test/cli.js test"
            "E2E browser job must not invoke .NET or the FAKE build."

        assertMutationProducesFailure
            "snapshot update mode in CI"
            "--update-snapshots=none"
            "--update-snapshots=all"
            "E2E browser job must verify snapshots without updating them."

        assertMutationProducesFailure
            "diagnostics that are not uploaded after failures"
            "if: always()"
            "if: failure()"
            "E2E browser job must upload diagnostics even after failure."

        assertMutationProducesFailure
            "diagnostics that omit hidden output"
            "include-hidden-files: true"
            "include-hidden-files: false"
            "E2E browser job must upload diagnostics even after failure."

        assertMutationProducesFailure
            "a browser job without its build dependency"
            "needs: build"
            "needs: publish"
            "E2E browser job must depend on the build job."

        assertMutationProducesFailure
            "a browser image that does not match the pin"
            "playwright:v1.64.1-noble"
            "playwright:v1.64.2-noble"
            "E2E browser job must use the pinned Playwright image."

        assertMutationProducesFailure
            "a write permission"
            "contents: read"
            "contents: write"
            "E2E workflow permissions must be limited to read-only contents access."

        assertMutationProducesFailure
            "a job-level write permission override"
            "    needs: build\n    runs-on: ubuntu-latest"
            "    needs: build\n    permissions:\n      contents: write\n    runs-on: ubuntu-latest"
            "E2E workflow permissions must be limited to read-only contents access."

        assertMutationProducesFailure
            "an unpinned action"
            "actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1"
            "actions/setup-node@v7.1.0"
            "E2E workflow actions must be pinned to immutable commit SHAs."

        assertMutationProducesFailure
            "a missing manual-dispatch trigger"
            "workflow_dispatch:"
            "workflow_call:"
            "E2E workflow must support manual dispatch."

        assertMutationProducesFailure
            "a missing scheduled trigger"
            "- cron: \"15 3 * * *\""
            "- name: \"15 3 * * *\""
            "E2E workflow must run nightly at 03:15 UTC."

        assertMutationProducesFailure
            "a different scheduled cadence"
            "- cron: \"15 3 * * *\""
            "- cron: \"*/5 * * * *\""
            "E2E workflow must run nightly at 03:15 UTC."

        assertMutationProducesFailure
            "a push trigger that excludes main"
            "branches: [main]"
            "branches: [release]"
            "E2E workflow must run on pushes to main."

        assertMutationProducesFailure
            "an additional push branch"
            "branches: [main]"
            "branches: [main, release]"
            "E2E workflow must run on pushes to main."

        assertMutationProducesFailure
            "an additional push filter"
            "branches: [main]"
            "branches: [main]\n    tags: [\"v*\"]"
            "E2E workflow must run on pushes to main."

        assertMutationProducesFailure
            "an additional workflow event"
            "  workflow_dispatch:\n"
            "  workflow_call:\n  workflow_dispatch:\n"
            "E2E workflow must use only push, schedule, and manual-dispatch triggers."

        let workflowWithWritableAdditionalJob =
            replaceExactlyOnce
                "an additional job permission override"
                "jobs:\n  build:"
                "jobs:\n  audit:\n    permissions:\n      contents: write\n    steps: []\n  build:"
                workflowText

        assertEqual
            "workflow contract rejects write permission on any job"
            [ "E2E workflow permissions must be limited to read-only contents access." ]
            (WorkflowContract.validate imageReference workflowWithWritableAdditionalJob)

        let rejectedPullRequestTriggers =
            [
                "on:\n  pull_request:\n    {}\n"
                "on:\n  pull_request_target:\n    {}\n"
                "on: [pull_request]\n"
                "on: [pull_request_target]\n"
                "on: pull_request\n"
                "on: pull_request_target\n"
            ]
            |> List.map (WorkflowContract.validate imageReference)
            |> List.forall (List.contains "E2E workflow must not run on pull requests.")

        assertEqual "workflow contract rejects all pull-request trigger forms" true rejectedPullRequestTriggers

        let buildArguments = (buildCommand root).Arguments
        let outputDirectoryIndex = buildArguments |> List.tryFindIndex ((=) "--outDir")

        let outputDirectory =
            outputDirectoryIndex |> Option.map (fun index -> buildArguments[index + 1])

        assertEqual "E2E build targets its isolated output directory" (Some "dist-e2e") outputDirectory

        assertEqual "snapshot verification is read-only" [ "--update-snapshots=none" ] (snapshotArguments Verify)
        assertEqual "snapshot references update only explicitly" [ "--update-snapshots=all" ] (snapshotArguments Update)

        use packageLock =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package-lock.json")))

        let lockedPlaywrightVersion =
            packageLock.RootElement
                .GetProperty("packages")
                .GetProperty("node_modules/@playwright/test")
                .GetProperty("version")
                .GetString()

        assertEqual
            "browser image matches the locked Playwright package and immutable digest"
            true
            (imageReference.StartsWith $"mcr.microsoft.com/playwright:v{lockedPlaywrightVersion}-"
             && Regex.IsMatch(imageReference, "@sha256:[0-9a-f]{64}$"))

        assertEqual
            "missing Docker CLI is recognized through the process runner wrapper"
            (Some 2)
            (missingDockerExecutable (Exception("Docker launch failed", Win32Exception(2))))

        assertEqual
            "other Docker launch failures are not classified as missing CLI"
            None
            (missingDockerExecutable (Exception "Docker failed"))

        let missingDockerGuidance =
            failureFrom (fun () ->
                runDockerCommandWith
                    (fun _ -> raise (Exception("Docker launch failed", Win32Exception(2))))
                    (processCommand root "docker" []))
            |> Option.map _.Message

        assertEqual
            "missing Docker reports the documented setup guidance"
            true
            (missingDockerGuidance
             |> Option.exists (fun message ->
                 message.StartsWith(
                     "Could not start Docker. Install and start a Linux-container engine as described in README.md."
                 )))

        let dockerFailureMessage = "Docker daemon rejected the command"

        let unexpectedDockerFailure =
            failureFrom (fun () ->
                runDockerCommandWith (fun _ -> failwith dockerFailureMessage) (processCommand root "docker" []))
            |> Option.map _.Message

        assertEqual
            "non-missing Docker failures propagate without translation"
            (Some dockerFailureMessage)
            unexpectedDockerFailure

        let verificationCommands =
            containerCommands root "wilnaatahl-e2e-test" Verify (Some "1000:1000")

        let updateCommands =
            containerCommands root "wilnaatahl-e2e-update" Update (Some "1000:1000")

        let verificationArguments = verificationCommands |> List.last |> _.Arguments
        let updateArguments = updateCommands |> List.last |> _.Arguments
        let installArguments = verificationCommands[1].Arguments

        let repositoryMount =
            $"type=bind,source={Path.GetFullPath root},target=/work,readonly"

        let writableRepositoryMount =
            $"type=bind,source={Path.GetFullPath root},target=/work"

        assertEqual
            "verification mounts project files read-only"
            true
            (verificationCommands
             |> List.forall (fun command -> command.Arguments |> List.contains repositoryMount))

        assertEqual
            "container install uses a Linux-only node_modules volume"
            true
            (verificationCommands
             |> List.forall (fun command ->
                 command.Arguments
                 |> List.contains "type=volume,source=wilnaatahl-e2e-test,target=/work/node_modules"))

        assertEqual
            "verification invokes npm ci before Playwright without updating snapshots"
            true
            (installArguments |> List.contains "ci"
             && verificationArguments |> List.contains "--update-snapshots=none")

        assertEqual
            "npm ci installs below the mounted dependency-volume root"
            [ "ci"; "--prefix"; "/work/node_modules" ]
            (installArguments |> List.skipWhile ((<>) "ci") |> List.take 3)

        assertEqual
            "Playwright runs as the host user to keep bind-mounted output writable"
            true
            (verificationArguments
             |> List.windowed 2
             |> List.contains [ "--user"; "1000:1000" ])

        assertEqual
            "explicit update mode mounts a writable project and updates snapshots"
            true
            (updateCommands
             |> List.forall (fun command -> command.Arguments |> List.contains writableRepositoryMount)
             && updateArguments |> List.contains "--update-snapshots=all")

        assertEqual
            "Playwright child failure remains a failed process result"
            (Error(ProcessFailure("docker", 17)))
            (runCheckedWith (fun _ -> 17) (verificationCommands |> List.last))

        let createdVolume = processCommand root "docker" [ "volume"; "create"; "e2e-test" ]
        let removedVolume = processCommand root "docker" [ "volume"; "rm"; "e2e-test" ]
        let executedCommands = ResizeArray<ProcessCommand>()
        let runCommand command = executedCommands.Add command

        let childFailure =
            failureFrom (fun () ->
                runWithVolume runCommand createdVolume removedVolume (fun () -> failwith "browser test failed"))

        assertEqual
            "container failure remains visible to the caller"
            (Some "browser test failed")
            (childFailure |> Option.map _.Message)

        assertEqual
            "operation-owned volume is removed after a failed browser run"
            [ createdVolume; removedVolume ]
            (List.ofSeq executedCommands)
