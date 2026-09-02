module Wilnaatahl.Traits.ViewTraits

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector

/// Used for entities that represent buttons on the toolbar.
let Button = valueTrait {| sortOrder = 0; label = ""; disabled = false |}

/// Marks an entity as unavailable: it is not rendered, and no click is raised on it.
let Hidden = tagTrait ()

/// Used to mark tree nodes that are selected.
let Selected = tagTrait ()

/// Marks a toolbar button that is only meaningful in Move mode. Bearers declare the marker and
/// nothing else: the ViewMode system alone decides what it means, adding and removing `Hidden` to
/// match the mode. Since `Events.handleClick` raises no click on a `Hidden` entity, a bearer never
/// needs to read the mode or guard against clicks arriving in the wrong one.
let internal MoveModeOnly = tagTrait ()

/// The mode the app is in: `Viewing` inspects one node at a time, `Moving` manipulates the scene.
// Deliberately not a [<StringEnum>]: Fable types a lambda returning one as `string` while still
// typing its container by the union, so the emitted `CurrentMode` factory would fail to type-check.
type AppMode =
    | Viewing
    | Moving

/// World-singleton trait holding the app's mode. It *is* the app's mode rather than a mirror of
/// one: nothing else stores the mode, and exactly one system writes it.
let CurrentMode = refTrait (fun () -> Viewing)

/// Whether the mode inspects nodes rather than manipulating them. This looks trivial, but it
/// makes mode-testing in TypeScript substantially easier.
let isViewing mode = mode = Viewing

/// The app's current mode. Fails when no mode has been established.
let internal currentMode (world: IWorld) =
    match world.Get CurrentMode with
    | Some mode -> mode
    | None -> failwith "The world has no CurrentMode. Spawn the view-mode controls first."

/// Added to every node that a running drag is moving, and holds the position that node had when
/// the drag started. The set of nodes is decided when the drag starts, so selecting or deselecting
/// a node while the drag runs does not change which nodes it moves.
let internal DragOrigin = valueTrait zeroPosition

/// Present while a drag is running. The Dragging system adds it when a drag starts with at least
/// one node selected, and removes it when the drag ends. A drag that starts with nothing selected
/// has no nodes to move, so it never adds this.
///
/// This records only that a drag is running; `DragOrigin` records which nodes it moves.
let DragInFlight = tagTrait ()

/// World-singleton trait holding the active UI Locale. It is written by the TS view
/// layer (from the browser locale) and read by both F#-side and TS-side consumers to
/// resolve localizable chrome.
let CurrentLocale = refTrait (fun () -> En)

/// Marks the singleton entity that stands for the empty scene/background: clicking outside any
/// node or control resolves to a click on this entity rather than a special-cased event.
let internal Background = tagTrait ()
