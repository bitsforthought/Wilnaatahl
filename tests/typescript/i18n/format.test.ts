import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { empty } from "../../../src/generated/fable_modules/fable-library-ts.5.1.0/List";
import { LocaleModule_parse } from "../../../src/generated/ViewModel/Localization";
import {
  DisplayDate,
  KinshipRow,
  NodeDetail,
  NodeLabelView,
} from "../../../src/generated/ViewModel/NodeContent";
import {
  bornText,
  composeNodeLabel,
  diedText,
  detectLocale,
  EN,
  kinshipRowText,
  otherNamesHeading,
} from "../../../src/i18n/format";

const locale = LocaleModule_parse("en-US");
const formattedDate = (date: Date): DisplayDate => ({
  kind: "formattedDate",
  date,
});
const rawDate = (text: string): DisplayDate => ({ kind: "rawText", text });

beforeEach(() => {
  vi.stubGlobal("navigator", { language: "en-US" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function labelView(
  colonialName: string | undefined,
  mostRecentName: string | undefined,
  kinshipParen: string | undefined,
  born: DisplayDate | undefined,
  died: DisplayDate | undefined
): NodeLabelView {
  return new NodeLabelView(colonialName, mostRecentName, kinshipParen, born, died);
}

function detail(born: DisplayDate | undefined, died: DisplayDate | undefined): NodeDetail {
  return new NodeDetail("", empty<KinshipRow>(), born, died, empty<string>());
}

describe("format date rendering", () => {
  test("formats short dates in UTC for node labels", () => {
    const date = new Date("2024-01-05T00:00:00.000Z");

    expect(
      composeNodeLabel(
        labelView(undefined, undefined, undefined, formattedDate(date), undefined),
        locale
      )
    ).toBe("B 01/05/2024");
  });

  test("formats long dates in UTC for detail rows", () => {
    const date = new Date("2024-01-05T00:00:00.000Z");

    expect(bornText(locale, detail(formattedDate(date), undefined))).toBe("Born: January 5, 2024");
    expect(diedText(locale, detail(undefined, formattedDate(date)))).toBe("Died: January 5, 2024");
  });

  test("renders unparseable date text verbatim in both views", () => {
    const date = rawDate("circa 1900");

    expect(
      composeNodeLabel(labelView(undefined, undefined, undefined, date, undefined), locale)
    ).toBe("B circa 1900");
    expect(bornText(locale, detail(date, undefined))).toBe("Born: circa 1900");
  });

  test("formats both short dates when both dates are formatted", () => {
    expect(
      composeNodeLabel(
        labelView(
          undefined,
          undefined,
          undefined,
          formattedDate(new Date("2024-01-05T00:00:00.000Z")),
          formattedDate(new Date("2024-01-06T00:00:00.000Z"))
        ),
        locale
      )
    ).toBe("B 01/05/2024 - D 01/06/2024");
  });

  test("formats a date when navigator is unavailable", () => {
    vi.stubGlobal("navigator", undefined);
    const date = new Date("2024-01-05T00:00:00.000Z");
    const expectedDate = new Intl.DateTimeFormat(undefined, {
      timeZone: "UTC",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    }).format(date);
    const dateTimeFormat = vi.spyOn(Intl, "DateTimeFormat");

    expect(
      composeNodeLabel(
        labelView(undefined, undefined, undefined, formattedDate(date), undefined),
        locale
      )
    ).toBe(`B ${expectedDate}`);
    expect(dateTimeFormat).toHaveBeenCalledWith(undefined, {
      timeZone: "UTC",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    });
  });

  test("reads the browser locale and falls back when navigator is unavailable", () => {
    vi.stubGlobal("navigator", {
      get language() {
        throw new Error("language was read");
      },
    });
    expect(() => detectLocale()).toThrowError("language was read");

    vi.stubGlobal("navigator", undefined);
    expect(detectLocale()).toEqual(EN);
  });
});

describe("composeNodeLabel", () => {
  test.each([
    [
      "all available lines",
      labelView("Colonial", "Recent", "Wilp", rawDate("1900"), rawDate("2000")),
      "Colonial\nRecent\n(Wilp)\nB 1900 - D 2000",
    ],
    [
      "only the death date",
      labelView(undefined, undefined, undefined, undefined, rawDate("2000")),
      "- D 2000",
    ],
    [
      "names and kinship without dates",
      labelView("Colonial", "Recent", "Wilp", undefined, undefined),
      "Colonial\nRecent\n(Wilp)",
    ],
    ["no content", labelView(undefined, undefined, undefined, undefined, undefined), ""],
  ])("%s", (_, view, expected) => {
    expect(composeNodeLabel(view, EN)).toBe(expected);
  });

  test("omits absent optional detail date rows", () => {
    expect(bornText(EN, detail(undefined, undefined))).toBeUndefined();
    expect(diedText(EN, detail(undefined, undefined))).toBeUndefined();
  });
});

describe("kinship and heading text", () => {
  test.each<[KinshipRow, string]>([
    [{ kind: "currentWilp", wilpName: "House" }, "Wilp: House"],
    [{ kind: "currentPdeek", pdeekDisplay: "Pdeeḵ A" }, "Pdeeḵ: Pdeeḵ A"],
    [{ kind: "birthWilp", wilpName: "Birth House" }, "Birth Wilp: Birth House"],
    [{ kind: "birthPdeek", pdeekDisplay: "Birth Pdeeḵ" }, "Birth Pdeeḵ: Birth Pdeeḵ"],
    [{ kind: "kinshipNote", note: "Maternal" }, "Kinship: Maternal"],
    [{ kind: "kinshipUnknown" }, "Kinship: Not provided"],
  ])("formats %j", (row, expected) => {
    expect(kinshipRowText(EN, row)).toBe(expected);
  });

  test("reports an unexpected runtime row with its exact error", () => {
    expect(() => kinshipRowText(EN, { kind: "futureCase", value: 1 } as never)).toThrowError(
      'Unhandled KinshipRow: {"kind":"futureCase","value":1}'
    );
  });

  test("returns the translated other-names heading", () => {
    expect(otherNamesHeading(EN)).toBe("Other names held:");
  });
});
