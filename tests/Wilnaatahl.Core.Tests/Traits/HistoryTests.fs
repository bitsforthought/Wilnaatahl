module Wilnaatahl.Tests.Traits.HistoryTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.History
open Wilnaatahl.Tests.EcsTestSupport

/// A move whose two positions differ, so a test that read one where it meant the other would fail.
let private move id x = moveAlongX (EntityId id) x -x
let private command id x = (Command.create [ move id x ]).Value

/// A command with no moves would put nothing back, so there is nothing for it to undo.
[<Fact>]
let ``Command.create rejects an empty list of moves`` () = Command.create [] =! None

/// One move is the boundary: the smallest change a command can record.
[<Fact>]
let ``Command.create accepts a single move`` () =
    (Command.create [ move 1 5.0 ]).Value.Moves =! [ move 1 5.0 ]

[<Fact>]
let ``Command.create keeps the moves it is given`` () =
    (Command.create [ move 1 5.0; move 2 7.0 ]).Value.Moves
    =! [ move 1 5.0; move 2 7.0 ]

type CommittingTests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    /// A frame in which nothing changed the scene has no commands to return, and callers must be
    /// able to read that without knowing whether anything has been committed yet.
    [<Fact>]
    member _.``No commands are committed until one is``() = world |> committedCommands =! []

    [<Fact>]
    member _.``A committed command is there to be picked up``() =
        world |> commitCommand (command 1 5.0)

        world |> committedCommands =! [ command 1 5.0 ]

    /// Two systems can commit in the same frame, and the order they committed in is the order the
    /// commands have to be applied in.
    [<Fact>]
    member _.``Commands committed in one frame come back in the order they arrived``() =
        world |> commitCommand (command 1 5.0)
        world |> commitCommand (command 2 7.0)

        world |> committedCommands =! [ command 1 5.0; command 2 7.0 ]

    /// A frame that changed nothing must leave nothing behind for the next one, or the undo
    /// history would gain an entry every frame for a change that happened once.
    [<Fact>]
    member _.``cleanupEvents clears the commands committed this frame``() =
        world |> commitCommand (command 1 5.0)
        world |> committedCommands =! [ command 1 5.0 ]

        cleanupEvents world |> ignore

        world |> committedCommands =! []
