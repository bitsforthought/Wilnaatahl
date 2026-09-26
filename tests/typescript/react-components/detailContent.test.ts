import { describe, expect, it } from "vitest";
import { ofArray } from "../../../src/generated/fable_modules/fable-library-ts.5.1.0/List.js";
import { NodeDetail } from "../../../src/generated/ViewModel/NodeContent";
import { EN } from "../../../src/i18n/format";
import { buildDetailContent } from "../../../src/react-components/detailContent";

describe("buildDetailContent", () => {
  it("formats kinship, both dates, and other names", () => {
    const detail = new NodeDetail(
      "A",
      ofArray([{ kind: "currentWilp", wilpName: "Wolf" }, { kind: "kinshipUnknown" }]),
      { kind: "rawText", text: "spring" },
      { kind: "rawText", text: "winter" },
      ofArray(["B", "C"])
    );

    expect(buildDetailContent(EN, detail)).toEqual({
      title: "A",
      kinshipRows: ["Wilp: Wolf", "Kinship: Not provided"],
      born: "Born: spring",
      died: "Died: winter",
      otherNames: ["B", "C"],
      otherNamesHeading: "Other names held:",
    });
  });

  it("omits each optional presentation section independently", () => {
    const noDates = new NodeDetail("A", ofArray([]), undefined, undefined, ofArray([]));
    expect(buildDetailContent(EN, noDates)).toEqual({
      title: "A",
      kinshipRows: [],
      born: undefined,
      died: undefined,
      otherNames: [],
      otherNamesHeading: "Other names held:",
    });

    const birthOnly = new NodeDetail(
      "A",
      ofArray([{ kind: "currentPdeek", pdeekDisplay: "Pdeek" }]),
      { kind: "rawText", text: "1900" },
      undefined,
      ofArray([])
    );
    expect(buildDetailContent(EN, birthOnly).born).toBe("Born: 1900");
    expect(buildDetailContent(EN, birthOnly).died).toBeUndefined();
  });
});
