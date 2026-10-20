import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { boltPoints, effectsFor, frameMood } from '../src/client/art/fx';
import { hasIcon } from '../src/client/art/icons';
import { MAX_SOUNDS_PER_FRAME, sfxForEvents } from '../src/client/sfx';
import type { GameEvent, SourceRef } from '../src/state/types';
import { C, run, scenario } from './helpers';

const svg = (events: GameEvent[], s = scenario()) => renderToStaticMarkup(<svg>{effectsFor(events, s)}</svg>);

const src = (side: SourceRef['side'], via: string, def = 'guard'): SourceRef => ({ side, category: side === 'hazard' ? 'hazard' : side === 'enemy' ? 'enemy' : 'player', kind: side === 'hazard' ? 'hazard' : 'unit', id: 1, def, via });

describe('sound effects for a frame', () => {
  it('plays a shot and a hit for the Sidearm', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B6' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B6' });
    expect(sfxForEvents(r.events)).toEqual(expect.arrayContaining(['shot', 'hit']));
  });

  it('tells shots apart by who fired them', () => {
    const shot = (source: SourceRef): GameEvent => ({ t: 'ProjectileFired', source, from: { x: 0, y: 0 }, path: [{ x: 1, y: 0 }], hit: null, hitPos: null });
    expect(sfxForEvents([shot(src('enemy', 'guard_shot'))])).toEqual(['enemyShot']);
    expect(sfxForEvents([shot(src('hazard', 'wall_gun', 'gun'))])).toEqual(['gunFire']);
    expect(sfxForEvents([shot(src('player', 'acid_spit', 'player'))])).toEqual(['acid']);
    expect(sfxForEvents([shot(src('player', 'sidearm', 'player'))])).toEqual(['shot']);
  });

  it('plays the hurt sound, not the hit sound, when the player takes damage', () => {
    const s = scenario({ player: 'B8', playerHp: 8, units: [{ def: 'guard', at: 'B5' }] });
    const r = run(s, { type: 'endTurn' });
    expect(r.events.some((e) => e.t === 'DamageDealt' && e.target.kind === 'unit' && e.target.id === 0)).toBe(true);
    expect(sfxForEvents(r.events)).toContain('hurt');
    expect(sfxForEvents(r.events)).not.toContain('hit');
  });

  it('makes no sound when a hazard primes: its cue stays visual only', () => {
    expect(sfxForEvents([{ t: 'HazardPrimed', hazard: 'gun', step: 2, reason: 'test', victims: [] }])).toEqual([]);
  });

  it('caps a frame at a few sounds, most important first', () => {
    const many: GameEvent[] = [
      { t: 'UnitMoved', id: 1, from: { x: 0, y: 0 }, to: { x: 0, y: 1 }, path: [{ x: 0, y: 1 }], mode: 'walk' as never },
      { t: 'IntercomLine', line: 'x', text: 'x' },
      { t: 'ChainArc', from: { x: 0, y: 0 }, to: { x: 1, y: 1 }, source: src('player', 'drone_zap', 'drone') },
      { t: 'AreaEffect', kind: 'gas', tiles: [], source: src('hazard', 'gas', 'vent_b5') },
      { t: 'AreaEffect', kind: 'blast', tiles: [], source: src('player', 'proximity_mine', 'mine') },
      { t: 'FightEnded', result: 'win', cause: 'All enemies are dead', round: 9 },
    ];
    const sounds = sfxForEvents(many);
    expect(sounds).toHaveLength(MAX_SOUNDS_PER_FRAME);
    expect(sounds[0]).toBe('win');
    expect(sounds).toContain('blast');
    expect(sounds).not.toContain('step');
  });
});

describe('board reactions to a frame', () => {
  it('shakes hard for blasts and charges, lightly when the player is hurt', () => {
    expect(frameMood([{ t: 'AreaEffect', kind: 'blast', tiles: [], source: src('player', 'proximity_mine', 'mine') }]).shake).toBe('heavy');
    expect(frameMood([{ t: 'Charged', id: 3, from: { x: 0, y: 0 }, to: { x: 0, y: 3 }, dir: { x: 0, y: 1 }, hit: null }]).shake).toBe('heavy');
    const hurt = frameMood([
      { t: 'DamageDealt', target: { kind: 'unit', id: 0 }, targetDef: 'player', targetSide: 'player', pos: { x: 1, y: 7 }, source: src('enemy', 'guard_shot'), damageType: 'kinetic', raw: 3, absorbed: 0, amount: 3, hpLost: 3, hpAfter: 5, killed: false },
    ]);
    expect(hurt).toEqual({ shake: 'light', alarm: false, hurt: true });
  });

  it('sounds the alarm when a hazard fires or a later wave arrives, but not for the first wave', () => {
    expect(frameMood([{ t: 'HazardFired', hazard: 'gun', victims: [], tiles: [] }]).alarm).toBe(true);
    expect(frameMood([{ t: 'WaveSpawned', wave: 1, units: [] }]).alarm).toBe(false);
    expect(frameMood([{ t: 'WaveSpawned', wave: 2, units: [] }]).alarm).toBe(true);
  });
});

describe('combat effects', () => {
  it('draws a tracer and a damage number for a shot that hits', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B6' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B6' });
    const out = svg(r.events, r.state);
    expect(out).toContain('fx-tracer');
    expect(out).toContain('fx-float');
    expect(out).toContain(`-${C.abilities['sidearm']!.damage}`);
  });

  it('draws a fireball on every tile of a blast', () => {
    const tiles = [
      { x: 2, y: 2 },
      { x: 3, y: 2 },
      { x: 2, y: 3 },
    ];
    const out = svg([{ t: 'AreaEffect', kind: 'blast', tiles, source: src('player', 'proximity_mine', 'mine') }]);
    expect(out.match(/fx-boom/g)).toHaveLength(tiles.length);
  });

  it('draws nothing for events that have no picture', () => {
    expect(svg([{ t: 'AIDecision', id: 1, def: 'guard', summary: 'x' }])).toBe('<svg></svg>');
  });

  it('draws the same bolt for the same pair of tiles, ending on their centres', () => {
    const a = { x: 10, y: 20 };
    const b = { x: 90, y: 60 };
    expect(boltPoints(a, b, 7)).toBe(boltPoints(a, b, 7));
    const pts = boltPoints(a, b, 7).split(' ');
    expect(pts[0]).toBe('10.0,20.0');
    expect(pts[pts.length - 1]).toBe('90.0,60.0');
  });
});

describe('icons', () => {
  it('has an icon for every ability, utility and status the action bar and badges show', () => {
    const ids = [...C.abilityOrder, ...Object.keys(C.utilities), 'poison', 'parasite', 'corrode', 'pinned', 'slowed', ...C.presets.map((p) => `kit-${p.id}`)];
    expect(ids.filter((id) => !hasIcon(id))).toEqual([]);
  });
});
