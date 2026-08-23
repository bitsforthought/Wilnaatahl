import React, { useEffect } from "react";
import { useFrame, useThree } from "@react-three/fiber";
import { OrbitControls } from "@react-three/drei";
import { Box3, Vector3 } from "three";
import { useQuery, useWorld } from "koota/react";
import { DragOrigin, MeshRef, Selected, runSystems, useOverlayVisible } from "../ecs";
import { HuwilpGroup } from "./HuwilpGroup";
import type { OverlayAnchor } from "./DetailOverlay";

/**
 * Converts a point in normalized device coordinates (NDC) to canvas pixels.
 *
 * `Vector3.project(camera)` returns NDC: the camera's view volume squashed into a
 * cube spanning -1..1 on every axis, with the origin at the centre of the viewport.
 * Screen pixels instead run 0..width from the left and 0..height from the *top*.
 * So each axis is rescaled from -1..1 to 0..1 and multiplied by the canvas
 * dimension. The y term is negated first because NDC's y grows upward while the
 * screen's grows downward.
 *
 * See https://threejs.org/docs/#api/en/math/Vector3.project and
 * https://learnopengl.com/Getting-started/Coordinate-Systems for background.
 */
function ndcToScreen(v: Vector3, width: number, height: number): { x: number; y: number } {
  // One over the -1..1 span, then a shift of half a unit to move the origin from the
  // centre to the edge. Equal by coincidence: the NDC span is twice the unit range.
  const NDC_TO_UNIT_SCALE = 0.5;
  const NDC_TO_UNIT_OFFSET = 0.5;

  return {
    x: (v.x * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * width,
    y: (-v.y * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * height,
  };
}

/**
 * Lives inside the Canvas so it can read the R3F camera and canvas size. When
 * the overlay becomes visible for a single selected node, it projects that
 * node's world bounds to canvas pixels once and reports the anchor upward; the
 * DOM-level DetailOverlay uses it to place the card. Reports `null` whenever no
 * overlay should show.
 *
 * Renders nothing: it exists only for that reporting side effect, and has to be a
 * component rather than a hook in its parent because the R3F camera and size are
 * only readable from inside the Canvas.
 */
function OverlayProjector({ onAnchor }: { onAnchor: (anchor: OverlayAnchor | null) => void }) {
  const camera = useThree((state) => state.camera);
  const width = useThree((state) => state.size.width);
  const height = useThree((state) => state.size.height);
  const overlayVisible = useOverlayVisible();
  const selected = useQuery(Selected, MeshRef);
  const entity = selected.length === 1 ? selected[0] : undefined;
  const entityId = entity?.id();

  useEffect(() => {
    const mesh = entity?.get(MeshRef);
    if (!overlayVisible || !mesh) {
      onAnchor(null);
      return;
    }
    // Three.js names these positionally; the call reads as (updateParents, updateChildren).
    const UPDATE_PARENTS = true;
    const UPDATE_CHILDREN = false;
    mesh.updateWorldMatrix(UPDATE_PARENTS, UPDATE_CHILDREN);
    const box = new Box3().setFromObject(mesh);
    // Project all eight corners of the node's world-space box and take the
    // screen-space min/max, so the anchor's left/right/top/bottom are edge-correct
    // regardless of how the camera was orbited before selection (projecting only
    // box.min/box.max could swap or collapse the edges under rotation).
    const corners = [
      new Vector3(box.min.x, box.min.y, box.min.z),
      new Vector3(box.min.x, box.min.y, box.max.z),
      new Vector3(box.min.x, box.max.y, box.min.z),
      new Vector3(box.min.x, box.max.y, box.max.z),
      new Vector3(box.max.x, box.min.y, box.min.z),
      new Vector3(box.max.x, box.min.y, box.max.z),
      new Vector3(box.max.x, box.max.y, box.min.z),
      new Vector3(box.max.x, box.max.y, box.max.z),
    ];

    let minX = Infinity;
    let maxX = -Infinity;
    let minY = Infinity;
    let maxY = -Infinity;
    for (const corner of corners) {
      const screen = ndcToScreen(corner.project(camera), width, height);
      minX = Math.min(minX, screen.x);
      maxX = Math.max(maxX, screen.x);
      minY = Math.min(minY, screen.y);
      maxY = Math.max(maxY, screen.y);
    }

    onAnchor({
      nodeLeft: minX,
      nodeRight: maxX,
      nodeTop: minY,
      nodeBottom: maxY,
      canvasWidth: width,
      canvasHeight: height,
    });
    // `entityId` stands in for the (stable) selected entity; re-running on the
    // entity object itself would fire every frame as the query array is rebuilt.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [overlayVisible, entityId, camera, width, height, onAnchor]);

  return null;
}

export default function TreeScene({
  onOverlayAnchor,
}: {
  onOverlayAnchor: (anchor: OverlayAnchor | null) => void;
}) {
  const world = useWorld();
  // Every node the drag is moving carries the position it started from, so any of them means a
  // drag is running.
  const isDragInProgress = useQuery(DragOrigin).length > 0;
  const overlayVisible = useOverlayVisible();

  useFrame((_state, delta) => {
    runSystems({ world, delta });
  });

  return (
    <group>
      {/* Ambient light for general illumination */}
      <ambientLight intensity={0.7} />
      {/* Directional light for stronger highlights and shadows */}
      <directionalLight position={[5, 5, 7]} intensity={1} castShadow />
      {/* Additional point light for more dynamic lighting */}
      <pointLight position={[1, -1, 2]} intensity={5} castShadow />
      <OrbitControls enabled={!isDragInProgress && !overlayVisible} />
      <HuwilpGroup />
      <OverlayProjector onAnchor={onOverlayAnchor} />
    </group>
  );
}
