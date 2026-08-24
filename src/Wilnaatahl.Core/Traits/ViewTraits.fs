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

/// World signal present exactly when the app is in View (inspection) mode, absent in Move
/// (manipulation) mode. It *is* the app's mode rather than a mirror of one: nothing else stores
/// the mode, and exactly one system writes it.
let InViewMode = tagTrait ()

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
