import fs from 'node:fs';
import path from 'node:path';
import react from '@vitejs/plugin-react';
import { defineConfig, type Plugin } from 'vite';

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
            const { name, files } = JSON.parse(body) as { name: string; files: Record<string, string> };
            const dir = path.resolve('telemetry', name.replace(/[^\w.-]/g, '_'));
            fs.mkdirSync(dir, { recursive: true });
            for (const [file, content] of Object.entries(files)) fs.writeFileSync(path.join(dir, file.replace(/[^\w.-]/g, '_')), content);
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
