import React from 'react';
import { GitFork, ShieldCheck, Zap, Sliders, CheckCircle2, RotateCcw } from 'lucide-react';

export const RoutingView: React.FC = () => {
  const [cacheEnabled, setCacheEnabled] = React.useState(true);
  const [timeoutMs, setTimeoutMs] = React.useState(1500);

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="pb-4 border-b border-zinc-800/80">
        <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
          Routing & Failover Sozlamalari
        </h1>
        <p className="text-xs text-zinc-500 mt-0.5">
          Provayderlar uzilishi yoki sekinlashishining oldini oluvchi avtomatik zaxira qoidalari
        </p>
      </div>

      {/* Failover Pipeline Visualizer */}
      <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-4">
        <div>
          <h3 className="text-sm font-semibold text-zinc-200">Avtomatik Model Failover Zanjiri</h3>
          <p className="text-xs text-zinc-500 mt-0.5">
            Asosiy provayder xato bersa (5xx) yoki belgilangan vaqt ichida javob bermasa, zaxiradagi modelga o'tadi
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          {/* Step 1 */}
          <div className="p-3.5 rounded-lg bg-zinc-950 border border-zinc-800 space-y-2">
            <span className="text-[10px] font-mono text-zinc-400 uppercase tracking-wider block">
              1. Asosiy (Primary)
            </span>
            <div className="font-semibold text-zinc-100 text-sm">OpenAI GPT-4o</div>
            <div className="text-[11px] text-zinc-500 font-mono">Timeout: {timeoutMs}ms • 3x retry</div>
            <div className="pt-2 border-t border-zinc-900 flex items-center space-x-1.5 text-[11px] text-emerald-400">
              <CheckCircle2 className="w-3.5 h-3.5" />
              <span>Prioritet 1</span>
            </div>
          </div>

          {/* Step 2 */}
          <div className="p-3.5 rounded-lg bg-zinc-950 border border-zinc-800 space-y-2">
            <span className="text-[10px] font-mono text-zinc-400 uppercase tracking-wider block">
              2. Zaxira (Fallback 1)
            </span>
            <div className="font-semibold text-zinc-100 text-sm">Claude 3.5 Sonnet</div>
            <div className="text-[11px] text-zinc-500 font-mono">Timeout: 2000ms • Region EU</div>
            <div className="pt-2 border-t border-zinc-900 flex items-center space-x-1.5 text-[11px] text-zinc-300">
              <ShieldCheck className="w-3.5 h-3.5 text-zinc-400" />
              <span>Avto-o'tish</span>
            </div>
          </div>

          {/* Step 3 */}
          <div className="p-3.5 rounded-lg bg-zinc-950 border border-zinc-800 space-y-2">
            <span className="text-[10px] font-mono text-zinc-400 uppercase tracking-wider block">
              3. Favqulodda (Emergency)
            </span>
            <div className="font-semibold text-zinc-100 text-sm">DeepSeek-V3</div>
            <div className="text-[11px] text-zinc-500 font-mono">Yuqori o'tkazuvchanlik</div>
            <div className="pt-2 border-t border-zinc-900 flex items-center space-x-1.5 text-[11px] text-zinc-400">
              <RotateCcw className="w-3.5 h-3.5 text-zinc-500" />
              <span>Yakuniy himoya</span>
            </div>
          </div>
        </div>
      </div>

      {/* Rules and Switches */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Smart Response Caching */}
        <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between mb-2">
              <div className="flex items-center space-x-2">
                <Zap className="w-4 h-4 text-zinc-400" />
                <h4 className="text-sm font-semibold text-zinc-200">Smart Response Caching</h4>
              </div>
              <input
                type="checkbox"
                checked={cacheEnabled}
                onChange={(e) => setCacheEnabled(e.target.checked)}
                className="w-4 h-4 accent-zinc-200 cursor-pointer"
              />
            </div>
            <p className="text-xs text-zinc-400 leading-relaxed">
              Bir xil prompt va parametrlarga ega so'rovlar Redis keshidan 5ms ichida qaytariladi. Bu provayder narxini tejaydi va tezlikni oshiradi.
            </p>
          </div>

          <div className="mt-4 pt-2.5 border-t border-zinc-800/80 text-[11px] text-zinc-500 flex justify-between font-mono">
            <span>Kesh muddati (TTL):</span>
            <span className="text-zinc-300">24 soat</span>
          </div>
        </div>

        {/* Latency Threshold */}
        <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between mb-2">
              <div className="flex items-center space-x-2">
                <Sliders className="w-4 h-4 text-zinc-400" />
                <h4 className="text-sm font-semibold text-zinc-200">Kutish Vaqti (Timeout)</h4>
              </div>
              <span className="font-mono text-xs text-zinc-200 font-bold">{timeoutMs} ms</span>
            </div>
            <p className="text-xs text-zinc-400 leading-relaxed mb-3">
              Provayder ushbu vaqt ichida birinchi tokenni qaytarmasa, so'rov zaxira modelga yo'naltiriladi.
            </p>
            <input
              type="range"
              min="500"
              max="5000"
              step="100"
              value={timeoutMs}
              onChange={(e) => setTimeoutMs(Number(e.target.value))}
              className="w-full accent-white cursor-pointer"
            />
          </div>

          <div className="mt-4 pt-2.5 border-t border-zinc-800/80 text-[11px] text-zinc-500 flex justify-between font-mono">
            <span>Tavsiya etiladi:</span>
            <span className="text-zinc-300">1200ms – 2000ms</span>
          </div>
        </div>
      </div>
    </div>
  );
};
