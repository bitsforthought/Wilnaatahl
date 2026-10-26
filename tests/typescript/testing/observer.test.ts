import { createWorld } from "koota";
import { Group, Mesh, PerspectiveCamera, Vector3 } from "three";
import { describe, expect, test } from "vitest";
import {
  createObserverSession,
  projectWorldToCss,
  registerE2EObserver,
  type ObserverSnapshot,
} from "../../../src/testing/observer";
import {
  AppMode_Moving,
  AppMode_Viewing,
  type AppMode_$union,
} from "../../../src/generated/Traits/ViewTraits";
import { Person_get_Empty } from "../../../src/generated/Model";
import {
  CurrentMode,
  DragInFlight,
  Hidden,
  MeshRef,
  PersonRef,
  Position,
  Selected,
} from "../../../src/ecs/traits";

function snapshotFor(frame: number, session: number): ObserverSnapshot {
  return {
    session,
    frame,
    mode: "view",
    dragging: false,
    selectedPersonIds: [7],
    nodes: [
      {
        personId: 7,
        logicalPosition: { x: 1, y: 2, z: 3 },
        renderedPosition: { x: 1, y: 2, z: 3 },
        renderedQuaternion: { x: 0, y: 0, z: 0, w: 1 },
        renderedScale: { x: 1, y: 1, z: 1 },
        cssPosition: { x: 100, y: 80 },
      },
    ],
    camera: {
      position: { x: 0, y: 0, z: 8 },
      target: { x: 0, y: 0, z: 0 },
    },
    canvas: { left: 10, top: 20, width: 400, height: 300 },
  };
}

describe("E2E observer", () => {
  test("projects world coordinates into CSS pixels using the canvas offset", () => {
    const fov = 50;
    const aspect = 4 / 3;
    const near = 0.1;
    const far = 100;
    const camera = new PerspectiveCamera(fov, aspect, near, far);
    camera.position.set(0, 0, 8);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    expect(
      projectWorldToCss(new Vector3(0, 0, 0), camera, {
        left: 10,
        top: 20,
        width: 400,
        height: 300,
      })
    ).toEqual({ x: 210, y: 170 });

    const offAxis = projectWorldToCss(new Vector3(1, 1, 0), camera, {
      left: 10,
      top: 20,
      width: 400,
      height: 300,
    });
    expect(offAxis.x).toBeGreaterThan(210);
    expect(offAxis.y).toBeLessThan(170);
  });

  test("returns detached serializable snapshots and advances frames", () => {
    const sourceSnapshot = snapshotFor(0, 11);
    const session = createObserverSession({
      session: 11,
      readSnapshot: (frame) => {
        sourceSnapshot.frame = frame;
        return sourceSnapshot;
      },
    });

    const first = session.snapshot();
    first.selectedPersonIds.push(99);
    first.nodes[0].logicalPosition.x = 999;
    session.advanceFrame();
    const second = session.snapshot();

    expect(second).toEqual(snapshotFor(1, 11));
    expect(JSON.parse(JSON.stringify(second))).toEqual(second);
  });

  test("rejects snapshots after disposal and does not accept a stale session", () => {
    const session = createObserverSession({
      session: 12,
      readSnapshot: (frame, sessionId) => snapshotFor(frame, sessionId),
    });

    session.dispose();

    expect(() => session.snapshot()).toThrowError("E2E observer session 12 is stale.");
    expect(() => session.advanceFrame()).toThrowError("E2E observer session 12 is stale.");
  });

  test("registers only serializable current-world data and removes the public observer on dispose", () => {
    const world = createWorld();
    const fov = 50;
    const aspect = 1;
    const near = 0.1;
    const far = 100;
    const camera = new PerspectiveCamera(fov, aspect, near, far);
    camera.position.set(0, 0, 8);
    world.add(CurrentMode);
    world.set(CurrentMode, AppMode_Viewing() as AppMode_$union);
    const mesh = new Mesh();
    world.spawn(
      PersonRef(Person_get_Empty()),
      Position({ x: 1, y: 2, z: 3 }),
      MeshRef(mesh),
      Selected
    );

    const session = registerE2EObserver({
      world,
      camera,
      readTarget: () => new Vector3(1, 2, 3),
      readCanvas: () => ({ left: 4, top: 5, width: 200, height: 100 }),
    });

    const snapshot = session.snapshot();
    expect(snapshot.selectedPersonIds).toEqual([0]);
    expect(snapshot.nodes[0]?.logicalPosition).toEqual({ x: 1, y: 2, z: 3 });
    expect(snapshot.nodes[0]?.renderedQuaternion).toEqual({ x: 0, y: 0, z: 0, w: 1 });
    expect(snapshot.nodes[0]?.cssPosition.x).toBeGreaterThan(4);
    expect(snapshot.nodes[0]?.cssPosition.y).toBeGreaterThan(5);
    expect(snapshot.camera.target).toEqual({ x: 1, y: 2, z: 3 });
    expect(snapshot.canvas).toEqual({ left: 4, top: 5, width: 200, height: 100 });
    expect(JSON.parse(JSON.stringify(snapshot))).toEqual(snapshot);
    expect(globalThis.__WILNAATAHL_E2E_OBSERVER__?.snapshot()).toEqual(snapshot);

    session.advanceFrame();
    expect(session.snapshot().frame).toBe(1);
    const replacement = registerE2EObserver({
      world,
      camera,
      readTarget: () => new Vector3(),
      readCanvas: () => ({ left: 4, top: 5, width: 200, height: 100 }),
    });
    session.dispose();
    expect(globalThis.__WILNAATAHL_E2E_OBSERVER__).toBeDefined();
    replacement.dispose();
    expect(globalThis.__WILNAATAHL_E2E_OBSERVER__).toBeUndefined();
    world.destroy();
  });

  test("omits people whose render data is incomplete", () => {
    const world = createWorld();
    world.spawn(PersonRef(Person_get_Empty()));
    const fov = 50;
    const aspect = 1;
    const near = 0.1;
    const far = 100;
    const camera = new PerspectiveCamera(fov, aspect, near, far);
    const session = registerE2EObserver({
      world,
      camera,
      readTarget: () => new Vector3(),
      readCanvas: () => ({ left: 0, top: 0, width: 100, height: 100 }),
    });

    expect(session.snapshot().nodes).toEqual([]);

    session.dispose();
    world.destroy();
  });

  test("reads mode, dragging, hidden filtering, and world-space render transforms", () => {
    const world = createWorld();
    world.add(CurrentMode);
    world.set(CurrentMode, AppMode_Moving() as AppMode_$union);
    world.add(DragInFlight);

    const parent = new Group();
    parent.position.set(1, 2, 3);
    const mesh = new Mesh();
    mesh.position.set(2, 0, 0);
    mesh.scale.set(2, 2, 2);
    parent.add(mesh);

    world.spawn(PersonRef(Person_get_Empty()), Position({ x: 9, y: 8, z: 7 }), MeshRef(mesh));
    world.spawn(
      PersonRef(Person_get_Empty()),
      Position({ x: 0, y: 0, z: 0 }),
      MeshRef(new Mesh()),
      Hidden
    );

    const fov = 50;
    const aspect = 1;
    const near = 0.1;
    const far = 100;
    const session = registerE2EObserver({
      world,
      camera: new PerspectiveCamera(fov, aspect, near, far),
      readTarget: () => new Vector3(),
      readCanvas: () => ({ left: 0, top: 0, width: 100, height: 100 }),
    });

    const snapshot = session.snapshot();
    expect(snapshot.mode).toBe("move");
    expect(snapshot.dragging).toBe(true);
    expect(snapshot.nodes).toHaveLength(1);
    expect(snapshot.nodes[0]?.renderedPosition).toEqual({ x: 3, y: 2, z: 3 });
    expect(snapshot.nodes[0]?.renderedScale).toEqual({ x: 2, y: 2, z: 2 });

    session.dispose();
    world.destroy();
  });

  test("reports unknown mode when no current mode exists", () => {
    const world = createWorld();
    const fov = 50;
    const aspect = 1;
    const near = 0.1;
    const far = 100;
    const session = registerE2EObserver({
      world,
      camera: new PerspectiveCamera(fov, aspect, near, far),
      readTarget: () => new Vector3(),
      readCanvas: () => ({ left: 0, top: 0, width: 100, height: 100 }),
    });

    expect(session.snapshot().mode).toBe("unknown");

    session.dispose();
    world.destroy();
  });
});
