import { describe, expect, it } from 'vitest';
import { ACTION_KEYS, actionIndexForKey, actionKeyLabel, endsTurn } from '../src/client/actionKeys';
import { actionStatuses } from '../src/engine/commands';
import { C, scenario } from './helpers';

describe('action shortcuts', () => {
  it('keep 1 to 9 and 0 for the first ten actions', () => {
    expect(['1', '2', '3', '4', '5', '6', '7', '8', '9', '0'].map(actionIndexForKey)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
  });

  it('give the eleventh and twelfth actions keys of their own', () => {
    expect(actionIndexForKey('-')).toBe(10);
    expect(actionIndexForKey('=')).toBe(11);
  });

  it('do not claim keys used by other controls', () => {
    for (const k of ['e', 'E', 'z', 'f', 'i', 'l', 'n', ' ', 'Tab', 'Enter', 'Escape', '`', '']) expect(actionIndexForKey(k)).toBe(-1);
  });

  it('label each slot with the key that selects it, and leave later slots blank', () => {
    ACTION_KEYS.forEach((k, i) => {
      expect(actionKeyLabel(i)).toBe(k);
      expect(actionIndexForKey(k)).toBe(i);
    });
    expect(new Set(ACTION_KEYS).size).toBe(ACTION_KEYS.length);
    expect(actionKeyLabel(ACTION_KEYS.length)).toBe('');
  });

  it('cover every action the bar can show: a full loadout plus every utility', () => {
    expect(C.rules.loadout.abilities + Object.keys(C.utilities).length).toBeLessThanOrEqual(ACTION_KEYS.length);
    for (const p of C.presets) {
      const bar = actionStatuses(C, scenario({ abilities: p.abilities, upgrades: p.upgrades }));
      expect(bar.length).toBeLessThanOrEqual(ACTION_KEYS.length);
    }
  });

  it('reach Redeploy in the Hybrid preset, which has 11 actions', () => {
    const hybrid = C.presets.find((p) => p.id === 'hybrid')!;
    const bar = actionStatuses(C, scenario({ abilities: hybrid.abilities, upgrades: hybrid.upgrades }));
    const i = bar.findIndex((a) => a.id === 'redeploy');
    expect(i).toBe(10);
    expect(actionKeyLabel(i)).toBe('-');
  });
});

describe('ending the turn from the keyboard', () => {
  it('lets a focused button, link or summary keep Enter, so Enter operates it instead of ending the turn', () => {
    for (const tagName of ['BUTTON', 'A', 'SUMMARY']) expect(endsTurn('Enter', { tagName })).toBe(false);
  });

  it('ends the turn on Enter when nothing, or something that is not operated by Enter, has focus', () => {
    expect(endsTurn('Enter', null)).toBe(true);
    expect(endsTurn('Enter', {})).toBe(true);
    for (const tagName of ['BODY', 'DIV', 'svg', 'MAIN']) expect(endsTurn('Enter', { tagName })).toBe(true);
  });

  it('always ends the turn on E, wherever focus is', () => {
    for (const k of ['e', 'E']) {
      expect(endsTurn(k, { tagName: 'BUTTON' })).toBe(true);
      expect(endsTurn(k, null)).toBe(true);
    }
  });

  it('ends the turn on no other key', () => {
    for (const k of [' ', 'Escape', 'z', 'Tab', '1', 'Shift']) expect(endsTurn(k, null)).toBe(false);
  });
});
