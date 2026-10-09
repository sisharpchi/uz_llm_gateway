import { spawnSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const [secondsRaw, intervalRaw, organizationId, output] = process.argv.slice(2);
const seconds = Number(secondsRaw);
const interval = Number(intervalRaw);
if (!Number.isInteger(seconds) || seconds < 20 || seconds > 180
  || !Number.isInteger(interval) || interval < 1 || interval > 5
  || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(organizationId ?? '') || !output
  || !/^uzllm_load_[a-z0-9_]+$/i.test(process.env.UZLLM_LOAD_DB_NAME ?? '')) {
  process.stderr.write('Usage: UZLLM_LOAD_DB_NAME=uzllm_load_* node collect-financial.mjs SECONDS INTERVAL ORG_UUID output.json\n');
  process.exitCode = 2;
} else {
  const sql = join(dirname(fileURLToPath(import.meta.url)), 'financial-snapshot.sql');
  const samples = [];
  const failures = [];
  for (let elapsed = 0; elapsed < seconds; elapsed += interval) {
    const query = spawnSync('psql', ['-X', '-v', 'ON_ERROR_STOP=1', '-v', `org_id=${organizationId}`,
      '-At', '-f', sql], { encoding: 'utf8', timeout: 5000 });
    try {
      if (query.status !== 0) throw new Error(query.stderr || query.error?.message || `exit ${query.status}`);
      const snapshot = JSON.parse(query.stdout.trim());
      if (snapshot.organizationId?.toLowerCase() !== organizationId.toLowerCase())
        throw new Error('Unexpected organization in financial snapshot');
      samples.push({ at: new Date().toISOString(), ...snapshot });
    } catch (error) { failures.push({ at: new Date().toISOString(), message: String(error).slice(0, 200) }); }
    if (elapsed + interval < seconds)
      await new Promise(resolve => setTimeout(resolve, interval * 1000));
  }
  writeFileSync(output, JSON.stringify({ samples, failures }, null, 2));
  if (failures.length || samples.length < 20) process.exitCode = 1;
}
