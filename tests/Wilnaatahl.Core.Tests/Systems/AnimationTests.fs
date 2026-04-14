module Wilnaatahl.Tests.Systems.AnimationTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Systems.Animation
open Wilnaatahl.Tests.EcsTestSupport

[<Fact>]
let ``animate moves position toward target`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let entity =
        world.Spawn(Position.Val {| x = 10.0; y = 0.0; z = 0.0 |}, TargetPosition.Val {| x = 0.0; y = 0.0; z = 0.0 |})

    animate 0.1 world |> ignore

    let pos = (entity |> get Position).Value
    pos.x <! 10.0
    pos.x >! 0.0
    pos.y =! 0.0
    pos.z =! 0.0
    (entity |> has TargetPosition) =! true

[<Fact>]
let ``animate snaps to target and removes TargetPosition when close`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let entity =
        world.Spawn(
            Position.Val {| x = 0.005; y = 0.005; z = 0.005 |},
            TargetPosition.Val {| x = 0.0; y = 0.0; z = 0.0 |}
        )

    // With damp rate 5.0 and delta 0.5: factor = 1 - exp(-2.5) ≈ 0.918
    // new ≈ lerp (0.005, ...) (0, ...) 0.918 ≈ (0.0004, ...)
    // |deltaV| ≈ 0.0004 < 0.01 → snaps to target and removes TargetPosition
    animate 0.5 world |> ignore

    let pos = (entity |> get Position).Value
    pos =! Line3.pos 0.0 0.0 0.0
    (entity |> has TargetPosition) =! false

[<Fact>]
let ``animate does nothing with zero delta`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let entity =
        world.Spawn(Position.Val {| x = 5.0; y = 5.0; z = 5.0 |}, TargetPosition.Val {| x = 0.0; y = 0.0; z = 0.0 |})

    animate 0.0 world |> ignore

    let pos = (entity |> get Position).Value
    pos =! Line3.pos 5.0 5.0 5.0

[<Fact>]
let ``animate ignores entities without TargetPosition`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let entity = world.Spawn(Position.Val {| x = 3.0; y = 4.0; z = 5.0 |})

    animate 0.1 world |> ignore

    let pos = (entity |> get Position).Value
    pos =! Line3.pos 3.0 4.0 5.0

[<Fact>]
let ``animate handles multiple entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let e1 =
        world.Spawn(Position.Val {| x = 10.0; y = 0.0; z = 0.0 |}, TargetPosition.Val {| x = 0.0; y = 0.0; z = 0.0 |})

    let e2 =
        world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |}, TargetPosition.Val {| x = 10.0; y = 10.0; z = 10.0 |})

    animate 0.1 world |> ignore

    let pos1 = (e1 |> get Position).Value
    test <@ pos1.x < 10.0 && pos1.x > 0.0 @>

    let pos2 = (e2 |> get Position).Value
    test <@ pos2.x > 0.0 && pos2.x < 10.0 @>
    test <@ pos2.y > 0.0 && pos2.y < 10.0 @>
    test <@ pos2.z > 0.0 && pos2.z < 10.0 @>
