module Wilnaatahl.Traits.ViewTraits

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector

/// Used for entities that represent buttons on the toolbar.
let Button = valueTrait {| sortOrder = 0; label = ""; disabled = false |}

/// Used to mark tree nodes that are selected.
let Selected = tagTrait ()

/// Represents an ongoing drag operation as a relation to the node being dragged.
let Dragging =
    mutableRelationWith zeroPosition MutableVector3.Zero { IsExclusive = true }
