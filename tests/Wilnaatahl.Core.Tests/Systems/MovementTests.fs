module Wilnaatahl.Tests.Systems.MovementTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.Entities
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Systems.Movement
open Wilnaatahl.Tests.EcsTestSupport

/// Triggers a Changed notification for Position on the given entity.
let private touchPosition entity =
    let pos = (entity |> get Position).Value
    entity |> setValue Position {| x = pos.x; y = pos.y; z = pos.z |}

[<Fact>]
let ``move updates SnapToX entity position`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    let entityA = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})
    let entityB = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})
    entityB |> addWith (SnapToX => entityA) {| x = 2.0 |}

    entityA |> touchPosition
    move tracker world |> ignore

    let posB = (entityB |> get Position).Value
    posB.x =! 7.0

[<Fact>]
let ``move updates snapped points on all axes`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    let entityA = world.Spawn(Position.Val {| x = 10.0; y = 20.0; z = 30.0 |})
    let entityB = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})
    entityB |> addWith (SnapToX => entityA) {| x = 1.0 |}
    entityB |> addWith (SnapToY => entityA) {| y = 2.0 |}
    entityB |> addWith (SnapToZ => entityA) {| z = 3.0 |}

    entityA |> touchPosition
    move tracker world |> ignore

    let posB = (entityB |> get Position).Value
    posB.x =! 11.0
    posB.y =! 22.0
    posB.z =! 33.0

[<Fact>]
let ``move updates bisecting entity to midpoint of line`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    let lineId = world |> Line3.spawn zeroPosition zeroPosition
    let bisectEntity = world.Spawn(Position.Val zeroPosition, Connector.Tag())
    bisectEntity |> add (Bisects => lineId)

    // Set endpoint positions after spawn to trigger Changed
    let ep1, ep2 = lineId |> Line3.getEndpoints world
    ep1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
    ep2 |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}

    move tracker world |> ignore

    let pos = (bisectEntity |> get Position).Value
    pos.x =! 5.0
    pos.y =! 0.0
    pos.z =! 0.0

[<Fact>]
let ``move propagates through kinematic chain`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    // Chain: C snaps to B, B snaps to A
    let entityA = world.Spawn(Position.Val {| x = 100.0; y = 0.0; z = 0.0 |})
    let entityB = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})
    let entityC = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})
    entityB |> addWith (SnapToX => entityA) {| x = 5.0 |}
    entityC |> addWith (SnapToX => entityB) {| x = 3.0 |}

    entityA |> touchPosition
    move tracker world |> ignore

    let posB = (entityB |> get Position).Value
    let posC = (entityC |> get Position).Value
    posB.x =! 105.0
    posC.x =! 108.0

[<Fact>]
let ``move with no position changes does nothing`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    let entity = world.Spawn(Position.Val {| x = 5.0; y = 5.0; z = 5.0 |})

    // Drain initial state by touching and running once
    entity |> touchPosition
    move tracker world |> ignore

    // Now run again with no changes
    move tracker world |> ignore

    let pos = (entity |> get Position).Value
    pos.x =! 5.0
    pos.y =! 5.0
    pos.z =! 5.0

[<Fact>]
let ``move updates bounding box corners`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    let boxId, _, _ = world |> BoundingBox.spawn {| x = 0.5; y = 0.5; z = 0.5 |}

    let child1 = world.Spawn(Position.Val {| x = 1.0; y = 1.0; z = 1.0 |})
    let child2 = world.Spawn(Position.Val {| x = 5.0; y = 5.0; z = 5.0 |})
    boxId |> add (BoundingBoxOn => child1)
    boxId |> add (BoundingBoxOn => child2)

    child1 |> touchPosition
    child2 |> touchPosition
    move tracker world |> ignore

    // Verify corners were adjusted. The min corner should be at child1 - margins,
    // and the max corner at child2 + margins.
    let c1, c2 = boxId |> BoundingBox.getCorners world
    let pos1 = (c1 |> get Position).Value
    let pos2 = (c2 |> get Position).Value

    let positions =
        Set.ofList [
            (pos1.x, pos1.y, pos1.z)
            (pos2.x, pos2.y, pos2.z)
        ]

    // One should be min corner (0.5, 0.5, 0.5), other max corner (5.5, 5.5, 5.5)
    positions =! Set.ofList [ (0.5, 0.5, 0.5); (5.5, 5.5, 5.5) ]

[<Fact>]
let ``move updates parallel line with positive offset on horizontal line`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    // Source line: horizontal along X-axis
    let sourceLineId = world |> Line3.spawn zeroPosition zeroPosition
    let srcEp1, srcEp2 = sourceLineId |> Line3.getEndpoints world
    srcEp1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
    srcEp2 |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}

    // Parallel line
    let parallelLineId = world |> Line3.spawnDynamic
    parallelLineId |> addWith (Parallels => sourceLineId) {| offset = 1.0 |}

    srcEp1 |> touchPosition
    srcEp2 |> touchPosition
    move tracker world |> ignore

    // For a horizontal X-axis line, the perpendicular in the vertical plane is +Y.
    // So positive offset should move the parallel line in the +Y direction.
    let pv1, pv2 = parallelLineId |> Line3.getPositions world
    test <@ pv1.y > 0.0 @>
    test <@ pv2.y > 0.0 @>

[<Fact>]
let ``move updates parallel line with negative offset`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    let sourceLineId = world |> Line3.spawn zeroPosition zeroPosition
    let srcEp1, srcEp2 = sourceLineId |> Line3.getEndpoints world
    srcEp1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
    srcEp2 |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}

    let parallelLineId = world |> Line3.spawnDynamic
    parallelLineId |> addWith (Parallels => sourceLineId) {| offset = -1.0 |}

    srcEp1 |> touchPosition
    srcEp2 |> touchPosition
    move tracker world |> ignore

    // Negative offset should move in the -Y direction.
    let pv1, pv2 = parallelLineId |> Line3.getPositions world
    test <@ pv1.y < 0.0 @>
    test <@ pv2.y < 0.0 @>

[<Fact>]
let ``move handles parallel line on vertical line`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    // Vertical line along Y-axis
    let sourceLineId = world |> Line3.spawn zeroPosition zeroPosition
    let srcEp1, srcEp2 = sourceLineId |> Line3.getEndpoints world
    srcEp1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
    srcEp2 |> setValue Position {| x = 0.0; y = 10.0; z = 0.0 |}

    let parallelLineId = world |> Line3.spawnDynamic
    parallelLineId |> addWith (Parallels => sourceLineId) {| offset = 1.0 |}

    srcEp1 |> touchPosition
    srcEp2 |> touchPosition
    move tracker world |> ignore

    // For a vertical line, the perpendicular falls back to a horizontal direction.
    // dir = (0,1,0), abs(dir.x)=0 < abs(dir.z)=0 is false → alt = (0,0,0.1)
    // The parallel line should be offset in the Z direction.
    let pv1, pv2 = parallelLineId |> Line3.getPositions world
    test <@ abs pv1.z > 0.0 @>
    test <@ abs pv2.z > 0.0 @>

[<Fact>]
let ``move handles parallel line on z-axis line`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    // Line along Z-axis
    let sourceLineId = world |> Line3.spawn zeroPosition zeroPosition
    let srcEp1, srcEp2 = sourceLineId |> Line3.getEndpoints world
    srcEp1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
    srcEp2 |> setValue Position {| x = 0.0; y = 0.0; z = 10.0 |}

    let parallelLineId = world |> Line3.spawnDynamic
    parallelLineId |> addWith (Parallels => sourceLineId) {| offset = 1.0 |}

    srcEp1 |> touchPosition
    srcEp2 |> touchPosition
    move tracker world |> ignore

    // dir = (0,0,1), up = (0,1,0). perpUp = up - (up·dir)*dir = (0,1,0).
    // The perpendicular is the Y direction, not X, because up is already perpendicular to Z.
    let pv1, pv2 = parallelLineId |> Line3.getPositions world
    test <@ abs pv1.y > 0.0 @>
    test <@ abs pv2.y > 0.0 @>

[<Fact>]
let ``move handles parallel line on nearly-vertical line with z perturbation`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    // Nearly vertical line with tiny Z perturbation: dir ≈ (0, 1, 1e-15).
    // perpUp length ≈ 1e-15 < nearZero, so the fallback path is taken.
    // abs(dir.x)=0 < abs(dir.z)≈1e-15 → true → alt = (1, 0, 0)
    let sourceLineId = world |> Line3.spawn zeroPosition zeroPosition
    let srcEp1, srcEp2 = sourceLineId |> Line3.getEndpoints world
    srcEp1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
    srcEp2 |> setValue Position {| x = 0.0; y = 1.0; z = 1e-15 |}

    let parallelLineId = world |> Line3.spawnDynamic
    parallelLineId |> addWith (Parallels => sourceLineId) {| offset = 1.0 |}

    srcEp1 |> touchPosition
    srcEp2 |> touchPosition
    move tracker world |> ignore

    // With alt = (1, 0, 0), the perpendicular should be in the X direction.
    let pv1, pv2 = parallelLineId |> Line3.getPositions world
    test <@ abs pv1.x > 0.0 @>
    test <@ abs pv2.x > 0.0 @>
