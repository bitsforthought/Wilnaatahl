import React, { useLayoutEffect, useRef, useState } from "react";
import { useQuery } from "koota/react";
import {
  FamilyGraph_FamilyGraph as FamilyGraph,
  FamilyGraph_namesHeldBy,
} from "../generated/Model";
import { NodeDetailModule_build } from "../generated/ViewModel/NodeContent";
import { bornText, diedText, kinshipRowText, otherNamesHeading } from "../i18n/format";
import { useLocale } from "../i18n/hooks";
import { PersonRef, Selected } from "../ecs";
import { dismissButtonStyle } from "./styles";

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

// Horizontal offset between the node and the card, and the minimum breathing room
// kept from every canvas edge. Both are by-eye tuning values.
const GAP_PX = 16;
const MARGIN_PX = 12;

const overlayCardStyle: React.CSSProperties = {
  position: "absolute",
  boxSizing: "border-box",
  width: "18em",
  maxWidth: "min(22em, 90vw)",
  maxHeight: "70vh",
  overflowY: "auto",
  overflowWrap: "break-word",
  background: "#2b2b2b",
  border: "1px solid #454545",
  color: "rgba(240, 240, 240, 0.95)",
  borderRadius: "8px",
  boxShadow: "0 2px 12px rgba(0, 0, 0, 0.4)",
  padding: "0.5em 0.9em 0.75em",
  // The canvas and its drei node labels are isolated in a lower stacking context
  // (see Visualizer), so any positive z-index keeps the card above the node text.
  zIndex: 100,
};

const headerStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "flex-start",
  justifyContent: "space-between",
  gap: "0.5em",
  fontWeight: "bold",
};

const dividerStyle: React.CSSProperties = {
  border: "none",
  borderTop: "1px solid #454545",
  margin: "0.5em 0",
};

const sectionHeadingStyle: React.CSSProperties = {
  fontWeight: "bold",
  marginBottom: "0.25em",
};

const nameRowStyle: React.CSSProperties = {
  marginLeft: "1em",
};

/**
 * The detail card for the single selected node. Renders nothing unless an
 * `anchor` (i.e. an active, projected overlay) and a single selected node are
 * present. All content is provided ready-to-render by the F# `NodeDetail.build`
 * view model; this component only lays it out. Position is computed once from
 * `anchor` plus the card's measured size, hidden until placed to avoid a
 * one-frame flash at the default corner.
 */
export function DetailOverlay({
  graph,
  anchor,
  onDismiss,
}: {
  graph: FamilyGraph;
  anchor: OverlayAnchor | null;
  onDismiss: () => void;
}) {
  const selected = useQuery(Selected, PersonRef);
  const locale = useLocale();
  const cardRef = useRef<HTMLDivElement>(null);
  const [placement, setPlacement] = useState<{
    anchor: OverlayAnchor;
    left: number;
    top: number;
  } | null>(null);

  const entity = selected.length === 1 ? selected[0] : undefined;
  const person = entity?.get(PersonRef);
  const detail = person
    ? NodeDetailModule_build(person, FamilyGraph_namesHeldBy(person.Id, graph))
    : undefined;

  // Measure the card once per anchor and compute its final position, flipping
  // left/up when a default right/top placement would spill off the canvas.
  useLayoutEffect(() => {
    if (!anchor || !cardRef.current) return;
    const { width, height } = cardRef.current.getBoundingClientRect();

    // Offset from the node's right/left edge (not its center) so the card sits
    // beside the node instead of overlapping it.
    let left = anchor.nodeRight + GAP_PX;
    if (anchor.nodeRight + GAP_PX + width + MARGIN_PX > anchor.canvasWidth) {
      left = anchor.nodeLeft - GAP_PX - width;
    }
    left = Math.max(MARGIN_PX, Math.min(left, anchor.canvasWidth - width - MARGIN_PX));

    let top = anchor.nodeTop;
    if (anchor.nodeTop + height + MARGIN_PX > anchor.canvasHeight) {
      top = anchor.nodeBottom - height;
    }
    top = Math.max(MARGIN_PX, Math.min(top, anchor.canvasHeight - height - MARGIN_PX));

    setPlacement({ anchor, left, top });
  }, [anchor]);

  if (!anchor || !detail) return null;

  const kinshipRows = Array.from(detail.Kinship).map((row) => kinshipRowText(locale, row));
  const otherNames = Array.from(detail.OtherNames);
  const born = bornText(locale, detail);
  const died = diedText(locale, detail);
  const hasDates = born != null || died != null;

  // Only reveal the card once its position has been computed for THIS anchor;
  // otherwise it would flash at the top-left corner for one frame.
  const ready = placement !== null && placement.anchor === anchor;

  const stop = (e: React.SyntheticEvent) => e.stopPropagation();

  return (
    <div
      ref={cardRef}
      role="dialog"
      style={{
        ...overlayCardStyle,
        left: ready ? placement.left : 0,
        top: ready ? placement.top : 0,
        visibility: ready ? "visible" : "hidden",
      }}
      onPointerDown={stop}
      onClick={stop}
    >
      <div style={headerStyle}>
        <span>{detail.Title}</span>
        <button
          onClick={onDismiss}
          aria-label="Dismiss detail"
          style={{ ...dismissButtonStyle, marginTop: "-0.15em" }}
        >
          ×
        </button>
      </div>

      <hr style={dividerStyle} />
      <div>
        {kinshipRows.map((row, i) => (
          <div key={i}>{row}</div>
        ))}
      </div>

      {hasDates && (
        <>
          <hr style={dividerStyle} />
          <div>
            {born != null && <div>{born}</div>}
            {died != null && <div>{died}</div>}
          </div>
        </>
      )}

      {otherNames.length > 0 && (
        <>
          <hr style={dividerStyle} />
          <div>
            <div style={sectionHeadingStyle}>{otherNamesHeading(locale)}</div>
            {otherNames.map((name, i) => (
              <div key={i} style={nameRowStyle}>
                {name}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
