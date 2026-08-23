module Wilnaatahl.Tests.Traits.HistoryTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.Entities
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.History
open Wilnaatahl.Tests.EcsTestSupport

let private move id x = { Entity = EntityId id; Before = Line3.pos x 0.0 0.0 }

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

/// Mapping positions preserves the non-empty invariant that `create` establishes, and cannot
/// change which nodes the command names.
[<Fact>]
let ``Command.mapPositions replaces each position and keeps the nodes`` () =
    let command = (Command.create [ move 1 5.0; move 2 7.0 ]).Value

    let doubled =
        command |> Command.mapPositions (fun m -> Line3.pos (m.Before.x * 2.0) 0.0 0.0)

    doubled.Moves =! [ move 1 10.0; move 2 14.0 ]

type CommittingTests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    /// A frame in which nothing changed the scene has nothing to offer, and reading that must not
    /// require the caller to know whether anyone has committed anything yet.
    [<Fact>]
    member _.``No commands are committed until one is``() = world |> committedCommands =! []

    [<Fact>]
    member _.``A committed command is there to be picked up``() =
        world |> commitCommand (command 1 5.0)

        world |> committedCommands =! [ command 1 5.0 ]

    /// Two features can commit in the same frame, and the order they did so is the order they
    /// have to be replayed in.
    [<Fact>]
    member _.``Commands committed in one frame come back in the order they arrived``() =
        world |> commitCommand (command 1 5.0)
        world |> commitCommand (command 2 7.0)

        world |> committedCommands =! [ command 1 5.0; command 2 7.0 ]

    /// A frame that changed nothing must leave nothing behind for the next one, or the undo
    /// history would grow an entry per frame for a change that happened once.
    [<Fact>]
    member _.``cleanupEvents clears the commands committed this frame``() =
        world |> commitCommand (command 1 5.0)
        world |> committedCommands =! [ command 1 5.0 ]

        cleanupEvents world |> ignore

        world |> committedCommands =! []
