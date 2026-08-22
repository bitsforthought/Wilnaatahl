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
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Systems.ViewMode
open Wilnaatahl.Tests.EcsTestSupport

let private isButtonHidden entity = entity |> has Hidden

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let sortOrder, _ = spawnUndoRedoControls (0, world)

    /// Drags a node to the given x: record on drag start, move, then release. Each phase is its
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

        let redoBtn = world |> buttonWithLabel "Redo"
        isButtonDisabled redoBtn =! true

        world |> buttonWithLabel "Undo" |> click

        isButtonDisabled redoBtn =! false

    [<Fact>]
    member _.``a redo click settles the undo button in the same frame``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        dragNodeTo node 10.0

        let undoBtn = world |> buttonWithLabel "Undo"
        click undoBtn
        isButtonDisabled undoBtn =! true

        world |> buttonWithLabel "Redo" |> click

        isButtonDisabled undoBtn =! false

    [<Fact>]
    member _.``spawnUndoRedoControls creates undo and redo buttons``() =
        sortOrder =! 2
        let buttons = world.Query(With Button) |> Seq.toList
        buttons.Length =! 2
        let labels = buttons |> List.map buttonLabel |> List.sort
        labels =! [ "Redo"; "Undo" ]

    [<Fact>]
    member _.``undo and redo buttons start disabled``() =
        let undoBtn = world |> buttonWithLabel "Undo"
        let redoBtn = world |> buttonWithLabel "Redo"

        isButtonDisabled undoBtn =! true
        isButtonDisabled redoBtn =! true

    /// Undo and redo are meaningless while inspecting, so the buttons declare themselves
    /// `MoveModeOnly` and leave hiding to the ViewMode system rather than reading the mode.
    [<Fact>]
    member _.``undo and redo buttons are marked Move-mode only``() =
        world |> buttonWithLabel "Undo" |> has MoveModeOnly =! true
        world |> buttonWithLabel "Redo" |> has MoveModeOnly =! true

    /// Only a node that has stopped moving can be put back where it started, so a drag that
    /// begins while every selected node is still animating records nothing.
    [<Fact>]
    member _.``a drag start that captures nothing records no undo entry``() =
        let _ =
            world.Spawn(
                Position.Val {| x = 5.0; y = 0.0; z = 0.0 |},
                TargetPosition.Val {| x = 9.0; y = 0.0; z = 0.0 |},
                Selected.Tag()
            )

        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent

        world |> buttonWithLabel "Undo" |> isButtonDisabled =! true

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

        // A drag start records an undo entry, so only the Undo button changes.
        let _ = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent

        world.Query(buttonWrites <=> [| Button |]) |> Seq.exactlyOne
        =! (world |> buttonWithLabel "Undo")

    [<Fact>]
    member _.``drag start captures positions and enables undo button``() =
        let _ = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        world.Add DragStartEvent
        handleUndoRedo world |> ignore

        let undoBtn = world |> buttonWithLabel "Undo"
        isButtonDisabled undoBtn =! false

    [<Fact>]
    member _.``undo restores original position via TargetPosition``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        world |> buttonWithLabel "Undo" |> click

        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0

    [<Fact>]
    member _.``undo then redo re-applies moved position``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        world |> buttonWithLabel "Undo" |> click
        world |> buttonWithLabel "Redo" |> click

        // Redo restores the position captured just before the undo.
        (node |> get TargetPosition).Value =! Line3.pos 10.0 0.0 0.0

    /// Every other application test drags one node, which a loop that applied only its first or
    /// last move would still satisfy. Two nodes moving together pin that the whole command is
    /// applied, in both directions.
    [<Fact>]
    member _.``undo and redo restore every node a drag moved``() =
        let left = world.Spawn(Position.Val {| x = 1.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        let right =
            world.Spawn(Position.Val {| x = 2.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        world.Add DragStartEvent
        handleUndoRedo world |> ignore
        world.Remove DragStartEvent
        left |> setValue Position {| x = 11.0; y = 0.0; z = 0.0 |}
        right |> setValue Position {| x = 12.0; y = 0.0; z = 0.0 |}
        world.Add DragEndEvent
        handleUndoRedo world |> ignore
        world.Remove DragEndEvent

        world |> buttonWithLabel "Undo" |> click

        (left |> get TargetPosition).Value =! Line3.pos 1.0 0.0 0.0
        (right |> get TargetPosition).Value =! Line3.pos 2.0 0.0 0.0

        world |> buttonWithLabel "Redo" |> click

        (left |> get TargetPosition).Value =! Line3.pos 11.0 0.0 0.0
        (right |> get TargetPosition).Value =! Line3.pos 12.0 0.0 0.0

    /// Multi-touch can deliver a tap on both buttons in one frame. Undo wins, and the redo tap is
    /// dropped rather than applied after it.
    [<Fact>]
    member _.``an undo and a redo click in the same frame apply only the undo``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        // Two drags and one undo, so both stacks hold an entry.
        dragNodeTo node 10.0
        dragNodeTo node 15.0
        world |> buttonWithLabel "Undo" |> click

        let undoBtn = world |> buttonWithLabel "Undo"
        let redoBtn = world |> buttonWithLabel "Redo"
        undoBtn |> add ClickEvent
        redoBtn |> add ClickEvent
        handleUndoRedo world |> ignore

        // Undoing again heads back to where the first drag began. Redo would have gone to 15,
        // and running both would have landed on 10.
        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0

    /// A button is disabled once its stack empties, but the view layer takes a frame to stop
    /// drawing it, so a click can still reach a button with nothing to pop. Clicking Undo
    /// afterwards proves the spurious click neither moved the node nor spent the undo entry.
    [<Fact>]
    member _.``a click on a button with an empty stack does nothing``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        dragNodeTo node 10.0

        let redoBtn = world |> buttonWithLabel "Redo"
        isButtonDisabled redoBtn =! true

        redoBtn |> click

        node |> has TargetPosition =! false

        world |> buttonWithLabel "Undo" |> click

        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0
        isButtonDisabled redoBtn =! false

    [<Fact>]
    member _.``buttons reflect stack state``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 3.0; z = 1.0 |}, Selected.Tag())

        let undoBtn = world |> buttonWithLabel "Undo"
        let redoBtn = world |> buttonWithLabel "Redo"
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
        world |> buttonWithLabel "Undo" |> click

        let redoBtn = world |> buttonWithLabel "Redo"
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
        let undoBtn = world |> buttonWithLabel "Undo"
        let redoBtn = world |> buttonWithLabel "Redo"
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
        let undoBtn = world |> buttonWithLabel "Undo"
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
