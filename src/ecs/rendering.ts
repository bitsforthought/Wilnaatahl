import { CylinderGeometry, Mesh, MeshStandardMaterial, Quaternion, Vector3 } from "three";
import { Entity, Not, World } from "koota";
import { IWorld, toKootaWorld } from "./koota/kootaWrapper";
import { Hidden, Line, MeshRef, PersonRef, Position, Selected } from "./traits";
import { getLinePositions } from "./connectors";
import {
  defaultArg,
  map as mapOption,
} from "../generated/fable_modules/fable-library-ts.4.27.0/Option.js";
import type { Person, Pdeek, Wilp } from "../generated/Model";

// MAINTENANCE NOTE: Keep all presentation-specific constants up here for ease of update.
const defaultEmissiveIntensity = 0;
const defaultEmissiveColour = "#000000"; // No emissive colour for unselected nodes.

const selectedNodeColour = "#8B4000"; // Deep, red copper
const selectedEmissiveIntensity = 0.8;

const lineCylinderRadius = 0.03;
const lineCylinderRadialSegments = 8;

// Base hue for each Pdeek (Clan), in HSL degrees. Each Wilp's specific shade is derived
// by hashing the Wilp name into a small lightness offset around the Pdeek's midpoint, so
// every Wilp keeps a recognizable family colour while remaining visually distinct.
const pdeekBaseHue: Record<Pdeek, number> = {
  giskaast: 0, // Fireweed → red
  ganeda: 130, // Frog → green
  laxSkiik: 50, // Eagle → yellow
  laxGibuu: 220, // Wolf → blue
};

const pdeekSaturationPercent = 75;
const pdeekLightnessMidPercent = 50;
const pdeekLightnessRangePercent = 12; // ±12% around the midpoint

// Color for tree nodes whose Person has no Wilp affiliation. A royal purple that's not too
// dark, so unaffiliated spouses remain clearly visible against the dark background.
const unaffiliatedColour = hslString(270, 60, 55);

// Many of these functions will be called for every rendered tree node, so they will be
// faster as standalone functions.
function setPositionOnMesh([pos, mesh]: [{ x: number; y: number; z: number }, Mesh]) {
  mesh.position.copy(pos);
}

function copyPositionsToMeshes(world: World) {
  world.query(Position, MeshRef, Not(Hidden)).updateEach(setPositionOnMesh);
}

function setColourProperties(
  mesh: Mesh,
  colorHex: string,
  emissiveHex: string,
  emissiveIntensity: number
) {
  const material = mesh.material as MeshStandardMaterial;
  material.color.set(colorHex);
  material.emissive.set(emissiveHex);
  material.emissiveIntensity = emissiveIntensity;
}

function setSelectedColour([mesh]: [Mesh]) {
  setColourProperties(mesh, selectedNodeColour, selectedNodeColour, selectedEmissiveIntensity);
}

function hslString(hue: number, saturationPercent: number, lightnessPercent: number) {
  return `hsl(${hue}, ${saturationPercent}%, ${lightnessPercent}%)`;
}

// djb2 string hash. Deterministic, dependency-free, and good enough to spread Wilp
// names across a small lightness window.
function hashString(str: string): number {
  let hash = 5381;
  for (let i = 0; i < str.length; i++) {
    hash = ((hash << 5) + hash + str.charCodeAt(i)) | 0;
  }
  // Convert to unsigned for predictable downstream arithmetic.
  return hash >>> 0;
}

function colourForWilp(wilp: Wilp): string {
  const baseHue = pdeekBaseHue[wilp.Pdeek];
  const hash = hashString(wilp.Name);
  // Map the hash into a lightness offset in [-pdeekLightnessRangePercent, +pdeekLightnessRangePercent].
  const offset =
    ((hash % 1000) / 999) * 2 * pdeekLightnessRangePercent - pdeekLightnessRangePercent;
  return hslString(baseHue, pdeekSaturationPercent, pdeekLightnessMidPercent + offset);
}

function setWilpColour([mesh, person]: [Mesh, Person]) {
  const colour = defaultArg(mapOption(colourForWilp, person.Wilp), unaffiliatedColour);
  setColourProperties(mesh, colour, defaultEmissiveColour, defaultEmissiveIntensity);
}

function paintTreeNodes(world: World) {
  world.query(MeshRef, Selected, Not(Hidden)).updateEach(setSelectedColour);
  world.query(MeshRef, PersonRef, Not(Selected, Hidden)).updateEach(setWilpColour);
}

function copyLinePropertiesToMeshes(world: World) {
  function setLineMeshProperties([mesh]: [Mesh], entity: Entity) {
    const [from, to] = getLinePositions(world, entity);
    const direction = to.clone().sub(from);
    const length = direction.length();
    const midpoint = from.clone().add(direction.clone().multiplyScalar(0.5));
    const orientation = new Quaternion().setFromUnitVectors(
      new Vector3(0, 1, 0), // cylinder's up axis
      direction.clone().normalize()
    );

    mesh.position.copy(midpoint);
    mesh.quaternion.copy(orientation);
    mesh.geometry.dispose();
    mesh.geometry = new CylinderGeometry(
      lineCylinderRadius,
      lineCylinderRadius,
      length,
      lineCylinderRadialSegments
    );
  }

  world.query(Line, MeshRef, Not(Hidden)).select(MeshRef).updateEach(setLineMeshProperties);
}

export function render(world: IWorld): IWorld {
  const kootaWorld = toKootaWorld(world);
  copyPositionsToMeshes(kootaWorld);
  copyLinePropertiesToMeshes(kootaWorld);
  paintTreeNodes(kootaWorld);
  return world;
}
