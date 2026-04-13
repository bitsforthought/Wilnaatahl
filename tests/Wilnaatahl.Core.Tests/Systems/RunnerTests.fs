module Wilnaatahl.Tests.Systems.RunnerTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.System.Layout
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.LifeCycle
open Wilnaatahl.Systems.Runner
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

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

    // Position starts at origin (0,0,0), so any movement toward target is progress.
    let posBefore = (entity |> get Position).Value.x
    runSystems world 0.5
    let posAfter = (entity |> get Position).Value.x
    posAfter >! posBefore

[<Fact>]
let ``integration: select, drag, and undo on scene nodes`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = createFamilyGraph testPeopleAndParents

    // 0.016 seconds ≈ one frame at 60 FPS
    let frameDelta = 0.016

    spawnControls world
    spawnScene world graph
    layoutNodes world graph

    // Animate to settled positions
    for _ in 1..50 do runSystems world 0.1

    // Find a person node
    let nodeEntity = world.Query(With PersonRef) |> Seq.head
    let originalPos = (nodeEntity |> get Position).Value
    let origX = originalPos.x

    // Click to select
    handlePointerDown nodeEntity
    handleClick nodeEntity
    runSystems world frameDelta
    (nodeEntity |> has Selected) =! true

    // Drag
    handleDragStart world |> ignore
    handleDrag world (origX + 2.0) originalPos.y originalPos.z |> ignore
    runSystems world frameDelta

    // End drag
    handleDragEnd world |> ignore
    runSystems world frameDelta

    // Position should have changed
    let movedPos = (nodeEntity |> get Position).Value
    movedPos.x <>! origX

    // Undo — find undo button using QueryTrait
    let _, undoBtn =
        world.QueryTrait(Button).ToSequence()
        |> Seq.find (fun (buttonData, _) -> buttonData.label = "Undo")
    handleClick undoBtn
    runSystems world frameDelta

    // Should have TargetPosition set (animating back)
    (nodeEntity |> has TargetPosition) =! true
