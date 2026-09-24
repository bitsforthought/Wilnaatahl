import { createWorld, Entity, World } from "koota";
import { CylinderGeometry, Mesh, MeshStandardMaterial, Vector3 } from "three";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { render } from "../../../src/ecs/rendering";
import { fromKootaWorld, IWorld } from "../../../src/ecs/koota/kootaWrapper";
import { Hidden, Line, MeshRef, PersonRef, Position, Selected } from "../../../src/ecs/traits";
import { Person_get_Empty } from "../../../src/generated/Model";
import { getEndpoints, spawn as spawnLine } from "../../../src/generated/Entities/Line";
import { nodePaint, NodePaint, SrgbColour } from "../../../src/generated/ViewModel/Palette";

function createMesh() {
  return new Mesh(new CylinderGeometry(1, 1, 1), new MeshStandardMaterial());
}

function getMaterial(mesh: Mesh) {
  return mesh.material as MeshStandardMaterial;
}

function hexDigits({ Red, Green, Blue }: SrgbColour) {
  return `${Red.toString(16).padStart(2, "0")}${Green.toString(16).padStart(2, "0")}${Blue.toString(16).padStart(2, "0")}`;
}

function materialState(material: MeshStandardMaterial) {
  return {
    color: material.color.getHexString(),
    emissive: material.emissive.getHexString(),
    emissiveIntensity: material.emissiveIntensity,
  };
}

function expectedMaterialState(paint: NodePaint) {
  return {
    color: hexDigits(paint.Colour),
    emissive: hexDigits(paint.Emissive),
    emissiveIntensity: paint.EmissiveIntensity,
  };
}

describe("render", () => {
  let world: World;
  let wrappedWorld: IWorld;

  beforeEach(() => {
    world = createWorld();
    wrappedWorld = fromKootaWorld(world);
  });

  afterEach(() => {
    world.destroy();
  });

  test("copies positions to visible meshes and leaves hidden meshes untouched", () => {
    const visibleMesh = createMesh();
    const hiddenMesh = createMesh();
    world.spawn(MeshRef(visibleMesh), Position({ x: 1, y: 2, z: 3 }));
    world.spawn(MeshRef(hiddenMesh), Position({ x: 4, y: 5, z: 6 }), Hidden);

    render(wrappedWorld);

    expect(visibleMesh.position).toStrictEqual(new Vector3(1, 2, 3));
    expect(hiddenMesh.position).toStrictEqual(new Vector3(0, 0, 0));
  });

  test("paints selected and unselected visible people using their palette instructions", () => {
    const selectedMesh = createMesh();
    const unselectedMesh = createMesh();
    const person = Person_get_Empty();
    world.spawn(MeshRef(selectedMesh), PersonRef(person), Selected);
    world.spawn(MeshRef(unselectedMesh), PersonRef(person));

    render(wrappedWorld);

    const selectedPaint = nodePaint(person, true);
    const unselectedPaint = nodePaint(person, false);
    const selectedMaterial = getMaterial(selectedMesh);
    const unselectedMaterial = getMaterial(unselectedMesh);

    expect(materialState(selectedMaterial)).toStrictEqual(expectedMaterialState(selectedPaint));
    expect(materialState(unselectedMaterial)).toStrictEqual(expectedMaterialState(unselectedPaint));
  });

  test("does not paint hidden entities", () => {
    const hiddenMesh = createMesh();
    world.spawn(MeshRef(hiddenMesh), PersonRef(Person_get_Empty()), Hidden, Selected);

    const hiddenMaterial = getMaterial(hiddenMesh);
    const materialBefore = materialState(hiddenMaterial);
    render(wrappedWorld);

    expect(materialState(hiddenMaterial)).toStrictEqual(materialBefore);
  });

  test("updates visible line geometry, midpoint, and orientation", () => {
    const lineMesh = createMesh();
    const lineId = spawnLine({ x: 1, y: 2, z: 3 }, { x: 4, y: 6, z: 3 }, wrappedWorld);
    const line = lineId as Entity;
    line.add(MeshRef(lineMesh));
    const oldGeometry = lineMesh.geometry;
    const dispose = vi.spyOn(oldGeometry, "dispose");

    render(wrappedWorld);

    expect(dispose).toHaveBeenCalledTimes(1);
    expect(lineMesh.geometry).toBeInstanceOf(CylinderGeometry);
    expect((lineMesh.geometry as CylinderGeometry).parameters.height).toBe(5);
    expect(lineMesh.position).toStrictEqual(new Vector3(2.5, 4, 3));
    // Three.js cylinders extend along their local Y axis. Rendering stores a quaternion that
    // rotates that default axis onto the line between the endpoints, so applying the stored
    // rotation to Y recovers the direction in which the cylinder will appear in the scene.
    const renderedDirection = new Vector3(0, 1, 0).applyQuaternion(lineMesh.quaternion);
    const expectedDirection = new Vector3(3, 4, 0).normalize();
    expect(renderedDirection.distanceTo(expectedDirection)).toBeCloseTo(0);
  });

  test("does not update hidden line geometry", () => {
    const lineMesh = createMesh();
    const lineId = spawnLine({ x: 0, y: 0, z: 0 }, { x: 0, y: 3, z: 0 }, wrappedWorld);
    const line = lineId as Entity;
    line.add(MeshRef(lineMesh));
    line.add(Hidden);
    const oldGeometry = lineMesh.geometry;
    const dispose = vi.spyOn(oldGeometry, "dispose");
    const positionBefore = lineMesh.position.clone();
    const quaternionBefore = lineMesh.quaternion.clone();

    render(wrappedWorld);

    expect(dispose).not.toHaveBeenCalled();
    expect(lineMesh.geometry).toBe(oldGeometry);
    expect(lineMesh.position).toStrictEqual(positionBefore);
    expect(lineMesh.quaternion.toArray()).toEqual(quaternionBefore.toArray());
  });

  test("propagates the generated line endpoint error with its contract message", () => {
    const lineMesh = createMesh();
    const lineId = world.spawn(Line, MeshRef(lineMesh));

    expect(() => render(wrappedWorld)).toThrowError(`Found Line ${lineId} with 0 endpoints.`);
  });

  test("propagates the generated missing-position error with its contract message", () => {
    const lineMesh = createMesh();
    const lineId = spawnLine({ x: 0, y: 0, z: 0 }, { x: 0, y: 3, z: 0 }, wrappedWorld);
    const line = lineId as Entity;
    line.add(MeshRef(lineMesh));
    const [firstEndpoint] = getEndpoints(wrappedWorld, lineId);
    (firstEndpoint as Entity).remove(Position);

    expect(() => render(wrappedWorld)).toThrowError(
      `Found Line ${lineId} with endpoint(s) that have no Position.`
    );
  });

  test("returns the original wrapped world", () => {
    expect(render(wrappedWorld)).toBe(wrappedWorld);
  });

  test("rejects an invalid world implementation", () => {
    expect(() => render({} as IWorld)).toThrowError(
      "Invalid IWorld implementation passed to toKootaWorld."
    );
  });
});
