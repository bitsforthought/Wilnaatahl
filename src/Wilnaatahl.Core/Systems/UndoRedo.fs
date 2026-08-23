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

let private pushCommitted (world: IWorld) (undoStack: Stack<Command>) (redoStack: Stack<Command>) =
    // A committed command is a change that has already happened, so it goes onto the undo stack.
    // It also invalidates the redo history: the future those entries led to branched off a scene
    // that no longer exists.
    //
    // This runs before a click is applied, so a click always acts on stacks that already reflect
    // every change made this frame. Nothing can currently commit a command in the same frame as a
    // click on these buttons — a drag commits at release, and a release refuses button clicks —
    // so whoever adds the second committer should decide whether that precedence still reads right.
    match world |> committedCommands with
    | [] -> ()
    | commands ->
        commands |> List.iter undoStack.Push
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

    // Commands are pushed before a click is applied; see pushCommitted for why.
    pushCommitted world undoStack redoStack

    // Multi-touch makes it possible to tap Undo and Redo together, and Undo wins.
    if undoButtonEntity |> has ClickEvent then
        undoStack |> handleButtonClicked redoStack
    elif redoButtonEntity |> has ClickEvent then
        redoStack |> handleButtonClicked undoStack

    // Anything above can move an entry between the two stacks, so settle both buttons rather
    // than only the one that was clicked.
    undoStack |> updateButtonState undoButtonEntity
    redoStack |> updateButtonState redoButtonEntity
    world
