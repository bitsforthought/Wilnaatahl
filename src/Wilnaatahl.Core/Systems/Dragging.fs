module Wilnaatahl.Systems.Dragging

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
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
    if world.Has DragEndEvent then
        // Whether a drag is in progress is world state, not something to thread down the pipeline:
        // a release can arrive in a frame carrying no movement, and that still ends the drag.
        if world |> anyDragParticipants then
            world.RemoveAll DragOrigin
        else
            // No drag is in progress, so this is a spurious DragEndEvent. We need to prevent it
            // from propagating or it could interfere with Undo/Redo.
            // ASSUMPTION: The dragging system must run before the undo/redo system!
            world.Remove DragEndEvent

    world

let dragNodes (world: IWorld) =
    // There are no early returns because it's possible to have some
    // combination of these events happen in the same frame.
    world |> handleDragStart |> handleDrag |> handleDragEnd
