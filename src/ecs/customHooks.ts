import { RefObject, useLayoutEffect, useRef } from "react";
import { Mesh } from "three";
import { Entity } from "koota";
import { useQuery, useTrait, useWorld } from "koota/react";
import { isViewing } from "../generated/Traits/ViewTraits";
import { CurrentMode, MeshRef, Selected } from "./traits";

// Custom React hook to dynamically attach a Mesh to an Entity via the MeshRef trait.
export function useMeshRef(entity: Entity): RefObject<Mesh | null> {
  const ref = useRef<Mesh>(null);

  useLayoutEffect(() => {
    if (!ref.current) {
      return;
    }

    entity.add(MeshRef(ref.current));
    return () => {
      entity.remove(MeshRef);
    };
  }, [entity]);

  return ref;
}

/**
 * Whether the detail overlay should show: View mode with exactly one node selected.
 * Derived from the two traits it depends on rather than mirrored into a world trait,
 * so there is no cached copy that can disagree with the mode or the selection.
 */
export function useOverlayVisible(): boolean {
  const world = useWorld();
  const mode = useTrait(world, CurrentMode);
  const selectedCount = useQuery(Selected).length;
  return mode !== undefined && isViewing(mode) && selectedCount === 1;
}
