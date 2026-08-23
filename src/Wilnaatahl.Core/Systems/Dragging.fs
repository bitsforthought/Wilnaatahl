module Wilnaatahl.Systems.Dragging

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits

let private handleDragStart (world: IWorld) =
    // Events refuses a start while a drag is running, so this can only be a drag beginning.
    if world.Has DragStartEvent then
        // What a drag moves is what was selected when it was grabbed, because the view only makes
        // the selection draggable. Recording each participant's starting position here is what
        // fixes the set: from here on the drag reads its own participants, not the selection.
        world.QueryTrait(Position, With Selected).ForEach
        <| fun (origin, entity) -> entity |> addWith DragOrigin origin

    world

let private handleDrag (world: IWorld) =
    match world.Get DragEvent with
    | None -> world // Nothing to do.
    | Some move ->
        // The gesture reports how far it has travelled since it started, so a participant belongs
        // at its own starting position plus that offset. Placing it there rather than nudging it
        // by the change since last frame means a participant lands where the gesture says even if
        // something else moved it, and that rounding cannot accumulate over a long drag.
        world.QueryTraits(Position, DragOrigin).UpdateEachWith AlwaysTrack
        <| fun ((position, origin), _) ->
            position.x <- origin.x + move.x
            position.y <- origin.y + move.y
            position.z <- origin.z + move.z

        world

let private handleDragEnd (world: IWorld) =
    // Whether a drag is in progress is world state, not something to thread down the pipeline: a
    // release can arrive in a frame carrying no movement, and that still ends the drag. A release
    // with no drag behind it is simply a release with nothing to end.
    if world.Has DragEndEvent && world |> anyDragParticipants then
        // History records changes to settled positions. A node with a TargetPosition is settled at
        // that target, and a drag only ever writes Position, so dragging one leaves nothing to
        // take back. Every other participant is settled where it now stands, so it changed unless
        // the gesture put it back where it started.
        //
        // ToSequence materializes the results: the query itself enumerates entities alone, and
        // this needs each entity's values with it.
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

    world

let dragNodes (world: IWorld) =
    // There are no early returns because it's possible to have some
    // combination of these events happen in the same frame.
    world |> handleDragStart |> handleDrag |> handleDragEnd
