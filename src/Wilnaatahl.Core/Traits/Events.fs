module Wilnaatahl.Traits.Events

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.ViewTraits

// The following traits are used to flag input events, some global, some on entities.
// They are deleted at the end of every frame to avoid being processed multiple times.
let ClickEvent = tagTrait ()
let DragEndEvent = tagTrait ()
let DragEvent = valueTrait zeroPosition
let DragStartEvent = tagTrait ()
let PointerDownEvent = tagTrait ()
let PointerMissedEvent = tagTrait ()

/// Whether a drag is happening. True from the moment a drag start arrives until the frame that
/// handles the release has run. Input arrives between frames, so a drag start is visible here
/// before any frame has run to spawn the entity that represents the drag.
///
/// A drag start that finds no node to drag spawns no entity, and the frame clears the start
/// event, so input is accepted again next frame. Once a drag entity does exist, only a drag end
/// removes it, so the browser is relied on to raise one for every drag it starts — including when
/// the pointer is cancelled or capture is lost.
let private dragInFlight (world: IWorld) =
    world.Has DragStartEvent
    || (world.QueryFirst(RelatedToAny Dragging) |> Option.isSome)

/// Raises a click on the entity, unless the entity is `Hidden` or a drag is happening.
///
/// The app hides a control one frame before the view layer stops drawing it, so a click can still
/// land on a control that is no longer available. A click can also arrive during a drag — from a
/// second finger tapping a control, for example, or from a drag short enough that the browser
/// counts it as a tap too. Whatever the source, acting on a click during a drag would mean
/// carrying out that click and the drag together, so clicks are dropped here rather than in the
/// systems that read them.
let handleClick world entity =
    if not (dragInFlight world) && not (entity |> has Hidden) then
        entity |> add ClickEvent

let handleDrag (world: IWorld) x y z =
    world.AddWith DragEvent {| x = x; y = y; z = z |}

let handleDragEnd (world: IWorld) = world.Add DragEndEvent

/// Raises a drag start, dropping any click or background miss that arrived since the last frame.
///
/// Input is refused from this point on, but input accepted moments earlier lands in the same
/// frame and would still take effect. A tap by another finger can arrive just before a drag
/// starts without the browser having stalled. The drag wins, because carrying out a tap and a
/// drag in the same frame has no coherent meaning.
let handleDragStart (world: IWorld) =
    world.RemoveAll ClickEvent
    world.Remove PointerMissedEvent
    world.Add DragStartEvent

let handlePointerDown entity = entity |> add PointerDownEvent

/// Raises a background miss, unless a drag is happening. A miss during a drag would clear the
/// selection, dropping the node being dragged.
let handlePointerMissed (world: IWorld) =
    if not (dragInFlight world) then
        world.Add PointerMissedEvent

let cleanupEvents (world: IWorld) =
    // Remove event traits from all entities at the end of the frame.
    world.RemoveAll PointerDownEvent
    world.RemoveAll ClickEvent

    // Global events are world traits, so we have to delete them one by one.
    // See eventActions.ts to see how events get created.
    world.Remove PointerMissedEvent
    world.Remove DragStartEvent
    world.Remove DragEvent
    world.Remove DragEndEvent
    world
