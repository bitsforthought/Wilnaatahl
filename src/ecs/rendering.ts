import { CylinderGeometry, Mesh, MeshStandardMaterial, Quaternion, Vector3 } from "three";
import { Entity, Not, World } from "koota";
import { IWorld, toKootaWorld } from "./koota/kootaWrapper";
import { Hidden, Line, MeshRef, PersonRef, Position, Selected } from "./traits";
import { getLinePositions } from "./connectors";

// MAINTENANCE NOTE: Keep all presentation-specific constants up here for ease of update.
const defaultNodeColour = "#FF0000"; // Red
const defaultEmissiveIntensity = 0;
const defaultEmissiveColour = "#000000"; // No emissive colour for unselected nodes.

const selectedNodeColour = "#8B4000"; // Deep, red copper
const selectedEmissiveIntensity = 0.8;

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

function setDefaultColour([mesh]: [Mesh]) {
  setColourProperties(mesh, defaultNodeColour, defaultEmissiveColour, defaultEmissiveIntensity);
}

function paintTreeNodes(world: World) {
  world.query(MeshRef, Selected, Not(Hidden)).updateEach(setSelectedColour);
  world
    .query(MeshRef, PersonRef, Not(Selected, Hidden))
    .select(MeshRef)
    .updateEach(setDefaultColour);
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
