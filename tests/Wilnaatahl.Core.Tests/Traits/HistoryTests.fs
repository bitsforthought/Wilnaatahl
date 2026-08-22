module Wilnaatahl.Tests.Traits.HistoryTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.Entities
open Wilnaatahl.Traits.History

let private move id x = { Entity = EntityId id; Before = Line3.pos x 0.0 0.0 }

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
