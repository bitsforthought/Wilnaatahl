import { Box3, Camera, Object3D, Vector3 } from "three";

export type ProjectedNodeBounds = {
  left: number;
  right: number;
  top: number;
  bottom: number;
};

/**
 * The projected on-screen anchor of the selected node, in canvas pixels. It is
 * reprojected only when the selection, camera, or canvas changes, so an animating
 * selected node leaves the anchor behind — see the accepted gaps in
 * `specs/names-and-detail-overlay.md`.
 */
export type OverlayAnchor = {
  /** Screen x of the node's left edge. */
  nodeLeft: number;
  /** Screen x of the node's right edge. */
  nodeRight: number;
  /** Screen y of the node's top edge. */
  nodeTop: number;
  /** Screen y of the node's bottom edge. */
  nodeBottom: number;
  /** Canvas width in pixels. */
  canvasWidth: number;
  /** Canvas height in pixels. */
  canvasHeight: number;
};

// Projection is synchronous, so these scratch values can be reused without retaining results.
const projectedBoundsBox = new Box3();
const projectedBoundsCorners = Array.from({ length: 8 }, () => new Vector3());
const projectedScreenPoint = { x: 0, y: 0 };

/**
 * Converts normalized device coordinates (NDC) to canvas pixels, inverting y
 * because NDC grows upward while screen coordinates grow downward.
 *
 * See https://threejs.org/docs/#api/en/math/Vector3.project.
 */
function ndcToScreen(v: Vector3, width: number, height: number): { x: number; y: number } {
  const NDC_TO_UNIT_SCALE = 0.5;
  const NDC_TO_UNIT_OFFSET = 0.5;

  return {
    x: (v.x * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * width,
    y: (-v.y * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * height,
  };
}

function readProjectedNodeBounds(
  object: Object3D,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
): ProjectedNodeBounds {
  const UPDATE_PARENTS = true;
  const UPDATE_CHILDREN = false;
  object.updateWorldMatrix(UPDATE_PARENTS, UPDATE_CHILDREN);
  camera.updateMatrixWorld();
  projectedBoundsBox.setFromObject(object);

  const min = projectedBoundsBox.min;
  const max = projectedBoundsBox.max;
  projectedBoundsCorners[0].set(min.x, min.y, min.z);
  projectedBoundsCorners[1].set(min.x, min.y, max.z);
  projectedBoundsCorners[2].set(min.x, max.y, min.z);
  projectedBoundsCorners[3].set(min.x, max.y, max.z);
  projectedBoundsCorners[4].set(max.x, min.y, min.z);
  projectedBoundsCorners[5].set(max.x, min.y, max.z);
  projectedBoundsCorners[6].set(max.x, max.y, min.z);
  projectedBoundsCorners[7].set(max.x, max.y, max.z);

  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;

  for (const corner of projectedBoundsCorners) {
    const projected = ndcToScreen(corner.project(camera), canvasWidth, canvasHeight);
    projectedScreenPoint.x = projected.x;
    projectedScreenPoint.y = projected.y;
    minX = Math.min(minX, projectedScreenPoint.x);
    maxX = Math.max(maxX, projectedScreenPoint.x);
    minY = Math.min(minY, projectedScreenPoint.y);
    maxY = Math.max(maxY, projectedScreenPoint.y);
  }

  return { left: minX, right: maxX, top: minY, bottom: maxY };
}

export function projectNodeBounds(
  object: Object3D,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
): ProjectedNodeBounds {
  return readProjectedNodeBounds(object, camera, canvasWidth, canvasHeight);
}

/**
 * Projects every corner of an object's world-space bounding box into canvas
 * pixels. Taking extrema after projection keeps the screen-space bounds correct
 * when the object or camera is rotated.
 */
export function projectAnchor(
  object: Object3D,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
): OverlayAnchor {
  const bounds = readProjectedNodeBounds(object, camera, canvasWidth, canvasHeight);

  return {
    nodeLeft: bounds.left,
    nodeRight: bounds.right,
    nodeTop: bounds.top,
    nodeBottom: bounds.bottom,
    canvasWidth,
    canvasHeight,
  };
}
