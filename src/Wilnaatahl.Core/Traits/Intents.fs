module Wilnaatahl.Traits.Intents

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits

/// A user-facing action a click resolves to, independent of which entity was clicked or which
/// system carries it out.
type internal Intent =
    | Undo
    | Redo
    | ChangeMode of newMode: AppMode
    | ToggleMultiSelect
    | ToggleNodeSelection of node: EntityId
    | ClearSelection
    | OpenFile
    | Save

/// The ordered intents resolved for clicks on an entity. Most bearers declare exactly one; an
/// entity with no bearing on any behaviour never gets this trait.
let internal EmitsIntent = refTrait (fun () -> ([]: Intent list))

/// Resolves queued clicks to an ordered intent list from each target's `EmitsIntent` declaration
/// at derivation time. The returned list is independent of later declaration mutations, which are
/// observed by later derivations.
let internal derivedIntents (world: IWorld) : Intent list =
    world
    |> inputEvents
    |> Seq.collect (function
        | Clicked(target, _) -> target |> get EmitsIntent |> Option.defaultValue []
        | DragStarted
        | Dragged _
        | DragEnded -> [])
    |> List.ofSeq
