#load "BuildFramework.fsx"

open System
open System.ComponentModel
open System.IO
open System.Runtime.InteropServices
open System.Text.Json
open System.Text.RegularExpressions
open BuildFramework

module E2EContainer =
    open CommandLine
    open ProcessExecution

    type SnapshotMode =
        | Verify
        | Update

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
