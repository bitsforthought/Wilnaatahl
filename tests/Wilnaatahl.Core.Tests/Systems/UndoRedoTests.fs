module Wilnaatahl.Tests.Systems.UndoRedoTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
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
    (entity |> get Button).Value.disabled

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let sortOrder, _ = spawnUndoRedoControls (0, world)

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnUndoRedoControls creates undo and redo buttons``() =
        sortOrder =! 2
        let buttons = world.Query(With Button) |> Seq.toList
        buttons.Length =! 2
        let labels = buttons |> List.map getButtonLabel |> List.sort
        labels =! [ "Redo"; "Undo" ]

    [<Fact>]
    member _.``undo and redo buttons start disabled``() =
        let undoBtn = world |> findButton "Undo"
        let redoBtn = world |> findButton "Redo"

        isButtonDisabled undoBtn =! true
        isButtonDisabled redoBtn =! true

    [<Fact>]
    member _.``drag start captures positions and enables undo button``() =
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
    member _.``undo restores original position via TargetPosition``() =
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
        targetPos =! Line3.pos 5.0 0.0 0.0

    [<Fact>]
    member _.``undo then redo re-applies moved position``() =
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
        targetPos =! Line3.pos 10.0 0.0 0.0

    [<Fact>]
    member _.``buttons reflect stack state``() =
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

    [<Fact>]
    member _.``new drag after undo flushes redo stack``() =
        let node =
            world.Spawn(
                Position.Val {| x = 5.0; y = 0.0; z = 0.0 |},
                Selected.Tag()
            )

        // Drag: capture at 5, move to 10, end
        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent
        node |> setValue Position {| x = 10.0; y = 0.0; z = 0.0 |}
        world.Add DragEndEvent
        handleUndoRedo world |> ignore
        world.Remove DragEndEvent

        // Undo: pushes to redo stack
        let undoBtn = world |> findButton "Undo"
        undoBtn |> add ClickEvent
        handleUndoRedo world |> ignore
        undoBtn |> remove ClickEvent

        // Redo button should be enabled (redo stack non-empty)
        handleUndoRedo world |> ignore
        let redoBtn = world |> findButton "Redo"
        isButtonDisabled redoBtn =! false

        // New drag: should flush the redo stack.
        // First, simulate that the undo animation completed by removing TargetPosition.
        node |> remove TargetPosition
        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent
        node |> setValue Position {| x = 15.0; y = 0.0; z = 0.0 |}
        world.Add DragEndEvent
        handleUndoRedo world |> ignore
        world.Remove DragEndEvent

        // Redo button should now be disabled (redo stack flushed)
        handleUndoRedo world |> ignore
        isButtonDisabled redoBtn =! true
