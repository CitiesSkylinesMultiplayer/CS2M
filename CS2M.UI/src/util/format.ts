/** Fills {NAME} placeholders in a translated string. */
export function format(text: string | null, args: Record<string, string | number>): string {
    let result = text ?? "";
    for (const key of Object.keys(args)) {
        result = result.split("{" + key + "}").join(String(args[key]));
    }
    return result;
}
