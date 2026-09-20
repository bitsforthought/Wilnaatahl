import React, { useLayoutEffect, useRef, useState } from "react";
import { useQuery } from "koota/react";
import {
  FamilyGraph_FamilyGraph as FamilyGraph,
  FamilyGraph_namesHeldBy,
} from "../generated/Model";
import { NodeDetailModule_build } from "../generated/ViewModel/NodeContent";
import { OverlayPlacement_place } from "../generated/ViewModel/OverlayPlacement";
import { useLocale } from "../i18n/hooks";
import { PersonRef, Selected } from "../ecs";
import { dismissButtonStyle } from "./styles";
import type { OverlayAnchor } from "./anchor";
import { buildDetailContent } from "./detailContent";

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

    const position = OverlayPlacement_place(
      anchor.nodeLeft,
      anchor.nodeRight,
      anchor.nodeTop,
      anchor.nodeBottom,
      anchor.canvasWidth,
      anchor.canvasHeight,
      width,
      height
    );

    setPlacement({ anchor, left: position.Left, top: position.Top });
  }, [anchor]);

  if (!anchor || !detail) return null;

  const content = buildDetailContent(locale, detail);
  const hasDates = content.born != null || content.died != null;

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
        <span>{content.title}</span>
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
        {content.kinshipRows.map((row, i) => (
          <div key={i}>{row}</div>
        ))}
      </div>

      {hasDates && (
        <>
          <hr style={dividerStyle} />
          <div>
            {content.born != null && <div>{content.born}</div>}
            {content.died != null && <div>{content.died}</div>}
          </div>
        </>
      )}

      {content.otherNames.length > 0 && (
        <>
          <hr style={dividerStyle} />
          <div>
            <div style={sectionHeadingStyle}>{content.otherNamesHeading}</div>
            {content.otherNames.map((name, i) => (
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
