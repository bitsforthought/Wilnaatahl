import { Not, type Entity, type World } from "koota";
import type { Camera, Object3D } from "three";
import {
  LabelLayout,
  LabelPlacementModule_layoutLabel,
  LabelSize,
  ProjectedNode,
} from "../generated/ViewModel/LabelPlacement";
import {
  projectNodeBounds,
  readPixelsPerWorldUnit,
  readViewSpaceDepth,
} from "../react-components/anchor";
import { IWorld, toKootaWorld } from "./koota/kootaWrapper";
import {
  CameraRef,
  Hidden,
  LabelRef,
  MeshRef,
  PersonRef,
  WorldCanvasDimensions,
  type CanvasDimensions,
} from "./traits";

type RenderingContext = {
  camera: Camera;
  dimensions: CanvasDimensions;
};

type LabelRenderingFrame = RenderingContext & {
  projectedNodes: ProjectedNode[];
};

function readRenderingContext(world: World): RenderingContext {
  const cameraEntity = world.query(CameraRef)[0];
  const camera = cameraEntity?.get(CameraRef);
  const dimensions = world.get(WorldCanvasDimensions);

  if (!camera || !dimensions) {
    throw new Error(
      "Cannot render labels without a camera reference and canvas dimensions in the rendering context."
    );
  }

  return { camera, dimensions };
}

function readLabelSize(element: HTMLElement | null | undefined): LabelSize | undefined {
  if (!element) return undefined;

  const width = element.offsetWidth;
  const height = element.offsetHeight;
  const hasUsableSize =
    Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0;

  return hasUsableSize ? new LabelSize(width, height) : undefined;
}

function readProjectedNode(
  mesh: Object3D,
  element: HTMLElement | null | undefined,
  camera: Camera,
  dimensions: CanvasDimensions
): ProjectedNode {
  return new ProjectedNode(
    projectNodeBounds(mesh, camera, dimensions.Width, dimensions.Height),
    readViewSpaceDepth(mesh, camera),
    readPixelsPerWorldUnit(mesh, camera, dimensions.Height),
    readLabelSize(element)
  );
}

function applyLabelLayout(element: HTMLElement, layout: LabelLayout): void {
  // Translate by half the label width to centre it on the anchor; the top-centre
  // origin keeps that anchor fixed while the label scales.
  element.style.transform = `translate3d(${layout.Placement.X}px, ${layout.Placement.Y}px, 0px) translateX(-50%) scale(${layout.Placement.Scale})`;
  element.style.visibility = layout.Visible ? "visible" : "hidden";
}

function hideStaleLabel([element]: [HTMLElement | null | undefined], entity: Entity): void {
  if (element && (entity.has(Hidden) || !entity.has(PersonRef) || !entity.has(MeshRef))) {
    element.style.visibility = "hidden";
  }
}

function readProjectedNodeForQuery(
  frame: LabelRenderingFrame,
  [mesh]: [Object3D],
  entity: Entity,
  index: number
): void {
  frame.projectedNodes[index] = readProjectedNode(
    mesh,
    entity.get(LabelRef),
    frame.camera,
    frame.dimensions
  );
}

function layoutLabelForQuery(
  frame: LabelRenderingFrame,
  _state: unknown[],
  entity: Entity,
  index: number
): void {
  const element = entity.get(LabelRef);
  if (!element) return;

  const projectedNode = frame.projectedNodes[index];
  const layout = LabelPlacementModule_layoutLabel(frame.projectedNodes, projectedNode);
  applyLabelLayout(element, layout);
}

export function renderLabels(world: IWorld): IWorld {
  const kootaWorld = toKootaWorld(world);
  const query = kootaWorld.query(PersonRef, MeshRef, Not(Hidden));
  if (query.length === 0) {
    kootaWorld.query(LabelRef).readEach(hideStaleLabel);
    return world;
  }

  const frame: LabelRenderingFrame = {
    ...readRenderingContext(kootaWorld),
    projectedNodes: [],
  };
  query.select(MeshRef).readEach(readProjectedNodeForQuery.bind(undefined, frame));
  query.readEach(layoutLabelForQuery.bind(undefined, frame));
  kootaWorld.query(LabelRef).readEach(hideStaleLabel);
  return world;
}
