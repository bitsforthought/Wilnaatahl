import { Mesh } from "three";
import { trait } from "koota";
import { toKootaTagTrait, toKootaValueFactoryTrait, toKootaValueTrait } from "./koota/kootaWrapper";
import * as FileCommands from "../generated/Systems/FileCommands";
import * as ConnectorTraits from "../generated/Traits/ConnectorTraits";
import * as PeopleTraits from "../generated/Traits/PeopleTraits";
import * as SpaceTraits from "../generated/Traits/SpaceTraits";
import * as ViewTraits from "../generated/Traits/ViewTraits";

// Used to connect entities that represent visible components to Three.js meshes.
export const MeshRef = trait(() => new Mesh());

// Unlike other events, these are not cleared by frame cleanup: they persist until
// the view layer consumes and removes them.
export const OpenFileRequested = toKootaTagTrait(FileCommands.OpenFileRequested);
export const SaveRequested = toKootaTagTrait(FileCommands.SaveRequested);

// Present iff the app is in View mode.
export const InViewMode = toKootaTagTrait(ViewTraits.InViewMode);

export const Elbow = toKootaTagTrait(ConnectorTraits.Elbow);
export const Line = toKootaTagTrait(ConnectorTraits.Line);

export const PersonRef = toKootaValueFactoryTrait(PeopleTraits.PersonRef);
export const NodeLabel = toKootaValueFactoryTrait(PeopleTraits.NodeLabel);

// World-scoped, not per-entity.
export const CurrentLocale = toKootaValueFactoryTrait(ViewTraits.CurrentLocale);

export const Position = toKootaValueFactoryTrait(SpaceTraits.Position);
export const Size = toKootaValueTrait(SpaceTraits.Size);

export const Button = toKootaValueTrait(ViewTraits.Button);
export const DragInFlight = toKootaTagTrait(ViewTraits.DragInFlight);
export const Hidden = toKootaTagTrait(ViewTraits.Hidden);
export const Selected = toKootaTagTrait(ViewTraits.Selected);
