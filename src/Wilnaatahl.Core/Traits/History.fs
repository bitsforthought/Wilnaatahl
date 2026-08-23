module Wilnaatahl.Traits.History

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait

/// One node's part in a change: the node, the position it moved from, and the position it moved
/// to. Both are recorded when the change happens, which is the only moment they are both known to
/// be its own; anything derived later could have been moved by someone else since.
///
/// An entity id held outside the ECS is not maintained by it, so a move must not outlive the
/// node it names.
type internal Move = {
    Entity: EntityId
    Before: {| x: float; y: float; z: float |}
    After: {| x: float; y: float; z: float |}
}

/// A change to the scene, recorded so it can be replayed in either direction: reversing it
/// restores the `Before` of every node it names, and reapplying it restores their `After`. Holds
/// at least one move.
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

// The commands committed so far this frame. A feature that changes the scene commits the command
// that takes its change back, without knowing or caring who acts on it.
//
// Held newest-first, because that is how a list grows; `committedCommands` hands out the
// chronological order that consumers actually need. The trait is private so that split cannot
// leak: nothing outside this module can see the stored order, or replace the list with a shorter
// one.
//
// A list rather than a queue because no consumer ever takes a single command — the frame's
// commands are read all at once — so a queue's cheap single dequeue would never be used. It also
// has to be immutable: a mutable collection handed to the first reader could be drained before
// the second one sees it. Fable ships no immutable queue (its library implements only mutable
// Stack and Queue), so a list is as close as the platform gets.
let private CommittedCommands = refTrait (fun () -> List.empty<Command>)

/// The commands committed so far this frame, oldest first — the order the changes happened in,
/// and so the order they have to be replayed in. Reading them leaves them in place.
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
