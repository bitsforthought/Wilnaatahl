module Wilnaatahl.Traits.History

open Wilnaatahl.ECS

/// One node's part in a change: the node, and the position to restore when the change is
/// reversed.
///
/// An entity id held outside the ECS is not maintained by it, so a move must not outlive the
/// node it names.
type internal Move = { Entity: EntityId; Before: {| x: float; y: float; z: float |} }

/// A change to the scene, recorded so it can be reversed. Reversing it restores the recorded
/// position of every node the change names. Holds at least one move.
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

    /// Derives a command over the same nodes, giving each move the position the mapping returns.
    let mapPositions mapping command = {
        Moves_ = command.Moves_ |> List.map (fun move -> { move with Before = mapping move })
    }
