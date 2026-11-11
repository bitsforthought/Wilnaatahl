import { createWorld, type World } from "koota";
import { PerspectiveCamera } from "three";
import { afterEach, describe, expect, test } from "vitest";
import { CameraRef, WorldCanvasDimensions } from "../../../src/ecs/traits";
import { syncRenderingContext } from "../../../src/ecs/renderingContext";

describe("rendering context", () => {
  let world: World | undefined;

  afterEach(() => {
    world?.destroy();
    world = undefined;
  });

  test("stores one camera reference and reuses its singleton entity", () => {
    world = createWorld();
    const firstCamera = new PerspectiveCamera();
    const secondCamera = new PerspectiveCamera();

    syncRenderingContext(world, firstCamera, 800, 600);
    const firstEntity = world.query(CameraRef)[0];

    syncRenderingContext(world, secondCamera, 800, 600);
    const cameraEntities = world.query(CameraRef);

    expect(cameraEntities).toHaveLength(1);
    expect(cameraEntities[0]).toBe(firstEntity);
    expect(firstEntity.get(CameraRef)).toBe(secondCamera);
  });

  test("does not write unchanged camera or canvas dimensions", () => {
    world = createWorld();
    const camera = new PerspectiveCamera();
    syncRenderingContext(world, camera, 800, 600);

    let cameraChanges = 0;
    let dimensionChanges = 0;
    const unsubscribeCamera = world.onChange(CameraRef, () => {
      cameraChanges += 1;
    });
    const unsubscribeDimensions = world.onChange(WorldCanvasDimensions, () => {
      dimensionChanges += 1;
    });

    syncRenderingContext(world, camera, 800, 600);

    expect(cameraChanges).toBe(0);
    expect(dimensionChanges).toBe(0);

    unsubscribeCamera();
    unsubscribeDimensions();
  });

  test("reads canvas dimensions as detached value copies", () => {
    world = createWorld();
    syncRenderingContext(world, new PerspectiveCamera(), 800, 600);

    const firstRead = world.get(WorldCanvasDimensions)!;
    const secondRead = world.get(WorldCanvasDimensions)!;

    expect(secondRead).not.toBe(firstRead);

    Object.assign(firstRead, { Width: 1 });

    expect(world.get(WorldCanvasDimensions)).toMatchObject({ Width: 800, Height: 600 });
  });

  test("updates changed camera identity and dimensions", () => {
    world = createWorld();
    const firstCamera = new PerspectiveCamera();
    const secondCamera = new PerspectiveCamera();
    syncRenderingContext(world, firstCamera, 800, 600);

    let cameraChanges = 0;
    let dimensionChanges = 0;
    const unsubscribeCamera = world.onChange(CameraRef, () => {
      cameraChanges += 1;
    });
    const unsubscribeDimensions = world.onChange(WorldCanvasDimensions, () => {
      dimensionChanges += 1;
    });

    syncRenderingContext(world, secondCamera, 1024, 600);

    expect(cameraChanges).toBe(1);
    expect(dimensionChanges).toBe(1);
    expect(world.get(WorldCanvasDimensions)).toMatchObject({ Width: 1024, Height: 600 });

    syncRenderingContext(world, secondCamera, 1024, 700);

    expect(cameraChanges).toBe(1);
    expect(dimensionChanges).toBe(2);
    expect(world.get(WorldCanvasDimensions)).toMatchObject({ Width: 1024, Height: 700 });

    unsubscribeCamera();
    unsubscribeDimensions();
  });
});
