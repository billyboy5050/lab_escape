import { describe, expect, it } from 'vitest';
import { loadoutToRetry, presetIdFor, reconcileLoadout, syncPresetLoadout } from '../src/client/loadoutSync';
import { buildContent, defaultContentFiles } from '../src/content';
import { newGame } from '../src/engine/step';
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

describe('reconcileLoadout', () => {
  // T3 is in no preset, so removing it is a valid content edit that breaks a custom loadout.
  const withoutT3 = () => {
    const f = defaultContentFiles();
    return buildContent({ ...f, upgrades: f.upgrades.filter((u) => u.id !== 'T3_suppressing_fire') });
  };
  const custom = { abilities: ['sidearm', 'auto_turret'], upgrades: ['T3_suppressing_fire'] };

  it('reports a loadout that the reloaded content no longer allows, which a fight could not be built from', () => {
    const c = withoutT3();
    const r = reconcileLoadout(c, custom, null);
    expect(r.problems).toEqual(['Unknown upgrade "T3_suppressing_fire"']);
    // This is why the client checks before it renders a fight.
    expect(() => newGame(c, custom)).toThrow(/Invalid loadout/);
  });

  it('has no problems for a loadout that still fits', () => {
    expect(reconcileLoadout(withoutT3(), { abilities: ['sidearm', 'auto_turret'], upgrades: [] }, null).problems).toEqual([]);
    expect(reconcileLoadout(C, custom, null).problems).toEqual([]);
  });

  it('checks the loadout after the selected preset has been synced to its edited picks', () => {
    const c = contentWith({ 'presets.tech.abilities': ['sidearm', 'lunge'], 'presets.tech.upgrades': [] });
    const r = reconcileLoadout(c, techLoadout(), 'tech');
    expect(r.loadout).toEqual({ abilities: ['sidearm', 'lunge'], upgrades: [] });
    expect(r.problems).toEqual([]);
  });
});

describe('presetIdFor', () => {
  it('finds the preset with exactly these picks', () => {
    expect(presetIdFor(C, techLoadout())).toBe('tech');
  });

  it('is null for a custom loadout, even one that only differs by an upgrade', () => {
    expect(presetIdFor(C, { abilities: [...tech.abilities], upgrades: [] })).toBeNull();
    expect(presetIdFor(C, { abilities: ['sidearm'], upgrades: [] })).toBeNull();
  });
});

describe('loadoutToRetry', () => {
  const hybrid = C.presets.find((p) => p.id === 'hybrid')!;

  it('keeps the current loadout and label when the finished session played it', () => {
    const current = techLoadout();
    const r = loadoutToRetry(C, techLoadout(), current, 'tech');
    expect(r.loadout).toBe(current);
    expect(r.presetId).toBe('tech');
  });

  it('takes the loadout a replay played when it differs from the one selected before the import', () => {
    const current = techLoadout();
    const replayed = { abilities: [...hybrid.abilities], upgrades: [...hybrid.upgrades] };
    const r = loadoutToRetry(C, replayed, current, 'tech');
    expect(r.loadout).toEqual(replayed);
    expect(r.loadout).not.toBe(replayed); // a copy, so later edits cannot reach the session's record
    expect(r.presetId).toBe('hybrid');
  });

  it('drops the label when the replayed loadout is not one of the presets', () => {
    const replayed = { abilities: ['sidearm', 'auto_turret'], upgrades: ['T3_suppressing_fire'] };
    expect(loadoutToRetry(C, replayed, techLoadout(), 'tech')).toEqual({ loadout: replayed, presetId: null });
  });
});
