module Wilnaatahl.Tests.Traits.EventsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    /// Starts a drag on an anonymous node, the way processing a drag start does.
    let beginDrag () =
        world.Spawn(Dragging.ToTargetWith(world.Spawn(), zeroPosition)) |> ignore

    /// Ends the synthetic drag started by `beginDrag`.
    let endDrag () =
        world.Query(RelatedToAny Dragging) |> Seq.iter destroy

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``handleClick adds ClickEvent to entity``() =
        let entity = world.Spawn()
        entity |> handleClick world
        entity |> has ClickEvent =! true

    /// The app hides a control one frame before the view layer stops drawing it, so a click can
    /// still land on a control that is no longer available. Dropping it here means the systems
    /// that read clicks never have to check for it.
    [<Fact>]
    member _.``handleClick raises no ClickEvent on a Hidden entity``() =
        let entity = world.Spawn()
        entity |> add Hidden
        entity |> handleClick world
        entity |> has ClickEvent =! false

    /// A click can reach a control during a drag — from a second finger tapping it, for example.
    /// Acting on it would mean carrying out the click and the drag together, so it is refused.
    [<Fact>]
    member _.``handleClick raises no ClickEvent while a drag is in flight``() =
        let entity = world.Spawn()
        beginDrag ()
        entity |> handleClick world
        entity |> has ClickEvent =! false

    /// Input arrives between frames, so a drag start is visible here before any frame has run to
    /// spawn the entity that represents the drag. The frame a drag starts in is part of the drag.
    [<Fact>]
    member _.``handleClick raises no ClickEvent in the frame a drag starts``() =
        let entity = world.Spawn()
        handleDragStart world
        entity |> handleClick world
        entity |> has ClickEvent =! false

    /// Refusing input must stop when the drag does, or the app would accept no input at all after
    /// the first drag.
    [<Fact>]
    member _.``handleClick adds ClickEvent once a drag has ended``() =
        let entity = world.Spawn()
        beginDrag ()
        endDrag ()
        entity |> handleClick world
        entity |> has ClickEvent =! true

    [<Fact>]
    member _.``handlePointerDown adds PointerDownEvent to entity``() =
        let entity = world.Spawn()
        handlePointerDown entity
        entity |> has PointerDownEvent =! true

    [<Fact>]
    member _.``handleDrag sets DragEvent on world with coordinates``() =
        handleDrag world 1.0 2.0 3.0
        world.Has DragEvent =! true
        world.Get DragEvent =! Some {| x = 1.0; y = 2.0; z = 3.0 |}

    [<Fact>]
    member _.``handleDragStart adds DragStartEvent to world``() =
        handleDragStart world
        world.Has DragStartEvent =! true

    /// Input is refused from the moment a drag start arrives, but a tap accepted just before that
    /// lands in the same frame. Dropping it stops one frame from applying both a tap and a drag.
    [<Fact>]
    member _.``handleDragStart discards input raised before it``() =
        let entity = world.Spawn()
        entity |> handleClick world
        handlePointerMissed world
        entity |> has ClickEvent =! true
        world.Has PointerMissedEvent =! true

        handleDragStart world

        entity |> has ClickEvent =! false
        world.Has PointerMissedEvent =! false

    [<Fact>]
    member _.``handleDragEnd adds DragEndEvent to world``() =
        handleDragEnd world
        world.Has DragEndEvent =! true

    [<Fact>]
    member _.``handlePointerMissed adds PointerMissedEvent to world``() =
        handlePointerMissed world
        world.Has PointerMissedEvent =! true

    /// A background miss during a drag would clear the selection, dropping the node being dragged.
    [<Fact>]
    member _.``handlePointerMissed raises no PointerMissedEvent while a drag is in flight``() =
        beginDrag ()
        handlePointerMissed world
        world.Has PointerMissedEvent =! false

    [<Fact>]
    member _.``cleanupEvents removes all event traits``() =
        // The traits are added directly instead of through the handlers, because the handlers
        // refuse and discard input around a drag and so can never leave every event standing.
        let entity1 = world.Spawn()
        let entity2 = world.Spawn()
        entity1 |> add ClickEvent
        entity2 |> add PointerDownEvent
        world.Add DragStartEvent
        world.AddWith DragEvent {| x = 1.0; y = 2.0; z = 3.0 |}
        world.Add DragEndEvent
        world.Add PointerMissedEvent

        entity1 |> has ClickEvent =! true
        entity2 |> has PointerDownEvent =! true
        world.Has DragStartEvent =! true
        world.Has DragEvent =! true
        world.Has DragEndEvent =! true
        world.Has PointerMissedEvent =! true

        cleanupEvents world |> ignore

        // Entity events should be removed.
        entity1 |> has ClickEvent =! false
        entity2 |> has PointerDownEvent =! false

        // World events should be removed.
        world.Has DragStartEvent =! false
        world.Has DragEvent =! false
        world.Has DragEndEvent =! false
        world.Has PointerMissedEvent =! false
