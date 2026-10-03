// Tile addressing: columns A.. left to right, rows 1.. top to bottom. "D5" is column D (x=3), row 5 (y=4).

export interface Pos { x: number; y: number }

export function tileName(p: Pos): string {
  return String.fromCharCode(65 + p.x) + String(p.y + 1);
}

export function parseTile(name: string): Pos {
  const m = /^([A-Za-z])(\d{1,2})$/.exec(name.trim());
  if (!m) throw new Error(`Bad tile name "${name}"`);
  return { x: m[1]!.toUpperCase().charCodeAt(0) - 65, y: Number(m[2]) - 1 };
}

export function isTileName(name: unknown): name is string {
  return typeof name === 'string' && /^[A-Za-z]\d{1,2}$/.test(name.trim());
}
