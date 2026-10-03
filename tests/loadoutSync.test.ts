import { describe, expect, it } from 'vitest';
import { syncPresetLoadout } from '../src/client/loadoutSync';
import { C, contentWith } from './helpers';

const tech = C.presets.find((p) => p.id === 'tech')!;
const techLoadout = () => ({ abilities: [...tech.abilities], upgrades: [...tech.upgrades] });

describe('syncPresetLoadout', () => {
  it('leaves a loadout alone when no preset is selected', () => {
    const l = { abilities: ['sidearm'], upgrades: [] };
    expect(syncPresetLoadout(C, l, null)).toEqual({ loadout: l, presetId: null });
  });

  it('returns the same object when the selected preset has not changed', () => {
    const l = techLoadout();
    const r = syncPresetLoadout(C, l, 'tech');
    expect(r.loadout).toBe(l);
    expect(r.presetId).toBe('tech');
  });

  it('takes the new picks when the selected preset was edited', () => {
    const c = contentWith({ 'presets.tech.abilities': ['sidearm', 'lunge'], 'presets.tech.upgrades': [] });
    const r = syncPresetLoadout(c, techLoadout(), 'tech');
    expect(r).toEqual({ loadout: { abilities: ['sidearm', 'lunge'], upgrades: [] }, presetId: 'tech' });
  });

  it('drops the label, and keeps the loadout, when the selected preset is gone', () => {
    const l = techLoadout();
    const r = syncPresetLoadout(C, l, 'no_such_preset');
    expect(r.presetId).toBeNull();
    expect(r.loadout).toBe(l);
  });
});
