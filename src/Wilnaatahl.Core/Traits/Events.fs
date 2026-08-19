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

/// Whether a drag gesture is under way. True from the moment a drag start is raised until the
/// frame that processes the release has run: input is raised between frames, so a start is
/// visible here before any frame has spawned the entity that represents the drag.
///
/// Nothing but a drag end clears this, so the host is relied on to raise one for every start it
/// raises, including when the pointer is cancelled or capture is lost. A start left unpaired
/// refuses input for as long as it stands.
let private dragInFlight (world: IWorld) =
    world.Has DragStartEvent
    || (world.QueryFirst(RelatedToAny Dragging) |> Option.isSome)

/// Raises a click on the entity, unless it takes no input — because it is `Hidden`, or because a
/// drag is under way. The app hides a control a frame before the view layer stops rendering it,
/// so a click can still land on one; and a pointer committed to a drag is not also pressing a
/// control, so a click reaching here mid-drag is fallout from the drag. Discarding both here
/// keeps them from reaching any consumer.
let handleClick world entity =
    if not (dragInFlight world) && not (entity |> has Hidden) then
        entity |> add ClickEvent

let handleDrag (world: IWorld) x y z =
    world.AddWith DragEvent {| x = x; y = y; z = z |}

let handleDragEnd (world: IWorld) = world.Add DragEndEvent

/// Raises a drag start, discarding input raised earlier in this same gap between frames. That
/// input is usually fallout from the pointer now dragging, but a stalled host can also coalesce a
/// genuine tap into the frame a drag starts in; the drag wins either way, since letting both take
/// effect is what leaves a frame acting on two incompatible intents.
let handleDragStart (world: IWorld) =
    world.RemoveAll ClickEvent
    world.Remove PointerMissedEvent
    world.Add DragStartEvent

let handlePointerDown entity = entity |> add PointerDownEvent

/// Raises a background miss, unless a drag is under way — a drag released over empty space
/// raises one, and that is fallout from the drag rather than a deliberate click away.
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
