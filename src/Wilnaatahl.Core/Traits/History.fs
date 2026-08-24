module Wilnaatahl.Traits.History

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait

/// How one node was moved by a change: the node, the position it moved from, and the position it
/// moved to. Both positions are recorded when the change happens, because that is the only time
/// they are both known to belong to this change. Reading one back from the world later could pick
/// up a move made by something else in the meantime.
///
/// An entity id held outside the ECS is not maintained by it, so a move must not outlive the
/// node it names.
type internal Move = {
    Entity: EntityId
    Before: {| x: float; y: float; z: float |}
    After: {| x: float; y: float; z: float |}
}

/// A change to the scene, recorded so it can be applied again in either direction. Undoing it
/// moves every node it lists to that move's `Before`; redoing it moves them to their `After`.
/// Always holds at least one move.
type internal Command = private {
    Moves_: Move list
} with

    member this.Moves = this.Moves_

module internal Command =

    /// Creates a command from the given moves, or `None` when there are none.
    let create moves =
        match moves with
        | [] -> None
        | _ -> Some { Moves_ = moves }

// The commands committed so far this frame. A system that changes the scene commits a command
// describing that change, and does not need to know which system consumes it.
//
// Stored newest-first, because prepending is how an F# list grows. `committedCommands` reverses
// it so that callers get them oldest-first. The trait is private so that no caller can see the
// stored order or replace the list with a different one.
//
// A list rather than a queue, because no caller ever takes a single command — the frame's
// commands are always read together — so a queue's fast single dequeue would go unused. It also
// has to be immutable: if it were mutable, the first caller to read it could empty it before the
// next caller looked. Fable provides no immutable queue, only mutable Stack and Queue, so a list
// is the closest fit available.
let private CommittedCommands = refTrait (fun () -> List.empty<Command>)

/// The commands committed so far this frame, oldest first. That is the order the changes happened
/// in, and so the order they have to be applied in. Reading them does not remove them.
let internal committedCommands (world: IWorld) =
    world.Get CommittedCommands |> Option.defaultValue [] |> List.rev

/// Commits a command as part of this frame's changes.
let internal commitCommand command (world: IWorld) =
    let committed = world.Get CommittedCommands |> Option.defaultValue []
    // The value is always supplied, never left to the trait's factory.
    world.AddWith CommittedCommands (command :: committed)

/// Discards the commands committed this frame. A command belongs to the frame its change
/// happened in.
let internal clearCommittedCommands (world: IWorld) = world.Remove CommittedCommands
