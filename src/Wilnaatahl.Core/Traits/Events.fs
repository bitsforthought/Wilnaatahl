module Wilnaatahl.Traits.Events

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.ViewTraits

// The following traits are used to flag input events, some global, some on entities.
// They are deleted at the end of every frame to avoid being processed multiple times.
let ClickEvent = tagTrait ()
let DragEndEvent = tagTrait ()
let DragEvent = valueTrait zeroPosition
let DragStartEvent = tagTrait ()
let PointerMissedEvent = tagTrait ()

/// Whether a drag is happening. The two traits cover different parts of one drag: input arrives
/// between frames, so `DragStartEvent` covers the window before any system has run, and
/// `DragInFlight` covers the rest, up to and including the frame that handles the release.
///
/// A drag with nothing to move never raises `DragInFlight`, and an event trait lasts a single
/// frame, so input is accepted again from the next frame. Otherwise `DragInFlight` is present
/// until a drag end arrives, so this relies on the browser raising one for every drag it starts —
/// including when the pointer is cancelled or capture is lost.
let private dragInFlight (world: IWorld) =
    world.Has DragStartEvent || world.Has DragInFlight

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

/// Raises a drag start, discarding any click or background miss that arrived since the last frame.
///
/// From this point on input is refused, but input accepted moments earlier is still standing and
/// would be handled in the same frame. A second finger can tap a control just before a drag
/// starts. The drag takes precedence, because handling a tap and a drag in the same frame has no
/// sensible meaning.
///
/// A drag start arriving while a drag is already running is refused like any other input, so
/// every system that reads `DragStartEvent` can assume it means a new drag.
let handleDragStart (world: IWorld) =
    world.RemoveAll ClickEvent
    world.Remove PointerMissedEvent

    if not (dragInFlight world) then
        world.Add DragStartEvent

/// Raises a background miss, unless a drag is happening. The selection is what the view layer
/// paints and makes draggable, so clearing it mid-drag would leave the app dragging nodes it no
/// longer shows as selected.
let handlePointerMissed (world: IWorld) =
    if not (dragInFlight world) then
        world.Add PointerMissedEvent

let cleanupEvents (world: IWorld) =
    // Remove event traits from all entities at the end of the frame.
    world.RemoveAll ClickEvent

    // Global events are world traits, so we have to delete them one by one.
    // The view layer raises them through the handlers above.
    world.Remove PointerMissedEvent
    world.Remove DragStartEvent
    world.Remove DragEvent
    world.Remove DragEndEvent

    // A command belongs to the frame its change happened in. Left standing, the next frame would
    // see the previous frame's changes as its own, once per frame, for a change that happened once.
    world |> clearCommittedCommands
    world
