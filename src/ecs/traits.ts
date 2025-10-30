import { Mesh } from "three";
import { trait } from "koota";
import {
  toKootaRelation,
  toKootaTagTrait,
  toKootaValueFactoryTrait,
  toKootaValueTrait,
} from "./koota/kootaWrapper";
import * as Events from "../generated/Traits/Events";
import * as ConnectorTraits from "../generated/Traits/ConnectorTraits";
import * as PeopleTraits from "../generated/Traits/PeopleTraits";
import * as SpaceTraits from "../generated/Traits/SpaceTraits";
import * as ViewTraits from "../generated/Traits/ViewTraits";

// Used to connect entities that represent visible components to Three.js meshes.
export const MeshRef = trait(() => new Mesh());

// Re-export traits from the F# side.
export const ClickEvent = toKootaTagTrait(Events.ClickEvent);

export const Elbow = toKootaTagTrait(ConnectorTraits.Elbow);
export const Hidden = toKootaTagTrait(ConnectorTraits.Hidden);
export const Line = toKootaTagTrait(ConnectorTraits.Line);

export const PersonRef = toKootaValueFactoryTrait(PeopleTraits.PersonRef);

export const Position = toKootaValueFactoryTrait(SpaceTraits.Position);
export const Size = toKootaValueTrait(SpaceTraits.Size);

export const Button = toKootaValueTrait(ViewTraits.Button);
export const Dragging = toKootaRelation(ViewTraits.Dragging);
export const Selected = toKootaTagTrait(ViewTraits.Selected);
