module Wilnaatahl.Tests.Systems.RunnerTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.LifeCycle
open Wilnaatahl.Systems.Runner
open Wilnaatahl.Tests.EcsTestSupport

let private mother = { Person.Empty with Id = PersonId 0; Shape = Sphere; Wilp = Some(WilpName "T") }
let private father = { Person.Empty with Id = PersonId 1; Shape = Cube }
let private child = { Person.Empty with Id = PersonId 2; Shape = Sphere; Wilp = Some(WilpName "T") }
let private coParents = { Mother = mother.Id; Father = father.Id }
let private testFamily = [ mother, None; father, None; child, Some coParents ]

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
    let p = (entity |> get Position).Value
    p.x >! 0.0

[<Fact>]
let ``integration: select, drag, and undo on scene nodes`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = createFamilyGraph testFamily

    spawnControls world
    spawnScene world graph
    Wilnaatahl.System.Layout.layoutNodes world graph

    // Animate to settled positions
    for _ in 1..50 do runSystems world 0.1

    // Find a person node
    let nodeEntity = world.Query(With PersonRef) |> Seq.head
    let originalPos = (nodeEntity |> get Position).Value
    let origX = originalPos.x

    // Click to select
    handlePointerDown nodeEntity
    handleClick nodeEntity
    runSystems world 0.016
    (nodeEntity |> has Selected) =! true

    // Drag
    handleDragStart world |> ignore
    handleDrag world (origX + 2.0) originalPos.y originalPos.z |> ignore
    runSystems world 0.016

    // End drag
    handleDragEnd world |> ignore
    runSystems world 0.016

    // Position should have changed
    let movedPos = (nodeEntity |> get Position).Value
    movedPos.x <>! origX

    // Undo — find undo button and click it
    let undoBtn = world.Query(With Button) |> Seq.find (fun e -> (e |> get Button).Value.label = "Undo")
    handleClick undoBtn
    runSystems world 0.016

    // Should have TargetPosition set (animating back)
    (nodeEntity |> has TargetPosition) =! true
