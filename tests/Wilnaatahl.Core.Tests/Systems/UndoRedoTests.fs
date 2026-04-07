module Wilnaatahl.Tests.Systems.UndoRedoTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Tests.EcsTestSupport

let private getButtonLabel entity =
    match entity |> get Button with
    | Some b -> b.label
    | None -> ""

let private findButton label (world: IWorld) =
    world.Query(With Button) |> Seq.find (fun e -> getButtonLabel e = label)

let private isButtonDisabled entity =
    match entity |> get Button with
    | Some b -> b.disabled
    | None -> true

[<Fact>]
let ``spawnUndoRedoControls creates undo and redo buttons`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let sortOrder, _ = spawnUndoRedoControls (0, world)

    sortOrder =! 2
    let buttons = world.Query(With Button) |> Seq.toList
    buttons.Length =! 2
    let labels = buttons |> List.map getButtonLabel |> List.sort
    labels =! [ "Redo"; "Undo" ]

[<Fact>]
let ``undo and redo buttons start disabled`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnUndoRedoControls (0, world) |> ignore

    let undoBtn = world |> findButton "Undo"
    let redoBtn = world |> findButton "Redo"

    isButtonDisabled undoBtn =! true
    isButtonDisabled redoBtn =! true

[<Fact>]
let ``drag start captures positions and enables undo button`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnUndoRedoControls (0, world) |> ignore

    let _ =
        world.Spawn(
            Position.Val {| x = 5.0; y = 0.0; z = 0.0 |},
            Selected.Tag()
        )

    world.Add DragStartEvent
    handleUndoRedo world |> ignore

    let undoBtn = world |> findButton "Undo"
    isButtonDisabled undoBtn =! false

[<Fact>]
let ``undo restores original position via TargetPosition`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnUndoRedoControls (0, world) |> ignore

    let node =
        world.Spawn(
            Position.Val {| x = 5.0; y = 0.0; z = 0.0 |},
            Selected.Tag()
        )

    // Capture position on drag start
    world.Add DragStartEvent
    handleUndoRedo world |> ignore
    world.Remove DragStartEvent

    // Simulate moving the node
    node |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}

    // End the drag
    world.Add DragEndEvent
    handleUndoRedo world |> ignore
    world.Remove DragEndEvent

    // Click undo
    let undoBtn = world |> findButton "Undo"
    undoBtn |> add ClickEvent
    handleUndoRedo world |> ignore

    // Node should have TargetPosition set to original position
    let targetPos = (node |> get TargetPosition).Value
    targetPos.x =! 5.0
    targetPos.y =! 0.0
    targetPos.z =! 0.0

[<Fact>]
let ``undo then redo re-applies moved position`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnUndoRedoControls (0, world) |> ignore

    let node =
        world.Spawn(
            Position.Val {| x = 5.0; y = 0.0; z = 0.0 |},
            Selected.Tag()
        )

    // Drag: capture, move, end
    world.Add DragStartEvent
    handleUndoRedo world |> ignore
    world.Remove DragStartEvent
    node |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}
    world.Add DragEndEvent
    handleUndoRedo world |> ignore
    world.Remove DragEndEvent

    // Undo
    let undoBtn = world |> findButton "Undo"
    undoBtn |> add ClickEvent
    handleUndoRedo world |> ignore
    undoBtn |> remove ClickEvent

    // Redo should re-apply the position we were at before undo
    let redoBtn = world |> findButton "Redo"
    redoBtn |> add ClickEvent
    handleUndoRedo world |> ignore

    // The node should now have a TargetPosition set to the position
    // that was saved before the undo (which was 10.0)
    let targetPos = (node |> get TargetPosition).Value
    targetPos.x =! 10.0

[<Fact>]
let ``buttons reflect stack state`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnUndoRedoControls (0, world) |> ignore

    let _ =
        world.Spawn(
            Position.Val {| x = 5.0; y = 3.0; z = 1.0 |},
            Selected.Tag()
        )

    // Initially both disabled
    let undoBtn = world |> findButton "Undo"
    let redoBtn = world |> findButton "Redo"
    isButtonDisabled undoBtn =! true
    isButtonDisabled redoBtn =! true

    // After drag start+end, undo is enabled
    world.Add DragStartEvent
    handleUndoRedo world |> ignore
    world.Remove DragStartEvent
    world.Add DragEndEvent
    handleUndoRedo world |> ignore
    world.Remove DragEndEvent

    isButtonDisabled undoBtn =! false
    isButtonDisabled redoBtn =! true

    // After undo, redo is enabled (button state updates on next frame)
    undoBtn |> add ClickEvent
    handleUndoRedo world |> ignore
    undoBtn |> remove ClickEvent

    // Run again with no events to update button states via the else branch
    handleUndoRedo world |> ignore

    isButtonDisabled redoBtn =! false
