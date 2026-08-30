module Wilnaatahl.Systems.Runner

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.Traits.Events
open Wilnaatahl.Systems.Animation
open Wilnaatahl.Systems.Dragging
open Wilnaatahl.Systems.FileCommands
open Wilnaatahl.Systems.Movement
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Systems.ViewMode

/// Exposes systems that are implemented in TypeScript so we can include them in runSystems.
[<AutoOpen>]
module private TypeScriptSystems =
#if FABLE_COMPILER
    open Fable.Core

    /// Calls the rendering system; Must be called on each frame.
    [<Import("render", "../../ecs/rendering.ts")>]
    let render: IWorld -> IWorld = nativeOnly

#else

    /// Unit test stub for rendering that does nothing.
    let render (world: IWorld) = world

#endif

/// Change tracker used to detect changing Positions by the Movement system.
/// This has to be global because otherwise they allocate in an unbounded fashion, which is very bad.
let private movementTracker = createChanged ()

/// Runs all systems in the correct order for a single frame.
let runSystems (world: IWorld) delta =
    // The order is behavioural, not incidental:
    //   - updateViewMode runs before selectNodes. It clears Selected when the mode changes, and
    //     Selection then applies the queued clicks. ViewMode and Selection both write Selected,
    //     so fixed Runner order cannot preserve both node-then-mode and mode-then-node.
    //     Deriving ordered intents before either system acts is the planned resolution.
    //   - dragNodes runs before handleUndoRedo because a completed drag commits the command that
    //     undo/redo must see in the same frame. Any future command-committing system belongs
    //     before handleUndoRedo for the same reason.
    world
    |> animate delta
    |> updateViewMode
    |> dragNodes
    |> selectNodes
    |> handleUndoRedo
    |> handleFileCommands
    |> move movementTracker
    |> render
    |> cleanupEvents
    |> ignore

    ()
