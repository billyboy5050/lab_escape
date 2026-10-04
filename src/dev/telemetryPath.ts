import path from 'node:path';

/**
 * Where the dev server may write a fight's telemetry. The folder and file names come from an HTTP request, so they are
 * confined to `root`: separators and odd characters become underscores, and a name made only of dots ("." or "..", which
 * the character filter alone lets through) is refused. A null return means refuse the request.
 */
function safeName(raw: unknown): string | null {
  if (typeof raw !== 'string') return null;
  const name = raw.replace(/[^\w.-]/g, '_');
  return name === '' || /^\.+$/.test(name) ? null : name;
}

/** True when `child` is strictly below `parent`. A name that merely starts with dots ("..x") is below it; the segment ".." is not. */
function inside(parent: string, child: string): boolean {
  const rel = path.relative(parent, child);
  return rel !== '' && rel !== '..' && !rel.startsWith(`..${path.sep}`) && !path.isAbsolute(rel);
}

/** The folder for one fight under `root`, or null if the name would put it anywhere else. */
export function telemetryDir(root: string, name: unknown): string | null {
  const safe = safeName(name);
  if (safe === null) return null;
  const dir = path.resolve(root, safe);
  return inside(path.resolve(root), dir) ? dir : null;
}

/** The path of one file inside a fight's folder, or null if the name would put it anywhere else. */
export function telemetryFile(dir: string, file: unknown): string | null {
  const safe = safeName(file);
  if (safe === null) return null;
  const target = path.join(dir, safe);
  return inside(dir, target) ? target : null;
}
