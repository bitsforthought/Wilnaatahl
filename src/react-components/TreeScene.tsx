import React, { useEffect, useRef } from "react";
import { useFrame, useThree } from "@react-three/fiber";
import { OrbitControls } from "@react-three/drei";
import { useHas, useQuery, useWorld } from "koota/react";
import { Vector3 } from "three";
import type { OrbitControls as OrbitControlsImpl } from "three-stdlib";
import { DragInFlight, MeshRef, Selected, runSystems, useOverlayVisible } from "../ecs";
import type { ObserverSession } from "../testing/observer";
import { HuwilpGroup } from "./HuwilpGroup";
import { projectAnchor, OverlayAnchor } from "./anchor";

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
    onAnchor(projectAnchor(mesh, camera, width, height));
    // `entityId` stands in for the (stable) selected entity; re-running on the
    // entity object itself would fire every frame as the query array is rebuilt.
  }, [overlayVisible, entityId, camera, width, height, onAnchor]);

  return null;
}

export default function TreeScene({
  onOverlayAnchor,
}: {
  onOverlayAnchor: (anchor: OverlayAnchor | null) => void;
}) {
  const world = useWorld();
  const camera = useThree((state) => state.camera);
  const gl = useThree((state) => state.gl);
  const controlsRef = useRef<OrbitControlsImpl>(null);
  const observerRef = useRef<ObserverSession | null>(null);
  const isDragInProgress = useHas(world, DragInFlight);
  const overlayVisible = useOverlayVisible();

  useEffect(() => {
    if (import.meta.env.MODE !== "e2e") return;

    let disposed = false;
    let disposeObserver: (() => void) | undefined;

    void import("../testing/observer").then(({ registerE2EObserver }) => {
      if (disposed) return;
      const registration = registerE2EObserver({
        world,
        camera,
        readTarget: () => controlsRef.current?.target ?? new Vector3(),
        readCanvas: () => {
          const { left, top, width, height } = gl.domElement.getBoundingClientRect();
          return { left, top, width, height };
        },
      });
      observerRef.current = registration;
      disposeObserver = () => registration.dispose();
    });

    return () => {
      disposed = true;
      disposeObserver?.();
      observerRef.current = null;
    };
  }, [camera, gl, world]);

  useFrame(function runFrame(_state, delta) {
    runSystems({ world, delta });
    observerRef.current?.advanceFrame();
  });

  return (
    <group>
      {/* Ambient light for general illumination */}
      <ambientLight intensity={0.7} />
      {/* Directional light for stronger highlights and shadows */}
      <directionalLight position={[5, 5, 7]} intensity={1} castShadow />
      {/* Additional point light for more dynamic lighting */}
      <pointLight position={[1, -1, 2]} intensity={5} castShadow />
      <OrbitControls ref={controlsRef} enabled={!isDragInProgress && !overlayVisible} />
      <HuwilpGroup />
      <OverlayProjector onAnchor={onOverlayAnchor} />
    </group>
  );
}
