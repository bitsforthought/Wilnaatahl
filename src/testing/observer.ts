import { Not, type Entity, type World } from "koota";
import { Camera, Quaternion, Vector3 } from "three";
import { isViewing } from "../generated/Traits/ViewTraits";
import {
  CurrentMode,
  DragInFlight,
  Hidden,
  MeshRef,
  NodeLabel,
  PersonRef,
  Position,
  Selected,
} from "../ecs/traits";
import { projectNodeBounds, readViewSpaceDepth } from "../react-components/anchor";

export interface CssPoint {
  x: number;
  y: number;
}

export interface ObserverSnapshot {
  session: number;
  frame: number;
  mode: "view" | "move" | "unknown";
  dragging: boolean;
  selectedPersonIds: number[];
  nodes: Array<{
    personId: number;
    logicalPosition: { x: number; y: number; z: number };
    renderedPosition: { x: number; y: number; z: number };
    renderedQuaternion: { x: number; y: number; z: number; w: number };
    renderedScale: { x: number; y: number; z: number };
    viewSpaceDepth: number;
    cssPosition: CssPoint;
    projectedBounds: {
      left: number;
      right: number;
      top: number;
      bottom: number;
    } | null;
    labelGeometry: {
      left: number;
      top: number;
      width: number;
      height: number;
    } | null;
    labelState: "visible" | "occluded" | "unmeasured" | "unmounted" | "unlabeled";
  }>;
  camera: {
    position: { x: number; y: number; z: number };
    target: { x: number; y: number; z: number };
  };
  canvas: { left: number; top: number; width: number; height: number };
}

export interface ObserverSession {
  snapshot(): ObserverSnapshot;
  advanceFrame(): void;
  dispose(): void;
}

export interface ReadOnlyObserver {
  snapshot(): ObserverSnapshot;
}

export interface ObserverSessionOptions {
  session: number;
  readSnapshot: (frame: number, session: number) => ObserverSnapshot;
}

export interface E2EObserverOptions {
  world: World;
  camera: Camera;
  readTarget: () => Vector3;
  readCanvas: () => CanvasRect;
}

export interface CanvasRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

export function projectWorldToCss(
  worldPosition: Vector3,
  camera: Camera,
  canvas: CanvasRect
): CssPoint {
  const projected = worldPosition.clone().project(camera);
  // Projection yields normalized device coordinates; scale to CSS pixels, flip y for screen
  // coordinates, and offset by the canvas position.
  return {
    x: canvas.left + ((projected.x + 1) / 2) * canvas.width,
    y: canvas.top + ((1 - projected.y) / 2) * canvas.height,
  };
}

export function createObserverSession(options: ObserverSessionOptions): ObserverSession {
  let frame = 0;
  let disposed = false;

  function assertActive() {
    if (disposed) {
      throw new Error(`E2E observer session ${options.session} is stale.`);
    }
  }

  return {
    snapshot() {
      assertActive();
      return structuredClone(options.readSnapshot(frame, options.session));
    },
    advanceFrame() {
      assertActive();
      frame += 1;
    },
    dispose() {
      disposed = true;
    },
  };
}

let nextSession = 1;

function copyVector(vector: { x: number; y: number; z: number }) {
  return { x: vector.x, y: vector.y, z: vector.z };
}

function readPersonId(entity: Entity): number | undefined {
  return entity.get(PersonRef)?.Id;
}

function hasLabelContent(entity: Entity): boolean {
  const label = entity.get(NodeLabel);
  return label !== undefined && Object.values(label).some((value) => value != null);
}

function readNode(
  entity: Entity,
  camera: Camera,
  canvas: CanvasRect
): ObserverSnapshot["nodes"][number] | undefined {
  const personId = readPersonId(entity);
  const logicalPosition = entity.get(Position);
  const mesh = entity.get(MeshRef);
  if (personId === undefined || !logicalPosition || !mesh) return undefined;

  const updateParents = true;
  const updateChildren = false;
  mesh.updateWorldMatrix(updateParents, updateChildren);
  const renderedPosition = mesh.getWorldPosition(new Vector3());
  const renderedQuaternion = mesh.getWorldQuaternion(new Quaternion());
  const renderedScale = mesh.getWorldScale(new Vector3());
  const rawProjectedBounds = projectNodeBounds(mesh, camera, canvas.width, canvas.height);
  const projectedBounds = Object.values(rawProjectedBounds).every(Number.isFinite)
    ? {
        left: rawProjectedBounds.Left,
        right: rawProjectedBounds.Right,
        top: rawProjectedBounds.Top,
        bottom: rawProjectedBounds.Bottom,
      }
    : null;
  const labelElement =
    typeof document === "undefined"
      ? undefined
      : document.querySelector<HTMLElement>(`.node-label[data-node-entity-id="${entity.id()}"]`);
  const labelRect = labelElement?.getBoundingClientRect();
  const labelState = !labelElement
    ? hasLabelContent(entity)
      ? "unmounted"
      : "unlabeled"
    : !labelRect ||
        labelRect.width <= 0 ||
        labelRect.height <= 0 ||
        labelElement.dataset?.labelMeasured === "false"
      ? "unmeasured"
      : typeof getComputedStyle === "function" &&
          getComputedStyle(labelElement).visibility === "hidden"
        ? "occluded"
        : "visible";

  return {
    personId,
    logicalPosition: copyVector(logicalPosition),
    renderedPosition: copyVector(renderedPosition),
    renderedQuaternion: {
      x: renderedQuaternion.x,
      y: renderedQuaternion.y,
      z: renderedQuaternion.z,
      w: renderedQuaternion.w,
    },
    renderedScale: copyVector(renderedScale),
    viewSpaceDepth: readViewSpaceDepth(mesh, camera),
    cssPosition: projectWorldToCss(renderedPosition, camera, canvas),
    projectedBounds,
    labelGeometry: labelRect
      ? {
          left: labelRect.left,
          top: labelRect.top,
          width: labelRect.width,
          height: labelRect.height,
        }
      : null,
    labelState,
  };
}

function readObserverSnapshot(
  world: World,
  camera: Camera,
  readTarget: () => Vector3,
  readCanvas: () => CanvasRect,
  frame: number,
  session: number
): ObserverSnapshot {
  const canvas = readCanvas();
  camera.updateMatrixWorld();
  const nodes = Array.from(world.query(PersonRef, Not(Hidden)))
    .map((entity) => readNode(entity, camera, canvas))
    .filter((node): node is ObserverSnapshot["nodes"][number] => node !== undefined);
  const mode = world.get(CurrentMode);
  const target = readTarget();

  return {
    session,
    frame,
    mode: mode === undefined ? "unknown" : isViewing(mode) ? "view" : "move",
    dragging: world.has(DragInFlight),
    selectedPersonIds: Array.from(world.query(Selected, PersonRef))
      .map(readPersonId)
      .filter((personId): personId is number => personId !== undefined),
    nodes,
    camera: {
      position: copyVector(camera.position),
      target: copyVector(target),
    },
    canvas,
  };
}

export function registerE2EObserver(options: E2EObserverOptions): ObserverSession {
  const session = createObserverSession({
    session: nextSession++,
    readSnapshot: (frame, session) =>
      readObserverSnapshot(
        options.world,
        options.camera,
        options.readTarget,
        options.readCanvas,
        frame,
        session
      ),
  });
  const publicObserver: ReadOnlyObserver = {
    snapshot: () => session.snapshot(),
  };
  globalThis.__WILNAATAHL_E2E_OBSERVER__ = publicObserver;

  return {
    snapshot: () => session.snapshot(),
    advanceFrame: () => session.advanceFrame(),
    dispose: () => {
      session.dispose();
      if (globalThis.__WILNAATAHL_E2E_OBSERVER__ === publicObserver) {
        delete globalThis.__WILNAATAHL_E2E_OBSERVER__;
      }
    },
  };
}

declare global {
  var __WILNAATAHL_E2E_OBSERVER__: ReadOnlyObserver | undefined;
}
