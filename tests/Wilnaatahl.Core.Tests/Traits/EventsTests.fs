module Wilnaatahl.Tests.Traits.EventsTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

[<Fact>]
let ``handleClick adds ClickEvent to entity`` () =
    use ecs = new EcsWorld()
    let entity = ecs.World.Spawn()
    entity |> handleClick
    entity |> has ClickEvent =! true

/// A control is hidden a frame before the view layer stops rendering it, so a click can land
/// on a control the app no longer considers live. Discarding it at the source is what lets the
/// systems that consume clicks stay free of stale-click guards.
[<Fact>]
let ``handleClick raises no ClickEvent on a Hidden entity`` () =
    use ecs = new EcsWorld()
    let entity = ecs.World.Spawn()
    entity |> add Hidden
    entity |> handleClick
    entity |> has ClickEvent =! false

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

[<Fact>]
let ``cleanupEvents removes all event traits`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    // Add entity-level events.
    let entity1 = world.Spawn()
    let entity2 = world.Spawn()
    entity1 |> handleClick
    handlePointerDown entity2

    // Add world-level events.
    handleDragStart world
    handleDrag world 1.0 2.0 3.0
    handleDragEnd world
    handlePointerMissed world

    cleanupEvents world |> ignore

    // Entity events should be removed.
    entity1 |> has ClickEvent =! false
    entity2 |> has PointerDownEvent =! false

    // World events should be removed.
    world.Has DragStartEvent =! false
    world.Has DragEvent =! false
    world.Has DragEndEvent =! false
    world.Has PointerMissedEvent =! false
