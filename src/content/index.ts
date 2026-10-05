// Default content, imported from the JSON files in content/. Vite hot-reloads these imports in the client,
// Node (tsx) and Vitest import them directly.
import rules from '../../content/rules.json';
import units from '../../content/units.json';
import abilities from '../../content/abilities.json';
import upgrades from '../../content/upgrades.json';
import hazards from '../../content/hazards.json';
import map from '../../content/map.json';
import waves from '../../content/waves.json';
import intercom from '../../content/intercom.json';
import presets from '../../content/presets.json';
import { buildContent } from './load';
import type { Content, ContentFiles } from './types';

export * from './types';
export { buildContent, validateContent, upgradeAllowed, ContentError } from './load';

/** Fresh deep copy of the content files, safe to modify (used by tuning sweeps). */
export function defaultContentFiles(): ContentFiles {
  return structuredClone({ rules, units, abilities, upgrades, hazards, map, waves, intercom, presets }) as unknown as ContentFiles;
}

let cached: Content | null = null;

/** The validated default content. */
export function defaultContent(): Content {
  cached ??= buildContent(defaultContentFiles());
  return cached;
}

/** Applies dotted-path overrides such as {"units.warden.hp": 16} to a copy of the files. */
export function withOverrides(files: ContentFiles, overrides: Record<string, unknown>): ContentFiles {
  const out = structuredClone(files);
  for (const [path, value] of Object.entries(overrides)) {
    const parts = path.split('.');
    let node: unknown = out;
    for (let i = 0; i < parts.length - 1; i++) {
      node = descend(node, parts[i]!, path);
    }
    const last = parts[parts.length - 1]!;
    if (Array.isArray(node)) {
      const idx = node.findIndex((x: { id?: string }) => x?.id === last);
      if (idx < 0) throw new Error(`Override path ${path}: no element with id "${last}"`);
      node[idx] = value;
    } else if (node && typeof node === 'object') {
      if (!(last in (node as Record<string, unknown>))) throw new Error(`Override path ${path}: unknown key "${last}"`);
      (node as Record<string, unknown>)[last] = value;
    } else throw new Error(`Override path ${path} does not lead to an object`);
  }
  return out;
}

function descend(node: unknown, key: string, path: string): unknown {
  if (Array.isArray(node)) {
    const found = node.find((x: { id?: string }) => x?.id === key) ?? (/^\d+$/.test(key) ? node[Number(key)] : undefined);
    if (found === undefined) throw new Error(`Override path ${path}: no element "${key}"`);
    return found;
  }
  if (node && typeof node === 'object' && key in (node as Record<string, unknown>)) return (node as Record<string, unknown>)[key];
  throw new Error(`Override path ${path}: unknown key "${key}"`);
}
