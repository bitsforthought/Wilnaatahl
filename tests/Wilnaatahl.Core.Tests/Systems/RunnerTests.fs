module Wilnaatahl.Tests.Systems.RunnerTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Systems.Runner
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Tests.EcsTestSupport

let private spawnControls (world: IWorld) =
    (0, world) |> spawnUndoRedoControls |> spawnSelectControls |> ignore

[<Fact>]
let ``runSystems with no events completes without error`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnControls world
    runSystems world 0.016

[<Fact>]
let ``runSystems cleans up events at end of frame`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnControls world
    handlePointerMissed world |> ignore
    handleDragStart world |> ignore
    world.Has PointerMissedEvent =! true
    world.Has DragStartEvent =! true
    runSystems world 0.016
    world.Has PointerMissedEvent =! false
    world.Has DragStartEvent =! false

[<Fact>]
let ``runSystems animates entities toward target`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnControls world
    let entity =
        world.Spawn(
            Position.Val zeroPosition,
            TargetPosition.Val {| x = 10.0; y = 0.0; z = 0.0 |}
        )
    runSystems world 0.5
    let pos = entity |> get Position
    test <@ pos.IsSome @>
    let p = pos.Value
    test <@ p.x > 0.0 @>
