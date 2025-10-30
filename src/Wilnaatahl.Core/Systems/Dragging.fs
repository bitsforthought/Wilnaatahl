module Wilnaatahl.Systems.Dragging

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ViewModel
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits

// Used to mark tree nodes that are touched.
let private Touched = tagTrait ()

// The dragging system relies on knowing the last "touched" node in
// order to map co-ordinates from DragControl to a tree node. We could
// use raycasting, but this seems simpler.
let private trackTouchedNodes (world: IWorld) =
    // Due to multi-touch, there could technically be more than one PointerDownEvent
    // in a frame, but in practice it requires an improbable level of dexterity to
    // pull off. We should be fine just getting the first arbitrary PointerDownEvent.
    match world.QueryFirst(With PointerDownEvent, With PersonRef) with
    | Some event ->
        // Clear the last touched node and replace with this one.
        world.RemoveAll Touched
        event |> add Touched
        world
    | None -> world // Nothing to do.

let private handleDragStart (world: IWorld) =
    if not (world.Has DragStartEvent) then
        world // Nothing to do.
    else
        // A node ought to have been touched before starting a drag; if not, we can't proceed.
        match world.QueryFirstTrait(Position, With Touched) with
        | Some(nodeEntity, origin) ->
            world.Spawn((Dragging => nodeEntity).Val origin) |> ignore
            world
        | None ->
            // There really should be a Touched node at this point. If there isn't, we should hear about it.
            printfn $"{nameof handleDragStart}: No Touched node found."
            world

let private handleDrag (world: IWorld) =
    match world.Get DragEvent with
    | None -> None // Nothing to do
    | Some move ->
        match world.QueryFirstTarget Dragging with
        | Some(dragEntity, nodeEntity, origin) ->
            match nodeEntity |> get Position with
            | Some oldPosition ->
                let originV, moveV, oldPosV =
                    Vector3.FromPosition origin, Vector3.FromPosition move, Vector3.FromPosition oldPosition

                let delta = originV + moveV - oldPosV

                world.QueryTrait(Position, With Selected).UpdateEachWith AlwaysTrack
                <| fun (pos, _) ->
                    pos.x <- pos.x + delta.x
                    pos.y <- pos.y + delta.y
                    pos.z <- pos.z + delta.z

                Some dragEntity
            | None ->
                // All nodes should have positions.
                failwith $"Drag target {nodeEntity} found without a Position."
        | None ->
            // No dragging entity found, which probably shouldn't happen.
            printfn $"{nameof handleDrag}: No Dragging entity found even though a drag is in progress."
            None

let private handleDragEnd maybeDragEntity (world: IWorld) =
    if world.Has DragEndEvent then
        match maybeDragEntity with
        | Some dragEntity ->
            dragEntity |> destroy
            // There should be a spurious click event in the same frame.
            // Delete it so it doesn't trigger selection.
            // ASSUMPTION: The dragging system must run before the selection system!
            world.RemoveAll ClickEvent
        | None ->
            // If there is no drag operation present, that means this is a spurious
            // DragEndEvent. We need to prevent it from propagating or it could interfere
            // with Undo/Redo.
            // ASSUMPTION: The dragging system must run before the undo/redo system!
            world.Remove DragEndEvent

    world

let dragNodes (world: IWorld) =
    // There are no early returns because it's possible to have some
    // combination of these events happen in the same frame.
    let dragEntity = world |> trackTouchedNodes |> handleDragStart |> handleDrag
    world |> handleDragEnd dragEntity
