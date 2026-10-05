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

/** The part of a DOM element the key helpers read, so they can be tested without a DOM. */
export interface FocusLike {
  tagName?: string;
  disabled?: boolean;
}

/**
 * True when Enter and Space operate the focused element itself: an enabled button, link or summary. A shortcut on
 * either key must leave the keystroke alone then, or keyboard users cannot activate the control. A disabled button
 * can be focused (it was clicked, then something disabled it) but cannot be activated, so it does not count.
 */
export function operatesFocus(focus: FocusLike | null): boolean {
  const tag = focus?.tagName;
  return (tag === 'BUTTON' || tag === 'A' || tag === 'SUMMARY') && !focus?.disabled;
}

/** True for the keys that end the turn: E, and Enter unless a button, link or summary has focus (Enter operates it then). */
export function endsTurn(key: string, focus: FocusLike | null): boolean {
  if (key === 'e' || key === 'E') return true;
  return key === 'Enter' && !operatesFocus(focus);
}

/** True when Space should skip the animation: unless a control has focus, where Space activates that control instead. */
export function spaceSkips(focus: FocusLike | null): boolean {
  return !operatesFocus(focus);
}
