module Wilnaatahl.Traits.SpaceTraits

open Wilnaatahl.ECS.Trait
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector

/// Used for any visible entity that has an on-screen position.
let Position = mutableTrait zeroPosition MutableVector3.Zero

/// Used for any visible entity that has a position and is animating towards a target position.
/// An entity that has this trait must also have the Position trait, although it's unclear how
/// to enforce this via the ECS.
let TargetPosition = mutableTrait zeroPosition MutableVector3.Zero

/// Represents a distance on each axis. For Bounding Boxes, it's the distance the box maintains around the
/// nodes it contains (taking into account the sizes of the nodes). For nodes, it's the distance out from the
/// node's Position that determines its extent. For rectilinear nodes, it translates directly to the node
/// shape's dimensions. For spherical nodes, the smallest component of this trait becomes the radius.
let Size = valueTrait zeroPosition
