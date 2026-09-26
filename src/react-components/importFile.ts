import { FamilyGraph_FamilyGraph as FamilyGraph } from "../generated/Model";
import {
  ImportError_$union as ImportError,
  ImportWarning_$union as ImportWarning,
} from "../generated/Persistence/Transform";
import {
  ImportService_importJsonText,
  ImportSuccess,
} from "../generated/Persistence/ImportService";
import {
  ImportErrorModule_toMessage,
  ImportWarningModule_summary,
} from "../generated/ViewModel/ImportMessages";
import { Locale } from "../generated/ViewModel/Localization";
import { FSharpList } from "../generated/fable_modules/fable-library-ts.5.1.0/List";

export type ImportOutcome =
  | { kind: "ok"; graph: FamilyGraph; warnings?: FSharpList<ImportWarning> }
  | { kind: "error"; message: string };

export function normalizeThrownValue(thrown: unknown): string {
  return thrown instanceof Error ? thrown.message : String(thrown);
}

export async function readFileText(file: Pick<File, "text">): Promise<string> {
  return file.text();
}

export async function importFile(file: Pick<File, "text">, locale: Locale): Promise<ImportOutcome> {
  let text: string;
  try {
    text = await readFileText(file);
  } catch (thrown) {
    return { kind: "error", message: `Could not read file: ${normalizeThrownValue(thrown)}` };
  }

  const result = ImportService_importJsonText(text);
  if (result.tag !== 0) {
    return {
      kind: "error",
      message: ImportErrorModule_toMessage(locale, result.fields[0] as ImportError),
    };
  }

  const success = result.fields[0] as ImportSuccess;
  // summary is "" exactly when there are no warnings, so it doubles as a
  // non-emptiness test without reaching into Fable's list representation.
  const summary = ImportWarningModule_summary(locale, success.Warnings);
  const warnings = summary !== "" ? success.Warnings : undefined;
  return { kind: "ok", graph: success.Graph, ...(warnings === undefined ? {} : { warnings }) };
}
