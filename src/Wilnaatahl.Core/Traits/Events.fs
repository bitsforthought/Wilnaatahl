module Wilnaatahl.Traits.Events

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.ViewTraits

/// One piece of input from the view layer. A drag's distance is measured from the position where
/// the drag started, not from the position on the previous frame.
type internal InputEvent =
    | DragStarted
    | Dragged of distance: {| x: float; y: float; z: float |}
    | DragEnded
    | PointerMissed

// A click is flagged with a trait rather than queued, because it belongs to the entity it landed
// on. Like the input queue below, clicks are discarded at the end of every frame so that no frame
// acts on the previous frame's input.
let ClickEvent = tagTrait ()

// The input raised since the last frame, in the order the view layer raised it. It is held in one
// ResizeArray that is emptied at the end of each frame rather than replaced, so the queue itself
// is allocated once and not per frame.
//
// The view layer raises input between frames only, so nothing is appended while the systems are
// running. That is what lets a system read the queue directly instead of copying it first.
let private InputQueue = refTrait (fun () -> ResizeArray<InputEvent>())

let private queue (world: IWorld) =
    match world.Get InputQueue with
    | Some queued -> queued
    | None ->
        let queued = ResizeArray<InputEvent>()
        world.AddWith InputQueue queued
        queued

let private raiseInput event (world: IWorld) = (world |> queue).Add event

/// The input raised since the last frame, in the order it was raised.
let internal inputEvents (world: IWorld) = (world |> queue) :> seq<InputEvent>

/// Whether a drag is happening. The two checks cover different parts of one drag: input arrives
/// between frames, so a queued `DragStarted` covers the window before any system has run, and
/// `DragInFlight` covers the rest, up to and including the frame that handles the release.
///
/// A drag with nothing to move never raises `DragInFlight`, and a frame's input is discarded at
/// the end of that frame, so input is accepted again from the next frame. Otherwise
/// `DragInFlight` is present until a drag end arrives, so this relies on the browser raising one
/// for every drag it starts — including when the pointer is cancelled or capture is lost.
let private dragInFlight (world: IWorld) =
    world.Has DragInFlight || world |> inputEvents |> Seq.contains DragStarted

/// Removes every background miss from the queue, leaving the rest of it in order.
let private discardPointerMisses (world: IWorld) =
    (world |> queue).RemoveAll(fun event -> event = PointerMissed) |> ignore

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
    world |> raiseInput (Dragged {| x = x; y = y; z = z |})

let handleDragEnd (world: IWorld) = world |> raiseInput DragEnded

/// Raises a drag start, discarding any click or background miss that arrived since the last frame.
///
/// While a drag runs, clicks, background misses and further drag starts are refused, so every
/// system that reads `DragStarted` can assume it means a new drag. Movement and releases are
/// still accepted, because a running drag needs them.
///
/// Input accepted moments earlier has not been discarded and would be handled in the same frame:
/// a second finger can tap a control just before a drag starts. The drag takes precedence,
/// because handling a tap and a drag in the same frame has no sensible meaning. Only clicks and
/// background misses are discarded — a queued release may belong to a drag that is still running,
/// and dropping it would leave that drag running with no release left to end it.
let handleDragStart (world: IWorld) =
    world.RemoveAll ClickEvent
    world |> discardPointerMisses

    if not (dragInFlight world) then
        world |> raiseInput DragStarted

/// Raises a background miss, unless a drag is happening. The selection is what the view layer
/// paints and makes draggable, so clearing it mid-drag would leave the app dragging nodes it no
/// longer shows as selected.
let handlePointerMissed (world: IWorld) =
    if not (dragInFlight world) then
        world |> raiseInput PointerMissed

let cleanupEvents (world: IWorld) =
    // Input belongs to the frame it arrived in. Left standing, the next frame would act on it a
    // second time.
    world.RemoveAll ClickEvent
    (world |> queue).Clear()

    // A command belongs to the frame its change happened in. Left standing, the next frame would
    // see the previous frame's changes as its own, once per frame, for a change that happened once.
    world |> clearCommittedCommands
    world
