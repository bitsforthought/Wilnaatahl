// The view-layer localization boundary. The F# core exposes a presentation-neutral
// view model (structured `NodeLabelView`/`NodeDetail` with `DisplayDate` values and
// `KinshipRow`s); this module formats dates via the browser's `Intl` API and pulls
// translatable chrome from the F#-authored catalog (`UiText`), then composes the
// display strings. Language selection flows through the `Locale` type; date/number
// formatting follows the device's regional settings (`navigator.language`).

import {
  Locale,
  LocaleModule_parse,
  UiTextModule_text as uiText,
} from "../generated/ViewModel/Localization";
import {
  DisplayDate,
  KinshipRow,
  NodeDetail,
  NodeLabelView,
} from "../generated/ViewModel/NodeContent";
import { unwrap } from "../generated/fable_modules/fable-library-ts.5.1.0/Option.js";

/**
 * The default/fallback locale (English is the only implemented language today).
 * Used when the world's `CurrentLocale` trait has not yet been written.
 */
export const EN: Locale = LocaleModule_parse("");

/** Resolves the active UI language from the browser's preferred language. */
export function detectLocale(): Locale {
  return LocaleModule_parse(typeof navigator !== "undefined" ? navigator.language : "");
}

// Date formatting follows the device's regional settings, independent of the UI
// language, so a value read fresh at format time reflects a live locale change.
function regionTag(): string | undefined {
  return typeof navigator !== "undefined" ? navigator.language : undefined;
}

// `DateOnly` values are compiled by Fable to a JS `Date` anchored at UTC midnight,
// so the formatter must read them in UTC — otherwise a device west of UTC renders
// the calendar date one day early. Only the pattern (order/separators/month name)
// follows the region; the date value does not shift.
const SHORT_DATE: Intl.DateTimeFormatOptions = {
  timeZone: "UTC",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
};

const LONG_DATE: Intl.DateTimeFormatOptions = {
  timeZone: "UTC",
  year: "numeric",
  month: "long",
  day: "numeric",
};

/**
 * Renders a `DisplayDate`: a `formattedDate` is formatted per the device's region
 * (compact for the node label, long for the overlay); a `rawText` fallback (an
 * unparseable original value) is shown verbatim.
 */
function formatDisplayDate(displayDate: DisplayDate, style: "short" | "long"): string {
  if (displayDate.kind === "rawText") {
    return displayDate.text;
  }

  const options = style === "short" ? SHORT_DATE : LONG_DATE;
  return new Intl.DateTimeFormat(regionTag(), options).format(displayDate.date);
}

/** The composed `B …`/`D …` date line for a node label, or `undefined` when neither date is known. */
function nodeDateLine(view: NodeLabelView, locale: Locale): string | undefined {
  const bornDate = unwrap(view.Born);
  const diedDate = unwrap(view.Died);
  const born = bornDate != null ? formatDisplayDate(bornDate, "short") : undefined;
  const died = diedDate != null ? formatDisplayDate(diedDate, "short") : undefined;
  const b = uiText(locale, "birthAbbreviation");
  const d = uiText(locale, "deathAbbreviation");

  if (born != null && died != null) return `${b} ${born} - ${d} ${died}`;
  if (born != null) return `${b} ${born}`;
  if (died != null) return `- ${d} ${died}`;
  return undefined;
}

/**
 * Composes the multi-line node label from its presentation-neutral view: the
 * colonial name, the most-recent Name, a parenthesized Kinship line, and a date
 * line — each omitted when absent, so the result never contains a blank line.
 */
export function composeNodeLabel(view: NodeLabelView, locale: Locale): string {
  const lines: string[] = [];
  const colonialName = unwrap(view.ColonialName);
  const mostRecentName = unwrap(view.MostRecentName);
  const kinshipParen = unwrap(view.KinshipParen);

  if (colonialName != null) lines.push(colonialName);
  if (mostRecentName != null) lines.push(mostRecentName);
  if (kinshipParen != null) lines.push(`(${kinshipParen})`);

  const dateLine = nodeDateLine(view, locale);
  if (dateLine != null) lines.push(dateLine);

  return lines.join("\n");
}

/** Renders one Kinship row of the detail overlay as `<label> <value>`. */
export function kinshipRowText(locale: Locale, row: KinshipRow): string {
  switch (row.kind) {
    case "currentWilp":
      return `${uiText(locale, "wilpLabel")} ${row.wilpName}`;
    case "currentPdeek":
      return `${uiText(locale, "pdeekLabel")} ${row.pdeekDisplay}`;
    case "birthWilp":
      return `${uiText(locale, "birthWilpLabel")} ${row.wilpName}`;
    case "birthPdeek":
      return `${uiText(locale, "birthPdeekLabel")} ${row.pdeekDisplay}`;
    case "kinshipNote":
      return `${uiText(locale, "kinshipLabel")} ${row.note}`;
    case "kinshipUnknown":
      return `${uiText(locale, "kinshipLabel")} ${uiText(locale, "kinshipNotProvided")}`;
    default: {
      // A KinshipRow case added in F# fails this assignment at compile time; the
      // throw only covers a value that escapes the type at runtime.
      const unhandled: never = row;
      throw new Error(`Unhandled KinshipRow: ${JSON.stringify(unhandled)}`);
    }
  }
}

/** The self-labeled `Born: <date>` overlay row, or `undefined` when the birth date is unknown. */
export function bornText(locale: Locale, detail: NodeDetail): string | undefined {
  const born = unwrap(detail.Born);
  return born != null
    ? `${uiText(locale, "bornLabel")} ${formatDisplayDate(born, "long")}`
    : undefined;
}

/** The self-labeled `Died: <date>` overlay row, or `undefined` when the death date is unknown. */
export function diedText(locale: Locale, detail: NodeDetail): string | undefined {
  const died = unwrap(detail.Died);
  return died != null
    ? `${uiText(locale, "diedLabel")} ${formatDisplayDate(died, "long")}`
    : undefined;
}

/** The overlay's "other names held" section heading. */
export function otherNamesHeading(locale: Locale): string {
  return uiText(locale, "otherNamesHeldHeading");
}
