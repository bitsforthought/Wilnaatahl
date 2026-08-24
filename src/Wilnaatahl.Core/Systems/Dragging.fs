module Wilnaatahl.Systems.Dragging

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits

let private startDrag (world: IWorld) =
    // Events refuses a start while a drag is running, so this can only be a drag beginning.
    // A drag moves the nodes that were selected when it started, because the view layer only
    // makes selected nodes draggable. Storing each node's starting position now is what
    // decides the set of nodes the drag moves: from here on it reads DragOrigin, not Selected.
    let participants = world.QueryTrait(Position, With Selected)

    // With nothing selected there is nothing to move, so DragInFlight is not added. Adding it
    // would refuse every click until the user released the pointer, for a drag that cannot
    // move anything.
    if participants |> Seq.isEmpty |> not then
        world.Add DragInFlight

        participants.ForEach
        <| fun (origin, entity) -> entity |> addWith DragOrigin origin

    world

let private moveParticipants (distance: {| x: float; y: float; z: float |}) (world: IWorld) =
    // A Dragged event carries the total distance travelled since the drag started, so each node's
    // new position is its starting position plus that distance. Computing it that way, instead of
    // adding the change since the last frame, keeps the position correct even if something else
    // moved the node, and stops rounding errors accumulating over a long drag.
    world.QueryTraits(Position, DragOrigin).UpdateEachWith AlwaysTrack
    <| fun ((position, origin), _) ->
        position.x <- origin.x + distance.x
        position.y <- origin.y + distance.y
        position.z <- origin.z + distance.z

    world

let private endDrag (world: IWorld) =
    // DragInFlight is required because a release can arrive when no drag is running, and then
    // there is nothing to end.
    if world.Has DragInFlight then
        // Undo has to move a node back to the position it will come to rest at, which is its
        // TargetPosition while it is animating and its Position otherwise. A drag only writes
        // Position, so it does not change where an animating node comes to rest, and those nodes
        // are excluded here. Every other node is committed, unless the drag ended where it began.
        //
        // ToSequence is needed because iterating the query yields entities on their own, and this
        // needs each entity's trait values as well.
        world.QueryTraits(Position, DragOrigin, Not [| TargetPosition |]).ToSequence()
        |> Seq.choose (fun ((position, origin), entity) ->
            if position = origin then
                None
            else
                Some { Entity = entity; Before = origin; After = position })
        |> List.ofSeq
        |> Command.create
        |> Option.iter (fun command -> world |> commitCommand command)

        world.RemoveAll DragOrigin
        world.Remove DragInFlight

    world

let private applyInput world event =
    match event with
    | DragStarted -> world |> startDrag
    | Dragged distance -> world |> moveParticipants distance
    | DragEnded -> world |> endDrag
    | PointerMissed -> world

let dragNodes (world: IWorld) =
    // Any combination of input events can arrive in one frame, so they are applied one at a time,
    // in the order they were raised.
    world |> inputEvents |> Seq.fold applyInput world
