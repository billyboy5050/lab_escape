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

/** True for the keys that end the turn: E, and Enter unless a button, link or summary has focus (Enter operates it then). */
export function endsTurn(key: string, focus: { tagName?: string } | null): boolean {
  if (key === 'e' || key === 'E') return true;
  if (key !== 'Enter') return false;
  const tag = focus?.tagName;
  return tag !== 'BUTTON' && tag !== 'A' && tag !== 'SUMMARY';
}
