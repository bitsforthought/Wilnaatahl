module Wilnaatahl.Tests.Systems.UndoRedoTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Systems.ViewMode
open Wilnaatahl.Tests.EcsTestSupport

let private getButtonLabel entity =
    match entity |> get Button with
    | Some b -> b.label
    | None -> ""

let private findButton label (world: IWorld) =
    world.Query(With Button) |> Seq.find (fun e -> getButtonLabel e = label)

let private isButtonDisabled entity = (entity |> get Button).Value.disabled

let private isButtonHidden entity = entity |> has Hidden

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let sortOrder, _ = spawnUndoRedoControls (0, world)

    /// Drags a node to the given x: snapshot on drag start, move, then release. Each phase is its
    /// own frame, as the host dispatches them.
    let dragNodeTo node x =
        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent
        node |> setValue Position {| x = x; y = 0.0; z = 0.0 |}
        world.Add DragEndEvent
        handleUndoRedo world |> ignore
        world.Remove DragEndEvent

    /// Clicks a button and runs the frame it lands on.
    let click button =
        button |> add ClickEvent
        handleUndoRedo world |> ignore
        button |> remove ClickEvent

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    /// An undo or redo click moves an entry between the two stacks, so it changes what *both*
    /// buttons should offer. Settling only the clicked one leaves the other disagreeing with its
    /// own stack until some later frame happens to run.
    [<Fact>]
    member _.``an undo click settles the redo button in the same frame``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        dragNodeTo node 10.0

        let redoBtn = world |> findButton "Redo"
        isButtonDisabled redoBtn =! true

        click (world |> findButton "Undo")

        isButtonDisabled redoBtn =! false

    [<Fact>]
    member _.``a redo click settles the undo button in the same frame``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        dragNodeTo node 10.0

        let undoBtn = world |> findButton "Undo"
        click undoBtn
        isButtonDisabled undoBtn =! true

        click (world |> findButton "Redo")

        isButtonDisabled undoBtn =! false

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

    /// Undo and redo are meaningless while inspecting, so the buttons declare themselves
    /// `MoveModeOnly` and leave hiding to the ViewMode system rather than reading the mode.
    [<Fact>]
    member _.``undo and redo buttons are marked Move-mode only``() =
        (world |> findButton "Undo") |> has MoveModeOnly =! true
        (world |> findButton "Redo") |> has MoveModeOnly =! true

    /// A drag start snapshots only *static* positions, so a drag that begins while every selected
    /// node is still animating captures nothing by design. The snapshot entity is spawned before
    /// that is known, so it has to be destroyed rather than left orphaned — otherwise every such
    /// drag start leaks one entity for the life of the session.
    [<Fact>]
    member _.``a drag start that captures nothing leaves no snapshot entity behind``() =
        let _ =
            world.Spawn(
                Position.Val {| x = 5.0; y = 0.0; z = 0.0 |},
                TargetPosition.Val {| x = 9.0; y = 0.0; z = 0.0 |},
                Selected.Tag()
            )

        let entityCount () = world.Query() |> Seq.length
        let before = entityCount ()

        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent

        // Nothing was captured, so nothing was pushed...
        isButtonDisabled (world |> findButton "Undo") =! true
        // ...and no entity was left behind either.
        entityCount () =! before

    /// `handleUndoRedo` recomputes both buttons' `disabled` every frame. A trait write
    /// notifies change subscribers whether or not the value moved, so writing
    /// unconditionally would re-render both buttons 60 times a second.
    [<Fact>]
    member _.``handleUndoRedo writes the undo and redo buttons only when their disabled state changes``() =
        let buttonWrites = createChanged ()
        // Establish the tracker's baseline so it afterwards reports only real writes.
        world.Query(buttonWrites <=> [| Button |]) |> Seq.length |> ignore

        // Both stacks are empty and both buttons already start disabled: no writes.
        handleUndoRedo world |> ignore
        (world.Query(buttonWrites <=> [| Button |]) |> Seq.length) =! 0

        // A drag start pushes an undo snapshot, so only the Undo button changes.
        let _ = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent

        world.Query(buttonWrites <=> [| Button |]) |> Seq.exactlyOne
        =! (world |> findButton "Undo")

    [<Fact>]
    member _.``drag start captures positions and enables undo button``() =
        let _ = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        world.Add DragStartEvent
        handleUndoRedo world |> ignore

        let undoBtn = world |> findButton "Undo"
        isButtonDisabled undoBtn =! false

    [<Fact>]
    member _.``undo restores original position via TargetPosition``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        click (world |> findButton "Undo")

        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0

    [<Fact>]
    member _.``undo then redo re-applies moved position``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        click (world |> findButton "Undo")
        click (world |> findButton "Redo")

        // Redo restores the position captured just before the undo.
        (node |> get TargetPosition).Value =! Line3.pos 10.0 0.0 0.0

    [<Fact>]
    member _.``buttons reflect stack state``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 3.0; z = 1.0 |}, Selected.Tag())

        let undoBtn = world |> findButton "Undo"
        let redoBtn = world |> findButton "Redo"
        isButtonDisabled undoBtn =! true
        isButtonDisabled redoBtn =! true

        dragNodeTo node 10.0
        isButtonDisabled undoBtn =! false
        isButtonDisabled redoBtn =! true

        click undoBtn
        isButtonDisabled redoBtn =! false

    [<Fact>]
    member _.``new drag after undo flushes redo stack``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        click (world |> findButton "Undo")

        let redoBtn = world |> findButton "Redo"
        isButtonDisabled redoBtn =! false

        // New drag: should flush the redo stack.
        // First, simulate that the undo animation completed by removing TargetPosition.
        node |> remove TargetPosition
        dragNodeTo node 15.0

        isButtonDisabled redoBtn =! true

    [<Fact>]
    member _.``view mode hides undo and redo without mutating the stacks``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        // Two drags, so the undo stack has two entries: 5 -> 10, then 10 -> 15.
        dragNodeTo node 10.0
        dragNodeTo node 15.0

        // One undo moves an entry to the redo stack, so both stacks are non-empty.
        let undoBtn = world |> findButton "Undo"
        let redoBtn = world |> findButton "Redo"
        click undoBtn

        isButtonHidden undoBtn =! false
        isButtonHidden redoBtn =! false
        isButtonDisabled undoBtn =! false
        isButtonDisabled redoBtn =! false

        // In View mode, ViewMode hides both buttons.
        world.Add InViewMode
        syncModalControls world |> ignore
        handleUndoRedo world |> ignore
        isButtonHidden undoBtn =! true
        isButtonHidden redoBtn =! true

        // Returning to Move mode reveals them in their stack-derived enabled state, proving
        // the hiding neither popped nor pushed either stack while View mode was active.
        world.Remove InViewMode
        syncModalControls world |> ignore
        handleUndoRedo world |> ignore
        isButtonHidden undoBtn =! false
        isButtonHidden redoBtn =! false
        isButtonDisabled undoBtn =! false
        isButtonDisabled redoBtn =! false

        // Exercise both preserved stacks after returning to Move mode to prove they kept their
        // exact contents, not merely nonzero counts. Redo restores the entry captured just before
        // the earlier undo (15); the first undo then reverses that redo (back to 10); the second
        // undo pops the surviving original undo entry (5).
        click redoBtn
        (node |> get TargetPosition).Value.x =! 15.0
        click undoBtn
        (node |> get TargetPosition).Value.x =! 10.0
        click undoBtn
        (node |> get TargetPosition).Value.x =! 5.0

    /// A click can no longer reach a hidden button — `Events.handleClick` refuses to raise one —
    /// so `handleUndoRedo` carries no mode guard. This pins that a delayed click delivered the way
    /// the view layer delivers it never arrives, and that the stack survives it untouched.
    [<Fact>]
    member _.``a delayed undo click while the button is hidden never arrives and preserves the stack``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0

        // View mode hides the button; a delayed click on it must be dropped at the source.
        world.Add InViewMode
        syncModalControls world |> ignore
        let undoBtn = world |> findButton "Undo"
        undoBtn |> handleClick world
        undoBtn |> has ClickEvent =! false
        handleUndoRedo world |> ignore
        node |> has TargetPosition =! false

        // The undo entry survived, so once back in Move mode the same click restores the
        // original position — proving the dropped click neither popped nor discarded the stack.
        world.Remove InViewMode
        syncModalControls world |> ignore
        handleUndoRedo world |> ignore
        isButtonDisabled undoBtn =! false
        undoBtn |> handleClick world
        handleUndoRedo world |> ignore
        node |> has TargetPosition =! true
        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0
