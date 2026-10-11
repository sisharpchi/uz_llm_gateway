import React from 'react';
import { ModelItem } from '../../types';
import {
  Cpu,
  Search,
  Terminal,
  Copy,
  Check,
  Calculator,
  ArrowRight,
  Sparkles
} from 'lucide-react';

interface ModelsViewProps {
  models: ModelItem[];
  onToggleModel: (modelId: string) => void;
  onOpenPlaygroundWithModel: (modelId: string) => void;
}

export const ModelsView: React.FC<ModelsViewProps> = ({
  models,
  onToggleModel,
  onOpenPlaygroundWithModel
}) => {
  const [providerFilter, setProviderFilter] = React.useState<string>('All');
  const [search, setSearch] = React.useState('');
  const [copiedId, setCopiedId] = React.useState<string | null>(null);

  // Quick Token Cost Calculator state (inspired by llmgateway.io)
  const [calcModel, setCalcModel] = React.useState<string>('claude-3-5-sonnet');
  const [promptTokensInput, setPromptTokensInput] = React.useState<number>(1000);
  const [completionTokensInput, setCompletionTokensInput] = React.useState<number>(500);

  const providers = ['All', 'OpenAI', 'Anthropic', 'Google', 'DeepSeek'];

  const filteredModels = models.filter((m) => {
    const matchesProvider = providerFilter === 'All' || m.provider === providerFilter;
    const matchesSearch = m.name.toLowerCase().includes(search.toLowerCase()) ||
                          m.id.toLowerCase().includes(search.toLowerCase()) ||
                          m.description.toLowerCase().includes(search.toLowerCase());
    return matchesProvider && matchesSearch;
  });

  const handleCopyId = (id: string) => {
    navigator.clipboard.writeText(id);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 1500);
  };

  // Calculator computation
  const selectedCalcModel = models.find(m => m.id === calcModel) || models[0];
  const calculatedCostUsd = ((promptTokensInput / 1000000) * selectedCalcModel.inputPricePer1M) +
                           ((completionTokensInput / 1000000) * selectedCalcModel.outputPricePer1M);
  const calculatedCostUzs = calculatedCostUsd * 13000;

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-zinc-800/80 gap-3">
        <div>
          <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
            Modellar Katalogi
          </h1>
          <p className="text-xs text-zinc-500 mt-0.5">
            Barcha global AI provayderlari modellariga yagona narx va O'zbekiston shlyuzi orqali ulanish
          </p>
        </div>

        <div className="flex items-center space-x-2 text-xs text-zinc-400 font-mono">
          <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
          <span>7/7 model operatsion</span>
        </div>
      </div>

      {/* Filter Tabs & Search */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-3">
        <div className="flex items-center space-x-1 p-0.5 rounded-lg bg-zinc-900 border border-zinc-800 overflow-x-auto w-full sm:w-auto">
          {providers.map((p) => (
            <button
              key={p}
              onClick={() => setProviderFilter(p)}
              className={`px-3 py-1 rounded-md text-xs font-medium transition cursor-pointer ${
                providerFilter === p
                  ? 'bg-zinc-800 text-zinc-100 shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              {p}
            </button>
          ))}
        </div>

        <div className="relative w-full sm:w-72">
          <Search className="w-3.5 h-3.5 text-zinc-500 absolute left-3 top-2.5" />
          <input
            type="text"
            placeholder="Model nomi yoki ID bo'yicha qidirish..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-8 pr-3 py-1.5 rounded-lg bg-zinc-900/60 border border-zinc-800 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
          />
        </div>
      </div>

      {/* Model Cards Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {filteredModels.map((model) => (
          <div
            key={model.id}
            className={`p-4 rounded-xl border transition flex flex-col justify-between ${
              model.enabled
                ? 'bg-zinc-900/40 border-zinc-800/80 hover:border-zinc-700/80 shadow-xs'
                : 'bg-zinc-950/40 border-zinc-900 opacity-60'
            }`}
          >
            <div>
              {/* Header: Provider badge & Model ID */}
              <div className="flex items-center justify-between mb-2">
                <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-zinc-800/80 text-zinc-300 border border-zinc-700/60">
                  {model.provider}
                </span>

                <button
                  onClick={() => handleCopyId(model.id)}
                  className="flex items-center space-x-1 text-[11px] font-mono text-zinc-500 hover:text-zinc-300 transition cursor-pointer"
                  title="Model ID nusxalash"
                >
                  <span>{model.id}</span>
                  {copiedId === model.id ? (
                    <Check className="w-3 h-3 text-emerald-400" />
                  ) : (
                    <Copy className="w-3 h-3 text-zinc-600" />
                  )}
                </button>
              </div>

              {/* Title & Description */}
              <h3 className="text-sm font-semibold text-zinc-100 tracking-tight">
                {model.name}
              </h3>
              <p className="text-xs text-zinc-400 line-clamp-2 mt-1 mb-3 leading-relaxed">
                {model.description}
              </p>

              {/* Minimal Specs Row */}
              <div className="grid grid-cols-2 gap-2 text-[11px] p-2.5 rounded-lg bg-zinc-950 border border-zinc-800/80 mb-3 font-mono">
                <div>
                  <span className="text-zinc-500 block text-[10px]">Kontekst</span>
                  <span className="text-zinc-200">{(model.contextWindow / 1000).toFixed(0)}k token</span>
                </div>
                <div>
                  <span className="text-zinc-500 block text-[10px]">Kechikish</span>
                  <span className="text-zinc-200">~{model.latencyAvgMs}ms</span>
                </div>
                <div>
                  <span className="text-zinc-500 block text-[10px]">Input / 1M</span>
                  <span className="text-emerald-400 font-medium">${model.inputPricePer1M.toFixed(2)}</span>
                </div>
                <div>
                  <span className="text-zinc-500 block text-[10px]">Output / 1M</span>
                  <span className="text-emerald-400 font-medium">${model.outputPricePer1M.toFixed(2)}</span>
                </div>
              </div>
            </div>

            {/* Bottom Actions */}
            <div className="flex items-center justify-between pt-2.5 border-t border-zinc-800/80">
              <div className="flex items-center space-x-1.5 text-[10px] text-zinc-400">
                {model.supportsVision && (
                  <span className="px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-300">Vision</span>
                )}
                {model.supportsStreaming && (
                  <span className="px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-300">Stream</span>
                )}
              </div>

              <div className="flex items-center space-x-1.5">
                <button
                  onClick={() => onOpenPlaygroundWithModel(model.id)}
                  className="flex items-center space-x-1 px-2.5 py-1 rounded-md bg-zinc-800 hover:bg-zinc-700 text-zinc-200 text-xs font-medium transition cursor-pointer"
                >
                  <Terminal className="w-3 h-3 text-zinc-400" />
                  <span>Sinash</span>
                </button>
                <button
                  onClick={() => onToggleModel(model.id)}
                  className={`text-xs px-2.5 py-1 rounded-md font-medium transition cursor-pointer ${
                    model.enabled
                      ? 'bg-zinc-900 hover:bg-zinc-800 text-zinc-400 border border-zinc-800'
                      : 'bg-white text-zinc-950 hover:bg-zinc-200'
                  }`}
                >
                  {model.enabled ? "O'chirish" : 'Yoqish'}
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Token Cost Calculator (Feature from llmgateway.io) */}
      <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-4">
        <div className="flex items-center space-x-2">
          <Calculator className="w-4 h-4 text-zinc-400" />
          <h3 className="text-sm font-semibold text-zinc-200">
            Token Narx Kalkulyatori (Token Cost Calculator)
          </h3>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div>
            <label className="text-[11px] text-zinc-400 block mb-1">Modelni tanlang</label>
            <select
              value={calcModel}
              onChange={(e) => setCalcModel(e.target.value)}
              className="w-full px-3 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-zinc-200 font-mono focus:outline-none focus:border-zinc-700 cursor-pointer"
            >
              {models.map(m => (
                <option key={m.id} value={m.id}>
                  {m.name} (${m.inputPricePer1M} in / ${m.outputPricePer1M} out)
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="text-[11px] text-zinc-400 block mb-1">Prompt Tokenlar (Input)</label>
            <input
              type="number"
              step="500"
              value={promptTokensInput}
              onChange={(e) => setPromptTokensInput(Number(e.target.value))}
              className="w-full px-3 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-zinc-200 font-mono focus:outline-none focus:border-zinc-700"
            />
          </div>

          <div>
            <label className="text-[11px] text-zinc-400 block mb-1">Completion Tokenlar (Output)</label>
            <input
              type="number"
              step="250"
              value={completionTokensInput}
              onChange={(e) => setCompletionTokensInput(Number(e.target.value))}
              className="w-full px-3 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-zinc-200 font-mono focus:outline-none focus:border-zinc-700"
            />
          </div>
        </div>

        <div className="p-3 rounded-lg bg-zinc-950 border border-zinc-800 flex items-center justify-between text-xs font-mono">
          <span className="text-zinc-400">1 ta so'rov uchun hisoblangan xarajat:</span>
          <div className="text-right">
            <span className="text-emerald-400 font-bold text-sm">
              ${calculatedCostUsd.toFixed(6)} USD
            </span>
            <span className="text-zinc-500 text-[11px] ml-2">
              (≈ {calculatedCostUzs.toFixed(2)} so'm)
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
