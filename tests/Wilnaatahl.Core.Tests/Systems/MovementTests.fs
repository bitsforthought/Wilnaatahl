module Wilnaatahl.Tests.Systems.MovementTests

open System
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

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let tracker = createChanged ()

    [<Fact>]
    member _.``move updates SnapToX entity position``() =
        let entityA = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})
        let entityB = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})
        entityB |> addWith (SnapToX => entityA) {| x = 2.0 |}

        entityA |> touchPosition
        move tracker world |> ignore

        let posB = (entityB |> get Position).Value
        posB.x =! 7.0

    [<Fact>]
    member _.``move updates snapped points on all axes``() =
        let entityA = world.Spawn(Position.Val {| x = 10.0; y = 20.0; z = 30.0 |})
        let entityB = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})
        entityB |> addWith (SnapToX => entityA) {| x = 1.0 |}
        entityB |> addWith (SnapToY => entityA) {| y = 2.0 |}
        entityB |> addWith (SnapToZ => entityA) {| z = 3.0 |}

        entityA |> touchPosition
        move tracker world |> ignore

        let posB = (entityB |> get Position).Value
        posB =! Line3.pos 11.0 22.0 33.0

    [<Fact>]
    member _.``move updates bisecting entity to midpoint of line``() =
        let lineId = world |> Line3.spawn zeroPosition zeroPosition
        let bisectEntity = world.Spawn(Position.Val zeroPosition, Connector.Tag())
        bisectEntity |> add (Bisects => lineId)

        // Set endpoint positions after spawn to trigger Changed
        let ep1, ep2 = lineId |> Line3.getEndpoints world
        ep1 |> setValue Position {| x = 2.0; y = 4.0; z = 6.0 |}
        ep2 |> setValue Position {| x = 10.0; y = 20.0; z = 30.0 |}

        move tracker world |> ignore

        let pos = (bisectEntity |> get Position).Value
        pos =! Line3.pos 6.0 12.0 18.0

    [<Fact>]
    member _.``move propagates through kinematic chain``() =
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
    member _.``move updates bounding box corners``() =
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

        // One should be min corner (0.5, 0.5, 0.5), other max corner (5.5, 5.5, 5.5)
        Set.ofList [ pos1; pos2 ]
        =! Set.ofList [ Line3.pos 0.5 0.5 0.5; Line3.pos 5.5 5.5 5.5 ]

    [<Theory>]
    [<InlineData(1.0, 1.0)>]
    [<InlineData(-1.0, -1.0)>]
    member _.``move updates parallel line on horizontal line``(offset: float, expectedY: float) =
        // Source line: horizontal along X-axis
        let sourceLineId = world |> Line3.spawn zeroPosition zeroPosition
        let srcEp1, srcEp2 = sourceLineId |> Line3.getEndpoints world
        srcEp1 |> setValue Position {| x = 0.0; y = 0.0; z = 0.0 |}
        srcEp2 |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}

        // Parallel line
        let parallelLineId = world |> Line3.spawnDynamic
        parallelLineId |> addWith (Parallels => sourceLineId) {| offset = offset |}

        srcEp1 |> touchPosition
        srcEp2 |> touchPosition
        move tracker world |> ignore

        // For a horizontal X-axis line, the perpendicular in the vertical plane is +Y.
        let pv1, pv2 = parallelLineId |> Line3.getPositions world
        pv1 =! Vector3.FromComponents(0.0, expectedY, 0.0)
        pv2 =! Vector3.FromComponents(10.0, expectedY, 0.0)

    [<Fact>]
    member _.``move handles parallel line on vertical line``() =
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
        pv1 =! Vector3.FromComponents(0.0, 0.0, 1.0)
        pv2 =! Vector3.FromComponents(0.0, 10.0, 1.0)

    [<Fact>]
    member _.``move handles parallel line on z-axis line``() =
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
        pv1 =! Vector3.FromComponents(0.0, 1.0, 0.0)
        pv2 =! Vector3.FromComponents(0.0, 1.0, 10.0)

    [<Fact>]
    member _.``move handles parallel line on nearly-vertical line with z perturbation``() =
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
        pv1 =! Vector3.FromComponents(1.0, 0.0, 0.0)
        pv2 =! Vector3.FromComponents(1.0, 1.0, 1e-15)

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
