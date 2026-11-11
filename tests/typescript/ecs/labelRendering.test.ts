// @vitest-environment jsdom

import { createWorld, type Entity, type World } from "koota";
import {
  BoxGeometry,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  PerspectiveCamera,
} from "three";
import { afterEach, describe, expect, test } from "vitest";
import { Hidden, LabelRef, MeshRef, PersonRef, Position } from "../../../src/ecs/traits";
import { renderLabels } from "../../../src/ecs/labelRendering";
import { syncRenderingContext } from "../../../src/ecs/renderingContext";
import { fromKootaWorld } from "../../../src/ecs/koota/kootaWrapper";
import { Person_get_Empty } from "../../../src/generated/Model";
import { runSystems as runGeneratedSystems } from "../../../src/generated/Systems/Runner";
import { spawnControls } from "../../../src/generated/EntityLifeCycle";

const canvasWidth = 800;
const canvasHeight = 600;
const verticalFieldOfViewDegrees = 55;
const aspectRatio = 1;
const rightAngleVerticalFieldOfViewDegrees = 90;
const rightAngleAspectRatio = canvasWidth / canvasHeight;
const cameraNear = 0.1;
const cameraFar = 100;
const nodeSideLength = 2;
// This distance keeps the entire origin-centred cube in front of the camera
// and between its clipping planes; it is a fixture choice, not a product constraint.
const perspectiveCameraDistance = 5;
const doubledCameraZoom = 2;

function createPerspectiveCamera(): PerspectiveCamera {
  const camera = new PerspectiveCamera(
    rightAngleVerticalFieldOfViewDegrees,
    rightAngleAspectRatio,
    cameraNear,
    cameraFar
  );
  camera.position.z = perspectiveCameraDistance;
  camera.updateProjectionMatrix();
  camera.updateMatrixWorld();
  return camera;
}

function createMesh(): Mesh {
  return new Mesh(
    new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
    new MeshBasicMaterial()
  );
}

function createLabel(width: number, height: number): HTMLElement {
  const label = document.createElement("div");
  Object.defineProperty(label, "offsetWidth", { configurable: true, value: width });
  Object.defineProperty(label, "offsetHeight", { configurable: true, value: height });
  label.style.visibility = "hidden";
  return label;
}

function readLabelTransform(label: HTMLElement): { x: number; y: number; scale: number } {
  const match = /^translate3d\(([^,]+)px, ([^,]+)px, 0px\).*scale\(([^)]+)\)$/.exec(
    label.style.transform
  );
  if (!match) throw new Error(`Unexpected label transform: ${label.style.transform}`);
  return { x: Number(match[1]), y: Number(match[2]), scale: Number(match[3]) };
}

function spawnNode(world: World, mesh: Mesh, label?: HTMLElement): Entity {
  const entity = world.spawn(PersonRef(Person_get_Empty()), MeshRef(mesh));
  if (label) entity.add(LabelRef(label));
  return entity;
}

function renderLabelFrame(
  world: World,
  camera: PerspectiveCamera,
  width: number,
  height: number
): void {
  syncRenderingContext(world, camera, width, height);
  renderLabels(fromKootaWorld(world));
}

describe("DOM label rendering", () => {
  let world: World | undefined;

  afterEach(() => {
    world?.destroy();
    world = undefined;
  });

  test("applies the generated placement after reading the mounted label size", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    spawnNode(world, createMesh(), label);

    renderLabelFrame(world, createPerspectiveCamera(), canvasWidth, canvasHeight);

    const transform = readLabelTransform(label);
    expect(transform.x).toBeCloseTo(400);
    expect(transform.y).toBeCloseTo(383);
    expect(transform.scale).toBeCloseTo(0.75);
    expect(label.style.transform).toContain("translateX(-50%)");
    expect(label.style.visibility).toBe("visible");
  });

  test("fails clearly when projected nodes have no rendering context", () => {
    const currentWorld = createWorld();
    world = currentWorld;
    spawnNode(currentWorld, createMesh(), createLabel(40, 20));

    expect(() => renderLabels(fromKootaWorld(currentWorld))).toThrowError(
      new Error(
        "Cannot render labels without a camera reference and canvas dimensions in the rendering context."
      )
    );
  });

  test.each([
    { name: "zero-width", width: 0, height: 20 },
    { name: "zero-height", width: 40, height: 0 },
  ])("keeps a mounted label hidden for a $name label", ({ width, height }) => {
    world = createWorld();
    const label = createLabel(width, height);
    spawnNode(world, createMesh(), label);

    renderLabelFrame(world, createPerspectiveCamera(), canvasWidth, canvasHeight);

    expect(label.style.visibility).toBe("hidden");
  });

  test("lets an unlabelled nearer node occlude a measured target label", () => {
    world = createWorld();
    const targetLabel = createLabel(40, 20);
    const target = createMesh();
    const nearer = createMesh();
    nearer.position.z = 2;
    spawnNode(world, target, targetLabel);
    spawnNode(world, nearer);

    renderLabelFrame(world, createPerspectiveCamera(), canvasWidth, canvasHeight);

    expect(targetLabel.style.visibility).toBe("hidden");
  });

  test("rendering labels in one world does not affect another", () => {
    const firstWorld = createWorld();
    const secondWorld = createWorld();
    try {
      const firstLabel = createLabel(40, 20);
      const secondLabel = createLabel(40, 20);
      spawnNode(firstWorld, createMesh(), firstLabel);
      spawnNode(secondWorld, createMesh(), secondLabel);

      renderLabelFrame(firstWorld, createPerspectiveCamera(), canvasWidth, canvasHeight);

      expect(firstLabel.style.visibility).toBe("visible");
      expect(secondLabel.style.visibility).toBe("hidden");
    } finally {
      firstWorld.destroy();
      secondWorld.destroy();
    }
  });

  test("hides a mounted label when its entity becomes hidden", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    const entity = spawnNode(world, createMesh(), label);
    const camera = createPerspectiveCamera();

    renderLabelFrame(world, camera, canvasWidth, canvasHeight);
    expect(label.style.visibility).toBe("visible");

    entity.add(Hidden);
    renderLabelFrame(world, camera, canvasWidth, canvasHeight);

    expect(label.style.visibility).toBe("hidden");
  });

  test("hides a mounted label when its person reference is removed", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    const entity = spawnNode(world, createMesh(), label);
    const camera = createPerspectiveCamera();

    renderLabelFrame(world, camera, canvasWidth, canvasHeight);
    entity.remove(PersonRef);
    renderLabelFrame(world, camera, canvasWidth, canvasHeight);

    expect(label.style.visibility).toBe("hidden");
  });

  test("hides a mounted label when its mesh reference is removed", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    const entity = spawnNode(world, createMesh(), label);
    const camera = createPerspectiveCamera();

    renderLabelFrame(world, camera, canvasWidth, canvasHeight);
    entity.remove(MeshRef);
    renderLabelFrame(world, camera, canvasWidth, canvasHeight);

    expect(label.style.visibility).toBe("hidden");
  });

  test("uses the oblique perspective projection for the DOM transform", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    mesh.position.x = 1;
    spawnNode(world, mesh, label);
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    camera.position.set(4, 3, 9);
    camera.lookAt(0, 0, 0);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    renderLabelFrame(world, camera, canvasWidth, canvasHeight);

    const transform = readLabelTransform(label);
    expect(transform.x).toBeCloseTo(467.869, 2);
    expect(transform.y).toBeCloseTo(402.131, 2);
    expect(transform.scale).toBeCloseTo(0.7271221135, 8);
  });

  test("reprojects and rescales a label across perspective zoom frames", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    spawnNode(world, mesh, label);
    const camera = createPerspectiveCamera();

    renderLabelFrame(world, camera, canvasWidth, canvasHeight);

    const firstFrameTransform = readLabelTransform(label);
    expect(firstFrameTransform.x).toBeCloseTo(400);
    expect(firstFrameTransform.y).toBeCloseTo(383);
    expect(firstFrameTransform.scale).toBeCloseTo(0.75);

    camera.zoom = doubledCameraZoom;
    camera.updateProjectionMatrix();
    renderLabelFrame(world, camera, canvasWidth, canvasHeight);

    const zoomedTransform = readLabelTransform(label);
    expect(zoomedTransform.x).toBeCloseTo(400);
    expect(zoomedTransform.y).toBeCloseTo(458);
    expect(zoomedTransform.scale).toBeCloseTo(1.5);
  });

  test("generated Runner renders labels after mesh positions are synchronized", () => {
    world = createWorld();
    const label = createLabel(40, 20);
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshStandardMaterial()
    );
    const node = world.spawn(
      PersonRef(Person_get_Empty()),
      MeshRef(mesh),
      Position({ x: 0, y: 0, z: 0 }),
      LabelRef(label)
    );
    const camera = createPerspectiveCamera();
    syncRenderingContext(world, camera, canvasWidth, canvasHeight);
    spawnControls(fromKootaWorld(world));

    runGeneratedSystems(fromKootaWorld(world), 0);
    const initialTransform = readLabelTransform(label);

    node.set(Position, { x: 2, y: 0, z: 0 });
    runGeneratedSystems(fromKootaWorld(world), 0);
    const movedTransform = readLabelTransform(label);

    expect(movedTransform.x).toBeGreaterThan(initialTransform.x);
    expect(movedTransform.x).not.toBe(initialTransform.x);
  });
});
