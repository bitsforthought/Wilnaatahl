module Wilnaatahl.Tests.Entities.LineTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport

[<Fact>]
let ``spawn creates line entity with Line and Connector traits`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawn zeroPosition zeroPosition

    lineId |> has Line =! true
    lineId |> has Connector =! true

[<Fact>]
let ``spawn creates lines that are not Hidden by default`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawn zeroPosition zeroPosition

    lineId |> has Hidden =! false

[<Fact>]
let ``spawn creates two endpoints with Position Hidden Connector and EndpointOf`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawn zeroPosition zeroPosition
    let ep1, ep2 = lineId |> Line3.getEndpoints world

    ep1 |> has Position =! true
    ep1 |> has Hidden =! true
    ep1 |> has Connector =! true
    ep2 |> has Position =! true
    ep2 |> has Hidden =! true
    ep2 |> has Connector =! true

[<Fact>]
let ``spawn sets endpoint positions correctly`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawn (Line3.pos 1.0 2.0 3.0) (Line3.pos 4.0 5.0 6.0)
    let v1, v2 = lineId |> Line3.getPositions world

    v1 =! Vector3.FromComponents(1.0, 2.0, 3.0)
    v2 =! Vector3.FromComponents(4.0, 5.0, 6.0)

[<Fact>]
let ``spawnDynamic creates line at origin`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawnDynamic
    let v1, v2 = lineId |> Line3.getPositions world

    v1 =! Vector3.FromComponents(0.0, 0.0, 0.0)
    v2 =! Vector3.FromComponents(0.0, 0.0, 0.0)

[<Fact>]
let ``spawnHidden creates line with Hidden trait`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawnHidden zeroPosition zeroPosition

    lineId |> has Hidden =! true
    lineId |> has Line =! true

[<Fact>]
let ``getEndpoints returns exactly two endpoints`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let lineId = world |> Line3.spawn zeroPosition zeroPosition
    let ep1, ep2 = lineId |> Line3.getEndpoints world

    ep1 <>! ep2

[<Fact>]
let ``snapToWithOffset adds SnapTo relations`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let target = world.Spawn(Position.Val zeroPosition)
    let subject = world.Spawn(Position.Val zeroPosition)

    Line3.snapToWithOffset target (1.0, 2.0, 3.0) subject

    subject |> targetFor SnapToX =! Some target
    subject |> targetFor SnapToY =! Some target
    subject |> targetFor SnapToZ =! Some target
    (subject |> get (SnapToX => target)).Value =! {| x = 1.0 |}
    (subject |> get (SnapToY => target)).Value =! {| y = 2.0 |}
    (subject |> get (SnapToZ => target)).Value =! {| z = 3.0 |}

[<Fact>]
let ``snapTo snaps both endpoints to targets with zero offset`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let target1 = world.Spawn(Position.Val(Line3.pos 10.0 0.0 0.0))
    let target2 = world.Spawn(Position.Val(Line3.pos 20.0 0.0 0.0))

    let lineId = world |> Line3.spawn zeroPosition zeroPosition
    lineId |> Line3.snapTo world target1 target2 |> ignore

    let ep1, ep2 = lineId |> Line3.getEndpoints world
    ep1 |> targetFor SnapToX =! Some target1
    ep2 |> targetFor SnapToX =! Some target2

[<Fact>]
let ``updateEndpoints calls functions on each endpoint position`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let lineId = world |> Line3.spawn (Line3.pos 1.0 0.0 0.0) (Line3.pos 2.0 0.0 0.0)
    let mutable calls = 0

    lineId |> Line3.updateEndpoints world AlwaysTrack
        (fun pos -> pos.x <- 100.0; calls <- calls + 1)
        (fun pos -> pos.x <- 200.0; calls <- calls + 1)

    calls =! 2
    let v1, v2 = lineId |> Line3.getPositions world
    // One endpoint should be 100, the other 200 (order depends on internal iteration)
    Set.ofList [ v1.x; v2.x ] =! Set.ofList [ 100.0; 200.0 ]
