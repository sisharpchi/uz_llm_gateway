import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export function parsePrometheus(text, instanceIds) {
  const active = Object.fromEntries(instanceIds.map(id => [id, 0]));
  const ttft = Object.fromEntries(instanceIds.map(id => [id, { sum: 0, count: 0 }]));
  const seen = new Set();
  for (const line of text.split('\n')) {
    const match = /^(uzllm_gateway_active_streams|uzllm_gateway_ttft_ms_(sum|count))\{([^}]*)\}\s+([\d.eE+-]+)(?:\s|$)/.exec(line);
    if (!match) continue;
    const instance = /(?:^|,)service_instance_id="([^"]+)"/.exec(match[3])?.[1];
    if (!instanceIds.includes(instance)) continue;
    const value = Number(match[4]);
    if (!Number.isFinite(value)) throw new Error(`Invalid Prometheus sample for ${instance}`);
    seen.add(instance);
    if (match[1] === 'uzllm_gateway_active_streams') active[instance] += value;
    else ttft[instance][match[2]] += value;
  }
  return { active, ttft, seen: [...seen] };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const [url, secondsRaw, instanceA, instanceB, output] = process.argv.slice(2);
  const seconds = Number(secondsRaw);
  if (!url || !/^https?:\/\//.test(url) || !Number.isInteger(seconds)
    || seconds < 1 || seconds > 180 || !instanceA || !instanceB || instanceA === instanceB || !output) {
    process.stderr.write('Usage: node collect-metrics.mjs URL SECONDS INSTANCE_A INSTANCE_B output.json\n');
    process.exitCode = 2;
  } else {
    const samples = [];
    let ttft = null;
    const ids = [instanceA, instanceB];
    for (let i = 0; i < seconds; i++) {
      try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`collector returned ${response.status}`);
        const parsed = parsePrometheus(await response.text(), ids);
        if (parsed.seen.length === 2) {
          samples.push({ at: new Date().toISOString(), ...parsed.active });
          ttft = parsed.ttft;
        }
      } catch (error) { process.stderr.write(`Collector sample ${i}: ${error}\n`); }
      if (i + 1 < seconds) await new Promise(resolve => setTimeout(resolve, 1000));
    }
    writeFileSync(output, JSON.stringify({ samples, ttft }, null, 2));
    if (samples.length === 0) process.exitCode = 1;
  }
}
