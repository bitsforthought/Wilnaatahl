module Wilnaatahl.Tests.Traits.EventsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    /// Raises the signal that a drag is running, marking nothing as taking part in it. These
    /// handlers only ever ask whether a drag is running, never what it moves.
    let beginDrag () = world.Add DragInFlight

    /// Ends the synthetic drag started by `beginDrag`.
    let endDrag () = world.Remove DragInFlight

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

    /// Input arrives between frames, so a drag start is visible here before the Dragging system
    /// has run and added `DragInFlight`. The frame a drag starts in is part of the drag.
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

    /// The first drag start decides which nodes the drag moves and where each of them began.
    /// `Events` refuses a start while a drag is running, so the system never sees a second one.
    [<Fact>]
    member _.``handleDragStart raises no DragStartEvent while a drag is in flight``() =
        beginDrag ()
        handleDragStart world
        world.Has DragStartEvent =! false

    /// The selection is what the view layer paints and makes draggable, so clearing it mid-drag
    /// would leave the app dragging nodes it no longer shows as selected.
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
        entity1 |> add ClickEvent
        world.Add DragStartEvent
        world.AddWith DragEvent {| x = 1.0; y = 2.0; z = 3.0 |}
        world.Add DragEndEvent
        world.Add PointerMissedEvent

        entity1 |> has ClickEvent =! true
        world.Has DragStartEvent =! true
        world.Has DragEvent =! true
        world.Has DragEndEvent =! true
        world.Has PointerMissedEvent =! true

        cleanupEvents world |> ignore

        // Entity events should be removed.
        entity1 |> has ClickEvent =! false

        // World events should be removed.
        world.Has DragStartEvent =! false
        world.Has DragEvent =! false
        world.Has DragEndEvent =! false
        world.Has PointerMissedEvent =! false
