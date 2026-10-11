import React from 'react';
import { ApiKeyItem } from '../../types';
import {
  Key,
  Plus,
  Copy,
  Check,
  Trash2,
  ShieldAlert,
  CheckCircle2,
  XCircle,
  Clock,
  Search
} from 'lucide-react';

interface ApiKeysViewProps {
  apiKeys: ApiKeyItem[];
  onOpenCreateKey: () => void;
  onRevokeKey: (keyId: string) => void;
}

export const ApiKeysView: React.FC<ApiKeysViewProps> = ({
  apiKeys,
  onOpenCreateKey,
  onRevokeKey
}) => {
  const [copiedKeyId, setCopiedKeyId] = React.useState<string | null>(null);
  const [search, setSearch] = React.useState('');

  const handleCopy = (key: ApiKeyItem) => {
    navigator.clipboard.writeText(key.maskedKey);
    setCopiedKeyId(key.id);
    setTimeout(() => setCopiedKeyId(null), 1500);
  };

  const filteredKeys = apiKeys.filter(k =>
    k.name.toLowerCase().includes(search.toLowerCase()) ||
    k.maskedKey.toLowerCase().includes(search.toLowerCase()) ||
    k.projectName.toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-zinc-800/80 gap-3">
        <div>
          <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
            API Kalitlar
          </h1>
          <p className="text-xs text-zinc-500 mt-0.5">
            UZLLM Gateway shlyuziga so'rov yuborish uchun maxfiy tokenlar
          </p>
        </div>

        <button
          onClick={onOpenCreateKey}
          className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
        >
          <Plus className="w-3.5 h-3.5" />
          <span>Yangi Kalit Yaratish</span>
        </button>
      </div>

      {/* Minimalist Security Callout */}
      <div className="p-3 rounded-lg bg-zinc-900/60 border border-zinc-800 flex items-center space-x-2.5 text-xs text-zinc-400">
        <ShieldAlert className="w-4 h-4 text-zinc-400 shrink-0" />
        <span className="leading-relaxed">
          <strong className="text-zinc-200">Show-once xavfsizlik qoidasi:</strong> Yangi yaratilgan kalit faqat bir marta ko'rsatiladi. Uni darhol <code className="text-zinc-300 font-mono">.env</code> faylingizga nusxalab oling.
        </span>
      </div>

      {/* Filter and Search */}
      <div className="flex items-center justify-between gap-3">
        <div className="relative w-full sm:w-80">
          <Search className="w-3.5 h-3.5 text-zinc-500 absolute left-3 top-2.5" />
          <input
            type="text"
            placeholder="Kalit nomi yoki loyiha bo'yicha qidirish..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-8 pr-3 py-1.5 rounded-lg bg-zinc-900/60 border border-zinc-800 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
          />
        </div>
        <div className="text-xs text-zinc-500 font-mono">
          {filteredKeys.length} ta kalit
        </div>
      </div>

      {/* API Keys Minimalist Table */}
      <div className="rounded-xl bg-zinc-900/40 border border-zinc-800/80 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-zinc-800 bg-zinc-950/60 text-zinc-500 font-medium text-[11px]">
                <th className="py-2.5 px-3.5">Nomi & Token</th>
                <th className="py-2.5 px-3.5">Loyiha</th>
                <th className="py-2.5 px-3.5">Holati</th>
                <th className="py-2.5 px-3.5">Oylik Limit & Xarajat</th>
                <th className="py-2.5 px-3.5">Oxirgi Foydalanish</th>
                <th className="py-2.5 px-3.5 text-right">Amal</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 text-[11px]">
              {filteredKeys.map((key) => {
                const spendPercentage = key.spendLimitMonthlyUsd
                  ? Math.min(100, Math.round((key.currentSpendMonthlyUsd / key.spendLimitMonthlyUsd) * 100))
                  : null;

                return (
                  <tr key={key.id} className="hover:bg-zinc-900/50 transition">
                    {/* Key Name & Masked */}
                    <td className="py-3 px-3.5">
                      <div className="font-medium text-zinc-200">{key.name}</div>
                      <div className="flex items-center space-x-2 mt-1">
                        <code className="px-1.5 py-0.5 rounded bg-zinc-950 border border-zinc-800 text-zinc-300 font-mono text-[10px]">
                          {key.maskedKey}
                        </code>
                        <button
                          onClick={() => handleCopy(key)}
                          className="text-zinc-500 hover:text-zinc-300 p-0.5 rounded transition cursor-pointer"
                          title="Nusxalash"
                        >
                          {copiedKeyId === key.id ? (
                            <Check className="w-3 h-3 text-emerald-400" />
                          ) : (
                            <Copy className="w-3 h-3" />
                          )}
                        </button>
                      </div>
                    </td>

                    {/* Project */}
                    <td className="py-3 px-3.5 text-zinc-300 font-mono">
                      <span className="px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400 text-[10px]">
                        {key.projectName}
                      </span>
                    </td>

                    {/* Status */}
                    <td className="py-3 px-3.5">
                      {key.status === 'active' ? (
                        <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-medium">
                          <CheckCircle2 className="w-3 h-3" />
                          <span>Faol</span>
                        </span>
                      ) : (
                        <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-500 text-[10px] font-medium">
                          <XCircle className="w-3 h-3" />
                          <span>Revoked</span>
                        </span>
                      )}
                    </td>

                    {/* Spend Limit & Current Spend */}
                    <td className="py-3 px-3.5 font-mono">
                      <div className="flex items-center space-x-1.5 text-[10px] mb-1">
                        <span className="text-zinc-200 font-medium">${key.currentSpendMonthlyUsd.toFixed(2)}</span>
                        <span className="text-zinc-500">
                          {key.spendLimitMonthlyUsd ? `/ $${key.spendLimitMonthlyUsd.toFixed(2)}` : '/ limitsiz'}
                        </span>
                      </div>
                      {spendPercentage !== null && (
                        <div className="w-32 h-1 rounded-full bg-zinc-800 overflow-hidden">
                          <div
                            style={{ width: `${spendPercentage}%` }}
                            className={`h-full rounded-full ${
                              spendPercentage > 85 ? 'bg-rose-500' : 'bg-zinc-400'
                            }`}
                          />
                        </div>
                      )}
                    </td>

                    {/* Last Used */}
                    <td className="py-3 px-3.5 text-zinc-500 font-mono">
                      {key.lastUsedAt ? (
                        <div className="flex items-center space-x-1">
                          <Clock className="w-3 h-3 text-zinc-600" />
                          <span>{new Date(key.lastUsedAt).toLocaleDateString()} {new Date(key.lastUsedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                        </div>
                      ) : (
                        <span>Hali ishlatilmagan</span>
                      )}
                    </td>

                    {/* Actions */}
                    <td className="py-3 px-3.5 text-right">
                      {key.status === 'active' && (
                        <button
                          onClick={() => onRevokeKey(key.id)}
                          className="px-2 py-1 rounded text-zinc-400 hover:text-rose-400 hover:bg-rose-500/10 text-xs transition cursor-pointer"
                        >
                          Revoke
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};
