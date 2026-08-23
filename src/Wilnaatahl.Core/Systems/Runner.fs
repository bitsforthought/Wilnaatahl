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
    //   - updateViewMode runs before the systems that read input, so every later system sees one
    //     settled mode and no click left over from the mode just left.
    //   - dragNodes before handleUndoRedo, because a completed drag records the command that
    //     takes it back and the undo/redo controls pick it up in the same frame. Any future
    //     feature that records commands belongs before handleUndoRedo for the same reason, or its
    //     buttons would lag a frame behind the change.
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
