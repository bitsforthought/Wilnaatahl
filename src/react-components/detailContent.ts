import { NodeDetail } from "../generated/ViewModel/NodeContent";
import { bornText, diedText, kinshipRowText, otherNamesHeading } from "../i18n/format";
import { Locale } from "../generated/ViewModel/Localization";

export type DetailContent = {
  title: string;
  kinshipRows: string[];
  born: string | undefined;
  died: string | undefined;
  otherNames: string[];
  otherNamesHeading: string;
};

/** Formats the view model into the strings and collections rendered by the card. */
export function buildDetailContent(locale: Locale, detail: NodeDetail): DetailContent {
  return {
    title: detail.Title,
    kinshipRows: Array.from(detail.Kinship).map((row) => kinshipRowText(locale, row)),
    born: bornText(locale, detail),
    died: diedText(locale, detail),
    otherNames: Array.from(detail.OtherNames),
    otherNamesHeading: otherNamesHeading(locale),
  };
}
