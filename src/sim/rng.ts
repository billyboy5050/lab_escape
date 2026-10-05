/** Seeded 32-bit generator (mulberry32). Integer-only output, so every platform draws the same sequence. */
export class Rng {
  private a: number;

  constructor(seed: number) {
    this.a = seed >>> 0;
  }

  nextU32(): number {
    this.a = (this.a + 0x6d2b79f5) >>> 0;
    let t = this.a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return (t ^ (t >>> 14)) >>> 0;
  }

  /** Integer in [0, n). */
  int(n: number): number {
    if (n <= 0) throw new Error('Rng.int needs n > 0');
    return this.nextU32() % n;
  }

  /** True with probability num/den. */
  chance(num: number, den: number): boolean {
    return this.int(den) < num;
  }

  pick<T>(items: readonly T[]): T {
    return items[this.int(items.length)]!;
  }

  shuffle<T>(items: T[]): T[] {
    for (let i = items.length - 1; i > 0; i--) {
      const j = this.int(i + 1);
      [items[i], items[j]] = [items[j]!, items[i]!];
    }
    return items;
  }
}
