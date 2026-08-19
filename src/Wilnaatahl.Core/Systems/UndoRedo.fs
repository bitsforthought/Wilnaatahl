module Wilnaatahl.Systems.UndoRedo

open System.Collections.Generic
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.Controls

let private UndoButton = tagTrait ()
let private RedoButton = tagTrait ()

// Used to define an undo/redo stack of entities. It's safe to keep these entity IDs
// outside the ECS because they represent tree nodes, which are all created at app startup
// and only destroyed on shutdown.
let private UndoRedoStack = refTrait (fun () -> new Stack<EntityId>())

let spawnUndoRedoControls (sortOrder, world: IWorld) =
    // The controls are spawned on app startup and never destroyed, which should be fine.
    world.Spawn(
        Button.Val {| sortOrder = sortOrder; label = "Undo"; disabled = true |},
        UndoButton.Tag(),
        UndoRedoStack.Val(new Stack<EntityId>()),
        MoveModeOnly.Tag()
    )
    |> ignore

    world.Spawn(
        Button.Val {| sortOrder = sortOrder + 1; label = "Redo"; disabled = true |},
        RedoButton.Tag(),
        UndoRedoStack.Val(new Stack<EntityId>()),
        MoveModeOnly.Tag()
    )
    |> ignore

    sortOrder + 2, world

[<AutoOpen>]
module private Snapshot =
    // Used to capture the original position of a node at the beginning of a drag operation.
    // For efficiency, the target of the relation will be the snapshot itself, since targets
    // require extra bookkeeping in Koota to track them.
    let private SnapshottedBy = valueRelation zeroPosition

    type Snapshot = private { World: IWorld; Entity: EntityId; mutable HasItems: bool }

    let getSnapshot world entity = { World = world; Entity = entity; HasItems = false }

    let capture entity position snapshot =
        entity |> addRelationWith SnapshottedBy snapshot.Entity position
        snapshot.HasItems <- true

    let destroy snapshot = snapshot.Entity |> destroy

    let getEntities snapshot =
        snapshot.World.Query(Related(SnapshottedBy, snapshot.Entity))

    let getSavedPositionFor entity snapshot =
        entity |> getRelationValue SnapshottedBy snapshot.Entity

    /// Keeps the snapshot by pushing it onto the given stack, or discards it when it captured
    /// nothing — the entity is spawned before that is known, so an empty one must be destroyed
    /// rather than left orphaned in the world.
    let pushTo (stack: Stack<EntityId>) snapshot =
        if snapshot.HasItems then
            stack.Push snapshot.Entity
        else
            snapshot |> destroy

let private handleDragStart (world: IWorld) (undoStack: Stack<EntityId>) =
    // Before allowing nodes to move as part of a drag operation, we need to capture their
    // starting positions for posterity. We use Selected and the presence of the DragStartEvent
    // to identify the nodes to process.
    if world.Has DragStartEvent then
        let snapshot = getSnapshot world (world.Spawn())

        // There are two distinct cases: Either the node about to be dragged was animating,
        // or it was static. We only want to save static positions for Undo.
        world.QueryTrait(Position, With Selected, Not [| TargetPosition |]).ForEach
        <| fun (pos, entity) -> snapshot |> capture entity pos

        snapshot |> pushTo undoStack

let private handleDragEnd (world: IWorld) (redoStack: Stack<EntityId>) =
    if world.Has DragEndEvent then
        // Drag is ending; Flush the redo history of all nodes to avoid massive time-travel
        // confusion for the user, but only if at least one of the nodes being dragged does
        // *not* have a TargetPosition. Otherwise, that means the user is dragging nodes that
        // are already animating, which is not an "undoable/redoable" operation. We use Selected
        // here as a proxy for being dragged.
        let draggingButNotAnimating = world.Query(With Selected, Not [| TargetPosition |])

        if not (Seq.isEmpty draggingButNotAnimating) then
            while redoStack.Count > 0 do
                let snapshot = getSnapshot world (redoStack.Pop())
                snapshot |> destroy

let private updateButtonState buttonEntity (stack: Stack<EntityId>) =
    // Enable the button when its stack has something to undo/redo.
    buttonEntity |> setButtonDisabled (stack.Count = 0)

let private handleButtonClicked (world: IWorld) (toStack: Stack<EntityId>) (fromStack: Stack<EntityId>) =
    // Disabling the Undo/Redo buttons isn't instantaneous due to delays in React rendering the button.
    // We have to protect against spurious clicks here or Pop() will fail.
    if fromStack.Count > 0 then
        let snapshot = getSnapshot world (fromStack.Pop())
        let newSnapshot = getSnapshot world (world.Spawn())

        // How Undo/Redo behaves depends on whether the node being manipulated is static or animating.
        // The invariants we want to maintain are:
        // 1. Positions saved on either stack represent static positions, not intermediate positions on
        //    an animated path.
        // 2. When restoring an old position, the node should animate to that old position, so we're
        //    using a static position from one of the stacks to set a new TargetPosition.
        // This should provide the most intuitive UX.
        for entity in snapshot |> getEntities do
            let posToSave =
                match entity |> getFirst TargetPosition Position with
                | Some pos -> pos
                | None -> failwith $"Entity {entity} from snapshot has no TargetPosition or Position."

            let newPos =
                match snapshot |> getSavedPositionFor entity with
                | Some p -> p
                | None -> failwith $"Entity {entity} from snapshot has no saved position."

            newSnapshot |> capture entity posToSave
            entity |> addWith TargetPosition newPos

        newSnapshot |> pushTo toStack
        snapshot |> destroy

let handleUndoRedo (world: IWorld) =
    // Buttons must exist and have the right traits or we have an app setup issue.
    let undoStack, undoButtonEntity =
        world.QueryTrait(UndoRedoStack, With Button, With UndoButton).ToSequence()
        |> Seq.exactlyOne

    let redoStack, redoButtonEntity =
        world.QueryTrait(UndoRedoStack, With Button, With RedoButton).ToSequence()
        |> Seq.exactlyOne

    // Multi-touch makes it possible to tap Undo and Redo together. Undo takes precedence over
    // Redo. A tap can no longer coexist with a drag in the same frame — `Events` refuses input
    // once a drag start is raised and discards input raised just before it — so the drag handlers
    // below are unreachable in any frame that also carries a button click, and the `else` that
    // guards them is vestigial.
    if undoButtonEntity |> has ClickEvent then
        undoStack |> handleButtonClicked world redoStack
    elif redoButtonEntity |> has ClickEvent then
        redoStack |> handleButtonClicked world undoStack
    else
        undoStack |> handleDragStart world
        redoStack |> handleDragEnd world

    // Every branch above can move an entry between the two stacks, so settle both buttons
    // rather than only the one that was clicked.
    undoStack |> updateButtonState undoButtonEntity
    redoStack |> updateButtonState redoButtonEntity
    world
