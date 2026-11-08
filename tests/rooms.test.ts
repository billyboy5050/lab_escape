import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { buildContent, defaultContentFiles, roomFor, validateContent, type ContentFiles, type RoomFile } from '../src/content';
import { legalCommands } from '../src/engine/commands';
import { newGame, step } from '../src/engine/step';
import { computeIntents, previewCommand } from '../src/preview/preview';
import { contentOfBatch, runBatch, seedRange } from '../src/sim/batch';
import { GreedyBot, RandomBot } from '../src/sim/bots';
import { Rng } from '../src/sim/rng';
import { runFight } from '../src/sim/runner';
import { boardFor } from '../src/state/grid';
import type { GameState, Loadout } from '../src/state/types';
import { checkCompatible, recordProblems, replay, ReplayMismatch } from '../src/telemetry/record';
import { parseTile } from '../src/util/tiles';
import { BOT_FIGHT_TIMEOUT_MS, C, contentWith } from './helpers';

const ROOMS = JSON.parse(fs.readFileSync(path.join('golden', 'rooms.json'), 'utf8')) as RoomFile[];
const R = buildContent({ ...defaultContentFiles(), rooms: ROOMS });
const HYBRID: Loadout = { abilities: C.presets.find((p) => p.id === 'hybrid')!.abilities, upgrades: C.presets.find((p) => p.id === 'hybrid')!.upgrades };
const room = (id: string): RoomFile => structuredClone(ROOMS.find((r) => r.id === id)!);

/** A one-row room: the player and a Guard either side of a wall, so nothing can happen until the round cap. */
function sealedRoom(roundCap: number): RoomFile {
  return { id: 'sealed', name: 'Sealed', width: 3, height: 1, rows: ['@#.'], playerStart: 'A1', hazards: [], waves: [{ round: 1, units: [{ def: 'guard', tile: 'C1' }] }], roundCap };
}

function armoryAsRoom(files: ContentFiles): RoomFile {
  const { legend: _legend, terrain: _terrain, ...plan } = files.map;
  return { ...structuredClone(plan), id: 'armory_copy', waves: structuredClone(files.waves), roundCap: files.rules.roundCap };
}

describe('the Armory as a room', () => {
  it('is the default room, built from map.json, waves.json and rules.roundCap', () => {
    expect(roomFor(C, undefined)).toBe(C.defaultRoom);
    expect(roomFor(C, C.map.id)).toBe(C.defaultRoom);
    expect(C.defaultRoom).toEqual({ ...C.files.map, waves: C.files.waves, roundCap: C.files.rules.roundCap });
    expect(Object.keys(C.rooms)).toEqual([C.map.id]);
  });

  it('follows overrides of the files it is built from', () => {
    expect(contentWith({ 'rules.roundCap': 9 }).defaultRoom.roundCap).toBe(9);
    expect(contentWith({ 'map.playerStart': 'A8' }).defaultRoom.playerStart).toBe('A8');
  });

  it('keeps its content hash when no rooms are added, and any added room changes the hash', () => {
    const golden = JSON.parse(fs.readFileSync(path.join('golden', 'hybrid.json'), 'utf8')) as { record: { contentHash: string } };
    expect(C.hash).toBe(golden.record.contentHash);
    expect(buildContent(defaultContentFiles()).hash).toBe(C.hash);
    expect(R.hash).not.toBe(C.hash);
    expect(buildContent({ ...defaultContentFiles(), rooms: ROOMS.slice(1) }).hash).not.toBe(R.hash);
  });

  it('plays exactly as the same room written in the new format', () => {
    const files = defaultContentFiles();
    const copy = buildContent({ ...files, rooms: [armoryAsRoom(files)] });
    const settings = { room: 'armory_copy' };
    for (const seed of [1, 2]) {
      const a = runFight(C, HYBRID, new GreedyBot(seed));
      const b = runFight(copy, HYBRID, new GreedyBot(seed), { settings });
      expect(b.record.commands).toEqual(a.record.commands);
      expect(replay(copy, b.record).events).toEqual(replay(C, a.record).events);
    }
    for (let seed = 1; seed <= 20; seed++) {
      const a = runFight(C, HYBRID, new RandomBot(seed));
      const b = runFight(copy, HYBRID, new RandomBot(seed), { settings });
      expect(replay(copy, b.record).events).toEqual(replay(C, a.record).events);
    }
  }, BOT_FIGHT_TIMEOUT_MS);

  it('stays the default when a fight names no room, even with other rooms loaded', () => {
    expect(newGame(R, HYBRID).state.units[0]!.pos).toEqual(parseTile(C.map.playerStart));
    expect(newGame(R, HYBRID).state.hazards.map((h) => h.id)).toEqual(C.map.hazards.map((h) => h.id));
  });
});

describe('rooms of other sizes', () => {
  it('load with their own size, and add to the palette in map.json', () => {
    expect(ROOMS.map((r) => `${R.rooms[r.id]!.width}x${R.rooms[r.id]!.height}`)).toEqual(['6x5', '12x12', '14x8']);
    const hallways = R.rooms['hallways']!;
    expect(hallways.legend['_']).toBe('void');
    expect(hallways.legend['#']).toBe('wall');
    expect(hallways.terrain['wall']).toEqual(C.map.terrain['wall']);
    // A room's additions do not leak into the Armory's palette.
    expect(R.defaultRoom.legend['_']).toBeUndefined();
  });

  it('are refused if unknown, rather than falling back to the Armory', () => {
    expect(() => newGame(R, HYBRID, { room: 'nowhere' })).toThrow('Unknown room "nowhere"');
    expect(() => newGame(R, HYBRID, { room: 'constructor' })).toThrow('Unknown room "constructor"');
  });

  it('start the fight from the room: player tile, hazards and first wave', () => {
    const { state } = newGame(R, HYBRID, { room: 'hall' });
    expect(state.units[0]!.pos).toEqual(parseTile('F12'));
    expect(state.hazards.map((h) => h.id)).toEqual(['gun', 'panel', 'vent_e5', 'vent_h5', 'vent_c10', 'vent_j10']);
    expect(state.units.filter((u) => u.team === 'enemy').map((u) => u.def)).toEqual(['guard', 'guard', 'guard', 'medic']);
    expect(state.units.find((u) => u.def === 'medic')!.pos).toEqual(parseTile('F2'));
  });

  it('use their own bounds', () => {
    const closet = boardFor(R.rooms['closet']!);
    const hall = boardFor(R.rooms['hall']!);
    expect(closet.inBounds(parseTile('F5'))).toBe(true);
    expect(closet.inBounds(parseTile('G5'))).toBe(false);
    expect(closet.inBounds(parseTile('F6'))).toBe(false);
    expect(hall.inBounds(parseTile('L12'))).toBe(true);
    const moved = (id: string, to: string) => legalCommands(R, newGame(R, HYBRID, { room: id }).state).some((c) => c.type === 'move' && c.path[c.path.length - 1] === to);
    expect(moved('hall', 'F10')).toBe(true);
    expect(moved('closet', 'B5')).toBe(false);
  });

  it('can be non-rectangular: void tiles block movement, sight and shots', () => {
    const b = boardFor(R.rooms['hallways']!);
    const voidTile = parseTile('A1');
    expect(b.terrainId(voidTile)).toBe('void');
    expect([b.blocksMove(voidTile), b.blocksSight(voidTile), b.blocksProjectile(voidTile)]).toEqual([true, true, true]);
    expect(b.hasLOS(parseTile('H3'), parseTile('K3'))).toBe(false);
  });

  it('end at their own round cap', () => {
    const c = buildContent({ ...defaultContentFiles(), rooms: [sealedRoom(3)] });
    let state: GameState = newGame(c, HYBRID, { room: 'sealed' }).state;
    for (let round = 1; round <= 3; round++) {
      expect(state.outcome).toBeNull();
      state = step(c, state, { type: 'endTurn' }).state;
    }
    expect(state.outcome).toEqual({ result: 'lose', cause: 'Round 3 ended with the fight unresolved', round: 3 });
  });

  it('bring their own waves, in fights and in the debug command', () => {
    const c = buildContent({ ...defaultContentFiles(), rooms: [sealedRoom(9)] });
    const { state } = newGame(c, HYBRID, { room: 'sealed' });
    expect(state.wavesSpawned).toBe(1);
    expect(step(c, state, { type: 'debug', op: 'forceWave' }).error).toBe('No waves left');
    expect(step(C, newGame(C, HYBRID).state, { type: 'debug', op: 'forceWave' }).ok).toBe(true);
  });

  for (const r of ROOMS) {
    it(`${r.id}: bots finish fights headlessly, reproducibly, on every preset`, () => {
      for (const p of C.presets) {
        const spec = { bot: 'random' as const, loadout: { preset: p.id }, seeds: seedRange(1, 30), rooms: ROOMS, settings: { room: r.id } };
        const rows = runBatch(spec);
        expect(rows.filter((f) => f.outcome === 'error').map((f) => f.error)).toEqual([]);
        expect(rows).toEqual(runBatch(spec));
      }
      const greedy = runBatch({ bot: 'greedy', loadout: { preset: 'hybrid' }, seeds: seedRange(1, 2), rooms: ROOMS, settings: { room: r.id } });
      expect(greedy.filter((f) => f.outcome === 'error').map((f) => f.error)).toEqual([]);
    }, BOT_FIGHT_TIMEOUT_MS);

    it(`${r.id}: previews and intents match results`, () => {
      const rng = new Rng(11);
      const settings = { room: r.id };
      for (let i = 0; i < 40; i++) {
        let s = newGame(R, HYBRID, settings).state;
        for (let n = rng.int(30); n > 0 && !s.outcome; n--) {
          const cmds = legalCommands(R, s);
          s = step(R, s, cmds[rng.int(cmds.length)]!).state;
        }
        if (s.outcome) continue;
        const before = JSON.stringify(s);
        const cmds = legalCommands(R, s);
        const cmd = cmds[rng.int(cmds.length)]!;
        const preview = previewCommand(R, s, cmd);
        const real = step(R, s, cmd);
        expect(preview.events).toEqual(real.events);
        expect(preview.state).toEqual(real.state);
        expect(computeIntents(R, s).frames.length).toBeGreaterThanOrEqual(0);
        expect(JSON.stringify(s)).toBe(before);
      }
    });
  }

  it('the golden fights really do go beyond the Armory: past row 8, past column H', () => {
    const tilesOf = (id: string): string[] => {
      const g = JSON.parse(fs.readFileSync(path.join('golden', `room-${id}.json`), 'utf8')) as { record: { commands: { path?: string[]; target?: string }[]; settings: { room?: string } } };
      expect(g.record.settings.room).toBe(id);
      return g.record.commands.flatMap((c) => [...(c.path ?? []), ...(c.target ? [c.target] : [])]);
    };
    expect(tilesOf('hall').some((t) => parseTile(t).y >= 8)).toBe(true);
    expect(tilesOf('hallways').some((t) => parseTile(t).x >= 8)).toBe(true);
  });

  it('a batch adds its rooms to the content it runs on', () => {
    expect(contentOfBatch({ rooms: ROOMS }).hash).toBe(R.hash);
    expect(contentOfBatch({}).hash).toBe(C.hash);
  });
});

describe('replays in a room', () => {
  const fight = () => runFight(R, HYBRID, new RandomBot(5), { settings: { room: 'closet' } });

  it('carry the room, and replay exactly', () => {
    const r = fight();
    expect(r.record.settings).toEqual({ room: 'closet' });
    expect(replay(R, r.record).state).toEqual(r.state);
  });

  it('are refused by content without that room', () => {
    expect(() => checkCompatible(C, fight().record)).toThrow(ReplayMismatch);
  });

  it('have the room checked, since a replay file is untrusted', () => {
    const r = fight();
    for (const bad of [5, null, ['closet'], { id: 'closet' }]) {
      const rec = { ...r.record, settings: { room: bad } };
      expect(recordProblems(rec)).toEqual([`settings.room must be a room ID (got ${JSON.stringify(bad)})`]);
      expect(() => checkCompatible(R, rec)).toThrow('malformed');
    }
    for (const id of ['nowhere', 'constructor', '__proto__']) {
      expect(() => checkCompatible(R, { ...r.record, settings: { room: id } })).toThrow(`Replay was played in room "${id}", which this content does not have`);
    }
  });
});

describe('room validation', () => {
  const problems = (rooms: unknown) => validateContent({ ...defaultContentFiles(), rooms } as ContentFiles);
  const edit = (id: string, f: (r: RoomFile) => void): string[] => {
    const rooms = structuredClone(ROOMS);
    f(rooms.find((r) => r.id === id)!);
    return problems(rooms);
  };

  it('accepts the golden rooms, a one-tile-wide room and the largest addressable one', () => {
    expect(problems(ROOMS)).toEqual([]);
    expect(problems([sealedRoom(5)])).toEqual([]);
    const huge: RoomFile = { id: 'huge', name: 'Huge', width: 26, height: 99, rows: Array.from({ length: 99 }, () => '.'.repeat(26)), playerStart: 'Z99', hazards: [], waves: [{ round: 1, units: [{ def: 'guard', tile: 'A1' }] }], roundCap: 5 };
    expect(problems([huge])).toEqual([]);
  });

  it('accepts no rooms at all, but not rooms that are not a list', () => {
    expect(problems(undefined)).toEqual([]);
    expect(problems([])).toEqual([]);
    expect(problems(null)).toEqual(['rooms must be a list']);
    expect(problems({ id: 'x' })).toEqual(['rooms must be a list']);
  });

  it('wants rooms with an id of their own', () => {
    expect(problems([{ name: 'x' }])).toEqual(['rooms[0] needs an id']);
    expect(problems([7])).toEqual(['rooms[0] needs an id']);
    expect(problems([room('closet'), room('closet')])).toEqual(['rooms has two rooms with the id "closet"']);
    expect(problems([{ ...room('closet'), id: C.map.id }])).toEqual([`rooms has two rooms with the id "${C.map.id}"`]);
  });

  it.each<[string, string, (r: RoomFile) => void, string[]]>([
    ['round cap', 'closet', (r) => { r.roundCap = 0; }, ['rooms.closet.roundCap must be an integer >= 1 (got 0)']],
    ['missing round cap', 'closet', (r) => { delete (r as Partial<RoomFile>).roundCap; }, ['rooms.closet.roundCap must be an integer >= 1 (got undefined)']],
    ['legend', 'closet', (r) => { (r as { legend: unknown }).legend = 'x'; }, ['rooms.closet.legend must be an object']],
    ['terrain', 'closet', (r) => { (r as { terrain: unknown }).terrain = 3; }, ['rooms.closet.terrain must be an object']],
    ['row count', 'closet', (r) => { r.rows.pop(); }, ['rooms.closet has 4 rows but height 5']],
    ['row length', 'closet', (r) => { r.rows[1] = '.....'; }, ['rooms.closet row 2 has 5 tiles but width 6']],
    ['unknown tile character', 'closet', (r) => { r.rows[1] = '.....Q'; }, ['rooms.closet legend has no entry for "Q"']],
    ['legend pointing nowhere', 'closet', (r) => { r.legend = { Q: 'lava' }; r.rows[1] = '.....Q'; }, ['rooms.closet terrain has no definition for "lava"']],
    ['player start off the map', 'closet', (r) => { r.playerStart = 'G5'; }, ['rooms.closet.playerStart "G5" is off the map']],
    ['player start not a tile', 'closet', (r) => { r.playerStart = 'here'; }, ['rooms.closet.playerStart "here" is not a tile name']],
    ['player start in a wall', 'closet', (r) => { r.playerStart = 'F1'; }, ['rooms.closet.playerStart "F1" is on terrain that blocks movement']],
    ['spawn in the void', 'hallways', (r) => { r.waves[0]!.units[0]!.tile = 'A1'; }, ['rooms.hallways.waves[0] spawn tile "A1" is on terrain that blocks movement']],
    ['spawn off the map', 'closet', (r) => { r.waves[1]!.units[0]!.tile = 'A9'; }, ['rooms.closet.waves[1] spawn tile "A9" is off the map']],
    ['unknown unit', 'closet', (r) => { r.waves[0]!.units[0]!.def = 'dragon'; }, ['rooms.closet.waves[0] uses unknown unit "dragon"']],
    ['wave order', 'closet', (r) => { r.waves[1]!.round = 0; }, ['rooms.closet.waves[1].round must be an integer >= 1 (got 0)', 'rooms.closet.waves[1] arrives before the wave listed above it']],
    ['wave units', 'closet', (r) => { (r.waves[0] as { units: unknown }).units = 'all'; }, ['rooms.closet.waves[0].units must be a list']],
    ['no waves', 'closet', (r) => { r.waves = []; }, ['rooms.closet has no waves']],
    ['hazard def', 'closet', (r) => { r.hazards[1]!.def = 'lava'; }, ['rooms.closet hazard vent uses unknown hazard def "lava"']],
    ['hazard tile', 'closet', (r) => { r.hazards[1]!.tiles = ['H9']; }, ['rooms.closet hazard vent tile "H9" is off the map']],
    ['hazard with no tiles', 'closet', (r) => { r.hazards[1]!.tiles = []; }, ['rooms.closet hazard vent needs at least one tile']],
    ['two hazards with one id', 'closet', (r) => { r.hazards[1]!.id = 'gun'; }, ['rooms.closet has two hazards with the id "gun"']],
    ['hazard with no id', 'closet', (r) => { delete (r.hazards[1] as { id?: string }).id; }, ['rooms.closet has a hazard with no id']],
    ['lane with no direction', 'closet', (r) => { delete r.hazards[0]!.direction; }, ['rooms.closet hazard gun is a lane and needs a direction']],
    ['hazards not a list', 'closet', (r) => { (r as { hazards: unknown }).hazards = {}; }, ['rooms.closet.hazards must be a list']],
    ['terrain flag', 'hallways', (r) => { delete (r.terrain!['void'] as { blocksSight?: boolean }).blocksSight; }, ['rooms.hallways terrain "void" needs blocksSight to be true or false (got undefined)']],
  ])('rejects a bad %s', (_name, id, change, expected) => {
    expect(edit(id, change)).toEqual(expected);
  });

  it('limits a room to what tile names can address', () => {
    const wide: RoomFile = { ...sealedRoom(5), width: 27, rows: ['@#.' + '.'.repeat(24)] };
    expect(problems([wide])).toEqual(['rooms.sealed.width must be at most 26, the columns A to Z (got 27)']);
    const tall: RoomFile = { ...sealedRoom(5), width: 1, height: 100, rows: Array.from({ length: 100 }, () => '.'), playerStart: 'A1', waves: [{ round: 1, units: [{ def: 'guard', tile: 'A2' }] }] };
    expect(problems([tall])).toEqual(['rooms.sealed.height must be at most 99, the rows 1 to 99 (got 100)']);
  });

  it('applies the same checks to the Armory, under its own file names', () => {
    const files = defaultContentFiles();
    files.map.playerStart = 'H2';
    expect(validateContent(files)).toEqual(['map.playerStart "H2" is on terrain that blocks movement']);
    const noRows = defaultContentFiles();
    delete (noRows.map as { rows?: string[] }).rows;
    expect(validateContent(noRows)).toContain('map.rows must be a list');
    const flags = defaultContentFiles();
    delete (flags.map.terrain['wall'] as { blocksMove?: boolean }).blocksMove;
    expect(validateContent(flags)).toEqual(['map terrain "wall" needs blocksMove to be true or false (got undefined)']);
  });

  it('rejects bad rooms when content is built', () => {
    expect(() => buildContent({ ...defaultContentFiles(), rooms: [{ ...room('closet'), roundCap: 0 }] })).toThrow('rooms.closet.roundCap');
  });
});
