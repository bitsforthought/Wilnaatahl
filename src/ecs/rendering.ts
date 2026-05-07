// Three.js orchestration layer for the ECS rendering pass.
//
// All renderer-agnostic colour logic lives in src/Wilnaatahl.Core/ViewModel/Palette.fs.
// This file is responsible for turning what Palette decides into Three.js mesh state
// and for the line-mesh geometry; it does no colour math of its own.

import { CylinderGeometry, Mesh, MeshStandardMaterial, Quaternion, Vector3 } from "three";
import { Entity, Not, World } from "koota";
import { IWorld, toKootaWorld } from "./koota/kootaWrapper";
import { Hidden, Line, MeshRef, PersonRef, Position, Selected } from "./traits";
import { getLinePositions } from "./connectors";
import type { Person } from "../generated/Model";
import { nodePaint, SrgbColour } from "../generated/ViewModel/Palette";

const lineCylinderRadius = 0.03;
const lineCylinderRadialSegments = 8;

// Many of these functions will be called for every rendered tree node, so they will be
// faster as standalone functions.
function setPositionOnMesh([pos, mesh]: [{ x: number; y: number; z: number }, Mesh]) {
  mesh.position.copy(pos);
}

function copyPositionsToMeshes(world: World) {
  world.query(Position, MeshRef, Not(Hidden)).updateEach(setPositionOnMesh);
}

// Three.js's `Color.set(string)` accepts any CSS colour string. We format the
// SrgbColour record produced by the F# palette as `#rrggbb` here — the only
// string-formatting concern in this file.
function srgbToHex({ Red, Green, Blue }: SrgbColour): string {
  const hex = (n: number) => n.toString(16).padStart(2, "0");
  return `#${hex(Red)}${hex(Green)}${hex(Blue)}`;
}

function applyPaintToMesh(mesh: Mesh, person: Person, isSelected: boolean) {
  const paint = nodePaint(person, isSelected);
  const material = mesh.material as MeshStandardMaterial;
  material.color.set(srgbToHex(paint.Colour));
  material.emissive.set(srgbToHex(paint.Emissive));
  material.emissiveIntensity = paint.EmissiveIntensity;
}

// The selected/unselected updateEach callbacks below are deliberately top-level
// named functions rather than inline lambdas, so they are allocated once instead
// of per call to paintTreeNodes.
function paintSelectedNode([mesh, person]: [Mesh, Person]) {
  applyPaintToMesh(mesh, person, true);
}

function paintUnselectedNode([mesh, person]: [Mesh, Person]) {
  applyPaintToMesh(mesh, person, false);
}

function paintTreeNodes(world: World) {
  world.query(MeshRef, PersonRef, Selected, Not(Hidden)).updateEach(paintSelectedNode);
  world.query(MeshRef, PersonRef, Not(Selected, Hidden)).updateEach(paintUnselectedNode);
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
