import React from 'react';
import { Organization, Project, Currency, UsageLogItem } from '../../types';
import {
  TrendingUp,
  Zap,
  Clock,
  ArrowUpRight,
  Plus,
  Key,
  Copy,
  Check,
  CheckCircle2,
  AlertTriangle,
  Cpu,
  Layers,
  Sparkles
} from 'lucide-react';

interface OverviewViewProps {
  currentOrg: Organization;
  currentProject: Project;
  currency: Currency;
  recentLogs: UsageLogItem[];
  onOpenTopUp: () => void;
  onOpenCreateKey: () => void;
  onNavigate: (view: any) => void;
}

export const OverviewView: React.FC<OverviewViewProps> = ({
  currentOrg,
  currentProject,
  currency,
  recentLogs,
  onOpenTopUp,
  onOpenCreateKey,
  onNavigate
}) => {
  const [copiedSnippet, setCopiedSnippet] = React.useState<string | null>(null);
  const [selectedLang, setSelectedLang] = React.useState<'python' | 'node' | 'curl' | 'csharp'>('python');

  const handleCopy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedSnippet(id);
    setTimeout(() => setCopiedSnippet(null), 2000);
  };

  const snippets = {
    python: `from openai import OpenAI

# UZLLM Gateway - barcha provayderlar uchun yagona client
client = OpenAI(
    base_url="https://api.uzllm.uz/v1",
    api_key="uzllm_live_your_secret_key"
)

response = client.chat.completions.create(
    model="claude-3-5-sonnet", # yoki "gpt-4o", "deepseek-chat-v3"
    messages=[{"role": "user", "content": "O'zbekiston AI imkoniyatlari haqida yoz"}]
)

print(response.choices[0].message.content)`,
    node: `import OpenAI from "openai";

const openai = new OpenAI({
  baseURL: "https://api.uzllm.uz/v1",
  apiKey: "uzllm_live_your_secret_key",
});

const response = await openai.chat.completions.create({
  model: "gpt-4o",
  messages: [{ role: "user", content: "Salom, UZLLM Gateway!" }],
});

console.log(response.choices[0].message.content);`,
    curl: `curl https://api.uzllm.uz/v1/chat/completions \\
  -H "Content-Type: application/json" \\
  -H "Authorization: Bearer uzllm_live_your_secret_key" \\
  -d '{
    "model": "deepseek-chat-v3",
    "messages": [{"role": "user", "content": "Assalomu alaykum!"}]
  }'`,
    csharp: `using System.Net.Http.Json;

using var client = new HttpClient { BaseAddress = new Uri("https://api.uzllm.uz/v1/") };
client.DefaultRequestHeaders.Add("Authorization", "Bearer uzllm_live_your_secret_key");

var payload = new {
    model = "claude-3-5-sonnet",
    messages = new[] { new { role = "user", content = "Salom C# .NET 10 dan!" } }
};

var response = await client.PostAsJsonAsync("chat/completions", payload);
Console.WriteLine(await response.Content.ReadAsStringAsync());`
  };

  const daysData = [
    { day: '18 Sen', reqs: 14200, cost: 8.40 },
    { day: '19 Sen', reqs: 16800, cost: 11.20 },
    { day: '20 Sen', reqs: 12100, cost: 7.90 },
    { day: '21 Sen', reqs: 18900, cost: 13.50 },
    { day: '22 Sen', reqs: 21400, cost: 15.80 },
    { day: '23 Sen', reqs: 24300, cost: 18.20 },
    { day: 'Bugun', reqs: 16800, cost: 9.30 },
  ];

  const maxReqs = Math.max(...daysData.map(d => d.reqs));

  return (
    <div className="space-y-6">
      {/* Minimal Header Breadcrumbs & Quick Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-zinc-800/80 gap-3">
        <div>
          <div className="flex items-center space-x-2 text-xs text-zinc-500 mb-1">
            <span>{currentOrg.name}</span>
            <span>/</span>
            <span className="text-zinc-300 font-medium">{currentProject.name}</span>
            <span className="px-1.5 py-0.2 rounded bg-zinc-900 border border-zinc-800 text-[10px] font-mono text-zinc-400">
              {currentProject.environment}
            </span>
          </div>
          <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
            Loyiha Umumiy Tahlili
          </h1>
        </div>

        <div className="flex items-center space-x-2">
          <button
            onClick={onOpenCreateKey}
            className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-zinc-900 hover:bg-zinc-800 text-zinc-200 text-xs font-medium border border-zinc-800 transition cursor-pointer"
          >
            <Key className="w-3.5 h-3.5 text-zinc-400" />
            <span>Yangi Kalit</span>
          </button>
          <button
            onClick={onOpenTopUp}
            className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>Hisobni To'ldirish</span>
          </button>
        </div>
      </div>

      {/* Minimalist KPI Row */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        {/* KPI 1 */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80">
          <div className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5">
            Mavjud Balans
          </div>
          <div className="text-xl font-bold font-mono text-zinc-100">
            {currency === 'USD'
              ? `$${currentOrg.balanceUsd.toFixed(2)}`
              : `${(currentOrg.balanceUzs).toLocaleString()} so'm`}
          </div>
          <div className="text-[11px] text-zinc-500 mt-1 flex items-center space-x-1">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
            <span>Prepaid audit faol</span>
          </div>
        </div>

        {/* KPI 2 */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80">
          <div className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5">
            Oylik Xarajat
          </div>
          <div className="text-xl font-bold font-mono text-zinc-100">
            {currency === 'USD'
              ? `$${currentProject.spendThisMonthUsd.toFixed(2)}`
              : `${(currentProject.spendThisMonthUsd * 13000).toLocaleString()} so'm`}
          </div>
          <div className="text-[11px] text-zinc-500 mt-1">
            <span className="text-zinc-300 font-mono">+12.4%</span> o'tgan haftadan
          </div>
        </div>

        {/* KPI 3 */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80">
          <div className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5">
            So'rovlar (Oy)
          </div>
          <div className="text-xl font-bold font-mono text-zinc-100">
            {currentProject.requestCountThisMonth.toLocaleString()}
          </div>
          <div className="text-[11px] text-zinc-500 mt-1">
            <span className="text-emerald-400 font-mono">99.96%</span> muvaffaqiyatli
          </div>
        </div>

        {/* KPI 4 */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80">
          <div className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5">
            O'rtacha Kechikish
          </div>
          <div className="text-xl font-bold font-mono text-zinc-100">
            312 ms
          </div>
          <div className="text-[11px] text-zinc-500 mt-1">
            Toshkent tranzit tuguni
          </div>
        </div>
      </div>

      {/* Two Column Section: Chart + Models Breakdown */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Left: Minimal Chart */}
        <div className="lg:col-span-2 p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between mb-4">
              <div>
                <h3 className="text-sm font-semibold text-zinc-200">So'rovlar va Xarajat Grafigi</h3>
                <p className="text-[11px] text-zinc-500">Oxirgi 7 kunlik inferensiya oqimi</p>
              </div>
              <div className="flex items-center space-x-3 text-[11px] font-mono text-zinc-400">
                <span className="flex items-center space-x-1.5">
                  <span className="w-2 h-2 rounded-xs bg-zinc-300 inline-block"></span>
                  <span>So'rovlar</span>
                </span>
                <span className="flex items-center space-x-1.5">
                  <span className="w-2 h-2 rounded-xs bg-emerald-400 inline-block"></span>
                  <span>Xarajat ($)</span>
                </span>
              </div>
            </div>

            {/* Clean Minimalist Bar Visualization */}
            <div className="h-44 flex items-end justify-between pt-6 pb-2 px-1 border-b border-zinc-800/80">
              {daysData.map((d, i) => {
                const heightPercent = Math.round((d.reqs / maxReqs) * 100);
                return (
                  <div key={i} className="flex-1 flex flex-col items-center group relative cursor-pointer mx-1">
                    {/* Tooltip */}
                    <div className="absolute -top-11 opacity-0 group-hover:opacity-100 transition-opacity bg-zinc-900 border border-zinc-700 text-zinc-100 text-[10px] rounded px-2 py-1 pointer-events-none whitespace-nowrap shadow-xl z-20 font-mono">
                      <div>{d.reqs.toLocaleString()} reqs</div>
                      <div className="text-emerald-400 font-semibold">${d.cost.toFixed(2)}</div>
                    </div>

                    <div className="w-full max-w-[28px] flex items-end space-x-1 h-32">
                      <div
                        style={{ height: `${heightPercent}%` }}
                        className="w-1/2 bg-zinc-500 rounded-t-xs group-hover:bg-zinc-300 transition"
                      />
                      <div
                        style={{ height: `${Math.round((d.cost / 20) * 100)}%` }}
                        className="w-1/2 bg-emerald-500/80 rounded-t-xs group-hover:bg-emerald-400 transition"
                      />
                    </div>
                    <span className="text-[10px] text-zinc-500 mt-2 font-mono">{d.day}</span>
                  </div>
                );
              })}
            </div>
          </div>

          <div className="pt-3 flex items-center justify-between text-[11px] text-zinc-500 font-mono">
            <span>Jami: 124,500 so'rov</span>
            <span className="text-zinc-300 font-medium">Xarajat: $84.30 (~1,095,900 so'm)</span>
          </div>
        </div>

        {/* Right: Model Distribution Breakdown */}
        <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <h3 className="text-sm font-semibold text-zinc-200 mb-0.5">Model Taqsimoti</h3>
            <p className="text-[11px] text-zinc-500 mb-4">Shu oyda sarflangan tokenlar ulushi</p>

            <div className="space-y-3.5">
              <div>
                <div className="flex justify-between text-[11px] mb-1">
                  <span className="text-zinc-300 font-medium">Claude 3.5 Sonnet</span>
                  <span className="text-zinc-400 font-mono">48% ($40.50)</span>
                </div>
                <div className="h-1.5 rounded-full bg-zinc-800 overflow-hidden">
                  <div className="h-full bg-zinc-200 rounded-full w-[48%]"></div>
                </div>
              </div>

              <div>
                <div className="flex justify-between text-[11px] mb-1">
                  <span className="text-zinc-300 font-medium">GPT-4o Mini</span>
                  <span className="text-zinc-400 font-mono">28% ($23.60)</span>
                </div>
                <div className="h-1.5 rounded-full bg-zinc-800 overflow-hidden">
                  <div className="h-full bg-zinc-400 rounded-full w-[28%]"></div>
                </div>
              </div>

              <div>
                <div className="flex justify-between text-[11px] mb-1">
                  <span className="text-zinc-300 font-medium">DeepSeek-V3</span>
                  <span className="text-zinc-400 font-mono">16% ($13.50)</span>
                </div>
                <div className="h-1.5 rounded-full bg-zinc-800 overflow-hidden">
                  <div className="h-full bg-zinc-500 rounded-full w-[16%]"></div>
                </div>
              </div>

              <div>
                <div className="flex justify-between text-[11px] mb-1">
                  <span className="text-zinc-300 font-medium">Gemini 2.5 Flash</span>
                  <span className="text-zinc-400 font-mono">8% ($6.70)</span>
                </div>
                <div className="h-1.5 rounded-full bg-zinc-800 overflow-hidden">
                  <div className="h-full bg-zinc-600 rounded-full w-[8%]"></div>
                </div>
              </div>
            </div>
          </div>

          <div className="pt-3 border-t border-zinc-800/80 text-xs">
            <button
              onClick={() => onNavigate('models')}
              className="text-zinc-300 hover:text-white font-medium flex items-center space-x-1 cursor-pointer"
            >
              <span>Barcha modellarni ko'rish</span>
              <ArrowUpRight className="w-3.5 h-3.5 text-zinc-500" />
            </button>
          </div>
        </div>
      </div>

      {/* Minimalist Code Integration Widget */}
      <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-3">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
          <div>
            <h3 className="text-sm font-semibold text-zinc-200">
              1 Daqiqada Ulanish (OpenAI SDK bilan 100% mos)
            </h3>
            <p className="text-[11px] text-zinc-500">
              Faqat <code className="text-zinc-300 font-mono">baseURL</code> va UZLLM kalitini berishingiz kifoya.
            </p>
          </div>

          <div className="flex items-center space-x-2">
            <div className="flex p-0.5 rounded-md bg-zinc-950 border border-zinc-800">
              {(['python', 'node', 'curl', 'csharp'] as const).map((lang) => (
                <button
                  key={lang}
                  onClick={() => setSelectedLang(lang)}
                  className={`px-2.5 py-1 rounded text-[11px] font-mono transition cursor-pointer ${
                    selectedLang === lang
                      ? 'bg-zinc-800 text-zinc-100 font-medium'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  {lang === 'python' ? 'Python' : lang === 'node' ? 'Node.js' : lang === 'curl' ? 'cURL' : '.NET / C#'}
                </button>
              ))}
            </div>

            <button
              onClick={() => handleCopy(snippets[selectedLang], selectedLang)}
              className="flex items-center space-x-1 px-2.5 py-1 rounded-md bg-zinc-900 hover:bg-zinc-800 text-zinc-300 text-xs font-medium border border-zinc-800 transition cursor-pointer"
            >
              {copiedSnippet === selectedLang ? (
                <>
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span className="text-emerald-400 font-mono text-[11px]">Nusxalandi</span>
                </>
              ) : (
                <>
                  <Copy className="w-3.5 h-3.5 text-zinc-500" />
                  <span className="font-mono text-[11px]">Nusxalash</span>
                </>
              )}
            </button>
          </div>
        </div>

        <div className="p-3.5 rounded-lg bg-zinc-950 border border-zinc-800/80 font-mono text-xs text-zinc-300 overflow-x-auto">
          <pre>{snippets[selectedLang]}</pre>
        </div>
      </div>

      {/* Recent Activity Table */}
      <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-3">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold text-zinc-200">So'nggi So'rovlar</h3>
            <p className="text-[11px] text-zinc-500">Gateway orqali o'tgan jonli so'rovlar</p>
          </div>
          <button
            onClick={() => onNavigate('activity')}
            className="text-xs text-zinc-400 hover:text-zinc-200 font-medium flex items-center space-x-1 cursor-pointer"
          >
            <span>Barcha loglar</span>
            <ArrowUpRight className="w-3.5 h-3.5 text-zinc-600" />
          </button>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-zinc-800 text-zinc-500 font-medium text-[11px]">
                <th className="pb-2.5 pl-1">Vaqt</th>
                <th className="pb-2.5">Model</th>
                <th className="pb-2.5">Tokenlar</th>
                <th className="pb-2.5">Kechikish</th>
                <th className="pb-2.5">Xarajat</th>
                <th className="pb-2.5">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 font-mono text-[11px]">
              {recentLogs.slice(0, 4).map((log) => (
                <tr key={log.id} className="hover:bg-zinc-900/50 transition">
                  <td className="py-2.5 pl-1 text-zinc-500">
                    {new Date(log.timestamp).toLocaleTimeString()}
                  </td>
                  <td className="py-2.5 text-zinc-200 font-sans font-medium">
                    {log.model} <span className="text-zinc-500 font-mono text-[10px]">({log.provider})</span>
                  </td>
                  <td className="py-2.5 text-zinc-400">
                    {log.totalTokens.toLocaleString()} tok
                  </td>
                  <td className="py-2.5 text-zinc-400">
                    {log.latencyMs}ms
                  </td>
                  <td className="py-2.5 text-emerald-400">
                    ${log.costUsd.toFixed(6)}
                  </td>
                  <td className="py-2.5 font-sans">
                    {log.statusCode === 200 ? (
                      <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-medium">
                        <CheckCircle2 className="w-3 h-3" />
                        <span>200 OK</span>
                      </span>
                    ) : (
                      <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-rose-500/10 text-rose-400 border border-rose-500/20 text-[10px] font-medium">
                        <AlertTriangle className="w-3 h-3" />
                        <span>{log.statusCode}</span>
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};
