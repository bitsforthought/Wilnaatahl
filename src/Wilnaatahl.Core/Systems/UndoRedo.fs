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
    // A committed command describes a change that has already happened, so it goes onto the undo
    // stack. It also clears the redo stack, because those entries would re-apply changes made to
    // a version of the scene that no longer exists.
    //
    // This runs before a click is handled, so a click always acts on stacks that already include
    // every change made this frame. Nothing can currently commit a command in the same frame as a
    // click on these buttons — a drag commits when it is released, and a release refuses button
    // clicks — so if a second system starts committing commands, check whether this order is
    // still what you want.
    match world |> committedCommands with
    | [] -> ()
    | commands ->
        commands |> List.iter undoStack.Push
        redoStack.Clear()

let private updateButtonState buttonEntity (stack: Stack<Command>) =
    // Enable the button when its stack has something to undo/redo.
    buttonEntity |> setButtonDisabled (stack.Count = 0)

/// Pops a command off `fromStack` and applies it in one direction: every node it lists is moved to
/// the position `destination` selects from that node's move. The command is then pushed onto
/// `toStack`, so the opposite button can apply it in the other direction.
let private handleButtonClicked destination (toStack: Stack<Command>) (fromStack: Stack<Command>) =
    // Disabling the Undo/Redo buttons isn't instantaneous due to delays in React rendering the button.
    // We have to protect against spurious clicks here or Pop() will fail.
    if fromStack.Count > 0 then
        let command = fromStack.Pop()

        // Nodes are moved by giving them a TargetPosition, so they animate to it instead of
        // jumping.
        for move in command.Moves do
            move.Entity |> addWith TargetPosition (destination move)

        // The same command is used in both directions, so the other button can apply it back.
        toStack.Push command

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

    // Multi-touch makes it possible to tap Undo and Redo in the same frame, and Undo wins. The
    // only difference between the two is which of a move's two positions the node is moved to.
    if undoButtonEntity |> wasClicked world then
        undoStack |> handleButtonClicked _.Before redoStack
    elif redoButtonEntity |> wasClicked world then
        redoStack |> handleButtonClicked _.After undoStack

    // Anything above can move an entry between the two stacks, so settle both buttons rather
    // than only the one that was clicked.
    undoStack |> updateButtonState undoButtonEntity
    redoStack |> updateButtonState redoButtonEntity
    world
