import fs from 'node:fs';
import path from 'node:path';
import react from '@vitejs/plugin-react';
import { defineConfig, type Plugin } from 'vite';
import { telemetryDir, telemetryFile } from './src/dev/telemetryPath';

/**
 * Dev-server endpoint that saves each fight's telemetry (replay, summary, events) under telemetry/,
 * so a playtest session leaves one folder per attempt without anyone clicking download.
 */
function telemetrySink(): Plugin {
  return {
    name: 'lab-escape-telemetry',
    configureServer(server) {
      server.middlewares.use('/api/telemetry', (req, res) => {
        if (req.method !== 'POST') {
          res.statusCode = 405;
          res.end();
          return;
        }
        let body = '';
        req.on('data', (chunk) => (body += chunk));
        req.on('end', () => {
          try {
            const { name, files } = JSON.parse(body) as { name: unknown; files: unknown };
            // The names come from the request, so every path is checked to stay under telemetry/ before anything is written.
            const dir = telemetryDir(path.resolve('telemetry'), name);
            const isMap = files !== null && typeof files === 'object' && !Array.isArray(files);
            const targets = dir === null || !isMap ? null : Object.entries(files).map(([file, content]) => ({ target: telemetryFile(dir, file), content }));
            if (dir === null || targets === null || targets.some((t) => t.target === null || typeof t.content !== 'string')) {
              res.statusCode = 400;
              res.end('Bad telemetry path or payload');
              return;
            }
            fs.mkdirSync(dir, { recursive: true });
            for (const t of targets) fs.writeFileSync(t.target!, t.content as string);
            res.setHeader('content-type', 'application/json');
            res.end(JSON.stringify({ ok: true, dir: path.relative(process.cwd(), dir) }));
          } catch (e) {
            res.statusCode = 400;
            res.end(String(e));
          }
        });
      });
    },
  };
}

export default defineConfig({
  plugins: [react(), telemetrySink()],
  server: { port: 5173 },
});
