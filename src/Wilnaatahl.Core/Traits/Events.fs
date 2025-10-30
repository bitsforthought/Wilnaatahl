module Wilnaatahl.Traits.Events

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector

// The following traits are used to flag input events, some global, some on entities.
// They are deleted at the end of every frame to avoid being processed multiple times.
let ClickEvent = tagTrait ()
let DragEndEvent = tagTrait ()
let DragEvent = valueTrait zeroPosition
let DragStartEvent = tagTrait ()
let PointerDownEvent = tagTrait ()
let PointerMissedEvent = tagTrait ()

let handleClick entity = entity |> add ClickEvent

let handleDrag (world: IWorld) x y z =
    world.AddWith DragEvent {| x = x; y = y; z = z |}

let handleDragEnd (world: IWorld) = world.Add DragEndEvent

let handleDragStart (world: IWorld) = world.Add DragStartEvent

let handlePointerDown entity = entity |> add PointerDownEvent

let handlePointerMissed (world: IWorld) = world.Add PointerMissedEvent

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
