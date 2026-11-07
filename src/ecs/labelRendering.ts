import { Not, type Entity, type World } from "koota";
import type { Camera, Object3D } from "three";
import {
  LabelPlacementModule_layoutLabels,
  LabelRenderInput,
  LabelRenderOutput,
  LabelSize,
} from "../generated/ViewModel/LabelPlacement";
import {
  projectNodeBounds,
  readPixelsPerWorldUnit,
  readViewSpaceDepth,
} from "../react-components/anchor";
import { Hidden, LabelRef, MeshRef, PersonRef } from "./traits";

export type LabelRenderingInput = {
  world: World;
  camera: Camera;
  canvasWidth: number;
  canvasHeight: number;
};

type ReadLabelNode = {
  id: number;
  mesh: Object3D;
  element: HTMLElement | undefined;
  width: number | undefined;
  height: number | undefined;
};

function readLabelNode(entity: Entity): ReadLabelNode {
  const mesh = entity.get(MeshRef)!;

  const element = entity.get(LabelRef) ?? undefined;
  const width = element?.offsetWidth;
  const height = element?.offsetHeight;
  const hasUsableSize =
    width !== undefined &&
    height !== undefined &&
    Number.isFinite(width) &&
    Number.isFinite(height) &&
    width > 0 &&
    height > 0;

  return {
    id: entity.id(),
    mesh,
    element,
    width: hasUsableSize ? width : undefined,
    height: hasUsableSize ? height : undefined,
  };
}

function createRenderInput(
  node: ReadLabelNode,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
): LabelRenderInput {
  const bounds = projectNodeBounds(node.mesh, camera, canvasWidth, canvasHeight);
  const pixelsPerWorldUnit = readPixelsPerWorldUnit(node.mesh, camera, canvasHeight);
  const labelSize =
    node.width === undefined || node.height === undefined
      ? undefined
      : new LabelSize(node.width, node.height);

  return new LabelRenderInput(
    node.id,
    bounds,
    readViewSpaceDepth(node.mesh, camera),
    pixelsPerWorldUnit,
    labelSize
  );
}

function writeLabelOutput(
  output: LabelRenderOutput,
  nodesById: ReadonlyMap<number, ReadLabelNode>
): void {
  const node = nodesById.get(output.Id);
  if (!node?.element) return;
  const { element } = node;

  // Translate by half the label width to centre it on the anchor; the top-centre
  // origin keeps that anchor fixed while the label scales.
  element.style.transform = `translate3d(${output.Placement.X}px, ${output.Placement.Y}px, 0px) translateX(-50%) scale(${output.Placement.Scale})`;
  element.style.visibility = output.Visible ? "visible" : "hidden";
  element.dataset.labelMeasured =
    node.width !== undefined && node.height !== undefined ? "true" : "false";
}

export function runLabelRendering(input: LabelRenderingInput): void {
  const nodes: ReadLabelNode[] = [];
  for (const entity of input.world.query(PersonRef, MeshRef, Not(Hidden))) {
    nodes.push(readLabelNode(entity));
  }

  const nodesById = new Map<number, ReadLabelNode>();
  const renderInputs: LabelRenderInput[] = [];
  for (const node of nodes) {
    nodesById.set(node.id, node);
    renderInputs.push(createRenderInput(node, input.camera, input.canvasWidth, input.canvasHeight));
  }

  const outputs = LabelPlacementModule_layoutLabels(renderInputs);

  for (const output of outputs) {
    writeLabelOutput(output, nodesById);
  }

  const renderedIds = new Set(nodes.map((node) => node.id));
  for (const entity of input.world.query(LabelRef)) {
    const element = entity.get(LabelRef);
    if (element && !renderedIds.has(entity.id())) {
      element.style.visibility = "hidden";
    }
  }
}
