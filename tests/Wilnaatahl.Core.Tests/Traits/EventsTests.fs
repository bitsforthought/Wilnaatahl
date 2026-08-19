module Wilnaatahl.Tests.Traits.EventsTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

/// Spawns a drag entity for an anonymous node, as processing a drag start does.
let private beginDrag (world: IWorld) =
    world.Spawn(Dragging.ToTargetWith(world.Spawn(), zeroPosition)) |> ignore

[<Fact>]
let ``handleClick adds ClickEvent to entity`` () =
    use ecs = new EcsWorld()
    let entity = ecs.World.Spawn()
    entity |> handleClick ecs.World
    entity |> has ClickEvent =! true

/// A control is hidden a frame before the view layer stops rendering it, so a click can land
/// on a control the app no longer considers live. Discarding it at the source is what lets the
/// systems that consume clicks stay free of stale-click guards.
[<Fact>]
let ``handleClick raises no ClickEvent on a Hidden entity`` () =
    use ecs = new EcsWorld()
    let entity = ecs.World.Spawn()
    entity |> add Hidden
    entity |> handleClick ecs.World
    entity |> has ClickEvent =! false

/// A pointer committed to a drag isn't also pressing a control, so a click arriving mid-drag is
/// fallout from the drag rather than input in its own right.
[<Fact>]
let ``handleClick raises no ClickEvent while a drag is in flight`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let entity = world.Spawn()
    beginDrag world
    entity |> handleClick world
    entity |> has ClickEvent =! false

/// Input is raised between frames, so a drag start can be seen before any frame has spawned the
/// drag entity it will be recognized by. The frame that starts a drag is already part of it.
[<Fact>]
let ``handleClick raises no ClickEvent in the frame a drag starts`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let entity = world.Spawn()
    handleDragStart world
    entity |> handleClick world
    entity |> has ClickEvent =! false

/// Refusing input during a drag must not outlast the drag, or the app would take no input at all
/// after the first one.
[<Fact>]
let ``handleClick adds ClickEvent once a drag has ended`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let entity = world.Spawn()
    beginDrag world
    world.Query(RelatedToAny Dragging) |> Seq.iter destroy
    entity |> handleClick world
    entity |> has ClickEvent =! true

[<Fact>]
let ``handlePointerDown adds PointerDownEvent to entity`` () =
    use ecs = new EcsWorld()
    let entity = ecs.World.Spawn()
    handlePointerDown entity
    entity |> has PointerDownEvent =! true

[<Fact>]
let ``handleDrag sets DragEvent on world with coordinates`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    handleDrag world 1.0 2.0 3.0
    world.Has DragEvent =! true
    world.Get DragEvent =! Some {| x = 1.0; y = 2.0; z = 3.0 |}

[<Fact>]
let ``handleDragStart adds DragStartEvent to world`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    handleDragStart world
    world.Has DragStartEvent =! true

/// A drag refuses input from the moment its start is raised, but input accepted just before that
/// lands in the same frame. Discarding it keeps a coalesced frame from applying both.
[<Fact>]
let ``handleDragStart discards input raised before it`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let entity = world.Spawn()
    entity |> handleClick world
    handlePointerMissed world
    entity |> has ClickEvent =! true
    world.Has PointerMissedEvent =! true

    handleDragStart world

    entity |> has ClickEvent =! false
    world.Has PointerMissedEvent =! false

[<Fact>]
let ``handleDragEnd adds DragEndEvent to world`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    handleDragEnd world
    world.Has DragEndEvent =! true

[<Fact>]
let ``handlePointerMissed adds PointerMissedEvent to world`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    handlePointerMissed world
    world.Has PointerMissedEvent =! true

/// A drag that ends over empty space raises a background miss, which would otherwise read as a
/// deliberate click away from the selection.
[<Fact>]
let ``handlePointerMissed raises no PointerMissedEvent while a drag is in flight`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    beginDrag world
    handlePointerMissed world
    world.Has PointerMissedEvent =! false

[<Fact>]
let ``cleanupEvents removes all event traits`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    // The traits are set directly rather than through the handlers, which refuse and discard
    // input around a drag and so cannot leave every event standing at once.
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
