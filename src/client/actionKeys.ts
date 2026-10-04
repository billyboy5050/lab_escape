/**
 * Keyboard shortcuts for the action bar, in bar order: 1 to 9, 0, then - and =. A full loadout (8 abilities) plus the
 * four utilities is 12 actions, and every one needs a key. The bar's labels and the key handler both read this table.
 */
export const ACTION_KEYS: readonly string[] = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '-', '='];

/** The key label for the action at this position in the bar, or '' when there is none. */
export function actionKeyLabel(index: number): string {
  return ACTION_KEYS[index] ?? '';
}

/** The position in the bar that a key selects, or -1 when the key is not an action shortcut. */
export function actionIndexForKey(key: string): number {
  return ACTION_KEYS.indexOf(key);
}
