import type { Content } from '../content/types';
import type { Loadout } from '../state/types';

const sameList = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((x, i) => x === b[i]);

/**
 * Keeps a selected preset honest across a content reload. The loadout screen clears the preset id on any manual
 * edit, so a set id means the loadout is that preset's picks: take the edited preset's picks, or drop the label
 * when the preset is gone. Returns the same loadout object when nothing changes.
 */
export function syncPresetLoadout(c: Content, loadout: Loadout, presetId: string | null): { loadout: Loadout; presetId: string | null } {
  if (presetId === null) return { loadout, presetId };
  const preset = c.presets.find((p) => p.id === presetId);
  if (!preset) return { loadout, presetId: null };
  if (sameList(loadout.abilities, preset.abilities) && sameList(loadout.upgrades, preset.upgrades)) return { loadout, presetId };
  return { loadout: { abilities: [...preset.abilities], upgrades: [...preset.upgrades] }, presetId };
}
