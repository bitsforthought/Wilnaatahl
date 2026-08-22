module Wilnaatahl.Systems.UndoRedo

open System.Collections.Generic
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.Controls

let private UndoButton = tagTrait ()
let private RedoButton = tagTrait ()

/// A stack of commands, most recent first.
let private CommandStack = refTrait (fun () -> new Stack<Command>())

let spawnUndoRedoControls (sortOrder, world: IWorld) =
    // The controls are spawned on app startup and never destroyed, which should be fine.
    world.Spawn(
        Button.Val {| sortOrder = sortOrder; label = "Undo"; disabled = true |},
        UndoButton.Tag(),
        CommandStack.Val(new Stack<Command>()),
        MoveModeOnly.Tag()
    )
    |> ignore

    world.Spawn(
        Button.Val {| sortOrder = sortOrder + 1; label = "Redo"; disabled = true |},
        RedoButton.Tag(),
        CommandStack.Val(new Stack<Command>()),
        MoveModeOnly.Tag()
    )
    |> ignore

    sortOrder + 2, world

let private handleDragStart (world: IWorld) (undoStack: Stack<Command>) =
    // Before allowing nodes to move as part of a drag operation, we need to capture their
    // starting positions for posterity. We use Selected and the presence of the DragStartEvent
    // to identify the nodes to process.
    if world.Has DragStartEvent then
        // There are two distinct cases: Either the node about to be dragged was animating,
        // or it was static. We only want to save static positions for Undo.
        world.QueryTrait(Position, With Selected, Not [| TargetPosition |]).ToSequence()
        |> Seq.map (fun (position, entity) -> { Entity = entity; Before = position })
        |> List.ofSeq
        |> Command.create
        |> Option.iter undoStack.Push

let private handleDragEnd (world: IWorld) (redoStack: Stack<Command>) =
    if world.Has DragEndEvent then
        // Drag is ending; Flush the redo history of all nodes to avoid massive time-travel
        // confusion for the user, but only if at least one of the nodes being dragged does
        // *not* have a TargetPosition. Otherwise, that means the user is dragging nodes that
        // are already animating, which is not an "undoable/redoable" operation. We use Selected
        // here as a proxy for being dragged.
        let draggingButNotAnimating = world.Query(With Selected, Not [| TargetPosition |])

        if not (Seq.isEmpty draggingButNotAnimating) then
            redoStack.Clear()

let private updateButtonState buttonEntity (stack: Stack<Command>) =
    // Enable the button when its stack has something to undo/redo.
    buttonEntity |> setButtonDisabled (stack.Count = 0)

let private handleButtonClicked (toStack: Stack<Command>) (fromStack: Stack<Command>) =
    // Disabling the Undo/Redo buttons isn't instantaneous due to delays in React rendering the button.
    // We have to protect against spurious clicks here or Pop() will fail.
    if fromStack.Count > 0 then
        let command = fromStack.Pop()

        // Work out the reverse before moving anything, or it would record where the nodes are
        // going rather than where they are. Nodes are moved by giving them a TargetPosition, so
        // they animate to it instead of jumping.
        let reverse =
            command |> Command.mapPositions (fun move -> move.Entity |> settledPosition)

        for move in command.Moves do
            move.Entity |> addWith TargetPosition move.Before

        toStack.Push reverse

let handleUndoRedo (world: IWorld) =
    // Buttons must exist and have the right traits or we have an app setup issue.
    let undoStack, undoButtonEntity =
        world.QueryTrait(CommandStack, With Button, With UndoButton).ToSequence()
        |> Seq.exactlyOne

    let redoStack, redoButtonEntity =
        world.QueryTrait(CommandStack, With Button, With RedoButton).ToSequence()
        |> Seq.exactlyOne

    // Multi-touch makes it possible to tap Undo and Redo together, and Undo wins.
    if undoButtonEntity |> has ClickEvent then
        undoStack |> handleButtonClicked redoStack
    elif redoButtonEntity |> has ClickEvent then
        redoStack |> handleButtonClicked undoStack

    // The handlers below need no guard against a click. Events refuses a click while a drag is
    // in flight, and drops one raised just before a drag started, so a click cannot share a
    // frame with a drag start or with a real drag end. The one case Events cannot see is a drag
    // end with no drag behind it, because nothing is in flight to refuse against; Dragging
    // removes that one before this system runs.
    undoStack |> handleDragStart world
    redoStack |> handleDragEnd world

    // Anything above can move an entry between the two stacks, so settle both buttons rather
    // than only the one that was clicked.
    undoStack |> updateButtonState undoButtonEntity
    redoStack |> updateButtonState redoButtonEntity
    world
