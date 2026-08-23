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
open Wilnaatahl.Traits.History
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

    /// Records the given moves as one committed change and runs the frame that picks it up. This
    /// is what a feature does when it commits: undo/redo never watches the feature itself.
    let commit moves =
        world |> commitCommand (Command.create moves).Value
        handleUndoRedo world |> ignore
        world |> clearCommittedCommands

    /// Moves a node to the given x and records the change, the way a completed drag does.
    let dragNodeTo node x =
        let before = (node |> get Position).Value
        node |> setValue Position {| x = x; y = 0.0; z = 0.0 |}

        commit [ { Entity = node; Before = before; After = Line3.pos x 0.0 0.0 } ]

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
    member _.``An undo click settles the redo button in the same frame``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())
        dragNodeTo node 10.0

        let redoBtn = world |> buttonWithLabel "Redo"
        isButtonDisabled redoBtn =! true

        world |> buttonWithLabel "Undo" |> click

        isButtonDisabled redoBtn =! false

    [<Fact>]
    member _.``A redo click settles the undo button in the same frame``() =
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
    member _.``Undo and redo buttons start disabled``() =
        let undoBtn = world |> buttonWithLabel "Undo"
        let redoBtn = world |> buttonWithLabel "Redo"

        isButtonDisabled undoBtn =! true
        isButtonDisabled redoBtn =! true

    /// Undo and redo are meaningless while inspecting, so the buttons declare themselves
    /// `MoveModeOnly` and leave hiding to the ViewMode system rather than reading the mode.
    [<Fact>]
    member _.``Undo and redo buttons are marked Move-mode only``() =
        world |> buttonWithLabel "Undo" |> has MoveModeOnly =! true
        world |> buttonWithLabel "Redo" |> has MoveModeOnly =! true

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

        // A committed command fills the undo stack, so only the Undo button changes.
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})
        dragNodeTo node 10.0

        world.Query(buttonWrites <=> [| Button |]) |> Seq.exactlyOne
        =! (world |> buttonWithLabel "Undo")

    /// Undo/redo no longer watches for drags: it offers whatever has been committed, whoever
    /// committed it.
    [<Fact>]
    member _.``A committed command enables the undo button``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})

        dragNodeTo node 10.0

        world |> buttonWithLabel "Undo" |> isButtonDisabled =! false

    /// A frame in which nothing was committed leaves the stacks exactly as they were, so an idle
    /// frame cannot quietly grow the history.
    [<Fact>]
    member _.``A frame that records nothing leaves the undo button alone``() =
        world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}) |> ignore

        handleUndoRedo world |> ignore

        world |> buttonWithLabel "Undo" |> isButtonDisabled =! true

    [<Fact>]
    member _.``Undo restores original position via TargetPosition``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        world |> buttonWithLabel "Undo" |> click

        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0

    [<Fact>]
    member _.``Undo then redo re-applies moved position``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0
        world |> buttonWithLabel "Undo" |> click
        world |> buttonWithLabel "Redo" |> click

        // Redo restores the position the change moved the node to.
        (node |> get TargetPosition).Value =! Line3.pos 10.0 0.0 0.0

    /// Undo and redo replay the change that was recorded, both of the positions it holds. Working
    /// the other one out from where the node happens to be at click time instead would fold in
    /// whatever else moved it since, so redo would carry the node somewhere the change never
    /// took it.
    [<Fact>]
    member _.``Redo re-applies the recorded change even after something else moved the node``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        dragNodeTo node 10.0

        // Some other feature moves the node before the undo click.
        node |> setValue Position {| x = 99.0; y = 0.0; z = 0.0 |}

        world |> buttonWithLabel "Undo" |> click
        (node |> get TargetPosition).Value =! Line3.pos 5.0 0.0 0.0

        world |> buttonWithLabel "Redo" |> click
        (node |> get TargetPosition).Value =! Line3.pos 10.0 0.0 0.0

    /// Two features can commit in the same frame, and undo takes them back newest first. Pushing
    /// the frame's commands in the wrong order would undo them out of sequence.
    [<Fact>]
    member _.``Two commands committed in one frame are undone newest first``() =
        let node = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})

        // Two changes in one frame: the node went 1 -> 2, then 2 -> 5.
        world |> commitCommand (Command.create [ moveAlongX node 1.0 2.0 ]).Value
        world |> commitCommand (Command.create [ moveAlongX node 2.0 5.0 ]).Value

        handleUndoRedo world |> ignore
        world |> clearCommittedCommands

        world |> buttonWithLabel "Undo" |> click
        (node |> get TargetPosition).Value.x =! 2.0

        world |> buttonWithLabel "Undo" |> click
        (node |> get TargetPosition).Value.x =! 1.0

    /// Every other application test drags one node, which a loop that applied only its first or
    /// last move would still satisfy. Two nodes moving together pin that the whole command is
    /// applied, in both directions.
    [<Fact>]
    member _.``Undo and redo restore every node a drag moved``() =
        let left = world.Spawn(Position.Val {| x = 1.0; y = 0.0; z = 0.0 |})
        let right = world.Spawn(Position.Val {| x = 2.0; y = 0.0; z = 0.0 |})

        left |> setValue Position {| x = 11.0; y = 0.0; z = 0.0 |}
        right |> setValue Position {| x = 12.0; y = 0.0; z = 0.0 |}

        commit [ moveAlongX left 1.0 11.0; moveAlongX right 2.0 12.0 ]

        world |> buttonWithLabel "Undo" |> click

        (left |> get TargetPosition).Value =! Line3.pos 1.0 0.0 0.0
        (right |> get TargetPosition).Value =! Line3.pos 2.0 0.0 0.0

        world |> buttonWithLabel "Redo" |> click

        (left |> get TargetPosition).Value =! Line3.pos 11.0 0.0 0.0
        (right |> get TargetPosition).Value =! Line3.pos 12.0 0.0 0.0

    /// Multi-touch can deliver a tap on both buttons in one frame. Undo wins, and the redo tap is
    /// dropped rather than applied after it.
    [<Fact>]
    member _.``An undo and a redo click in the same frame apply only the undo``() =
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
    member _.``A click on a button with an empty stack does nothing``() =
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
    member _.``Buttons reflect stack state``() =
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
    member _.``New drag after undo flushes redo stack``() =
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
    member _.``View mode hides undo and redo without mutating the stacks``() =
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
        // exact contents, not merely nonzero counts. Redo reapplies the second drag (to 15); the
        // first undo then reverses that drag again (back to 10); the second undo pops the
        // surviving original undo entry (5).
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
    member _.``A delayed undo click while the button is hidden never arrives and preserves the stack``() =
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
