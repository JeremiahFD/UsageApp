import type { InterfaceFont } from "@usageapp/core";

export const INTERFACE_FONT_OPTIONS: ReadonlyArray<{
  value: InterfaceFont;
  label: string;
}> = [
  { value: "system", label: "Windows default" },
  { value: "segoe-ui", label: "Segoe UI" },
  { value: "verdana", label: "Verdana" },
  { value: "tahoma", label: "Tahoma" },
  { value: "arial", label: "Arial" },
  { value: "trebuchet-ms", label: "Trebuchet MS" },
  { value: "georgia", label: "Georgia" },
  { value: "consolas", label: "Consolas" },
];

export function interfaceFontFamily(value: InterfaceFont): string {
  switch (value) {
    case "segoe-ui":
      return '"Segoe UI", sans-serif';
    case "verdana":
      return 'Verdana, sans-serif';
    case "tahoma":
      return 'Tahoma, sans-serif';
    case "arial":
      return 'Arial, sans-serif';
    case "trebuchet-ms":
      return '"Trebuchet MS", sans-serif';
    case "georgia":
      return 'Georgia, serif';
    case "consolas":
      return 'Consolas, "Courier New", monospace';
    default:
      return 'Inter, "Segoe UI Variable", "Segoe UI", system-ui, sans-serif';
  }
}
