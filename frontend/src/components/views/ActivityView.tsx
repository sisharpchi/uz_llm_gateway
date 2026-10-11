import React from 'react';
import { UsageLogItem } from '../../types';
import {
  Activity,
  Search,
  CheckCircle2,
  AlertTriangle,
  GitFork,
  Copy,
  Check
} from 'lucide-react';

interface ActivityViewProps {
  logs: UsageLogItem[];
}

export const ActivityView: React.FC<ActivityViewProps> = ({ logs }) => {
  const [selectedLog, setSelectedLog] = React.useState<UsageLogItem | null>(logs[0] || null);
  const [search, setSearch] = React.useState('');
  const [statusFilter, setStatusFilter] = React.useState<'all' | '200' | 'errors'>('all');
  const [copiedId, setCopiedId] = React.useState(false);

  const filteredLogs = logs.filter((log) => {
    const matchesSearch = log.model.toLowerCase().includes(search.toLowerCase()) ||
                          log.id.toLowerCase().includes(search.toLowerCase()) ||
                          log.keyName.toLowerCase().includes(search.toLowerCase());
    const matchesStatus = statusFilter === 'all' ||
                          (statusFilter === '200' && log.statusCode === 200) ||
                          (statusFilter === 'errors' && log.statusCode !== 200);
    return matchesSearch && matchesStatus;
  });

  const handleCopyId = (id: string) => {
    navigator.clipboard.writeText(id);
    setCopiedId(true);
    setTimeout(() => setCopiedId(false), 1500);
  };

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-zinc-800/80 gap-3">
        <div>
          <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
            So'rovlar Tarixi (Activity & Logs)
          </h1>
          <p className="text-xs text-zinc-500 mt-0.5">
            Gateway orqali o'tayotgan har bir LLM chaqiruvi, tokenlar hisobi, kechikish va xarajat auditi
          </p>
        </div>

        <div className="flex items-center space-x-1.5 text-xs text-zinc-400 font-mono">
          <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
          <span>Jonli oqim</span>
        </div>
      </div>

      {/* Filter and Search Bar */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-3">
        <div className="relative w-full sm:w-80">
          <Search className="w-3.5 h-3.5 text-zinc-500 absolute left-3 top-2.5" />
          <input
            type="text"
            placeholder="Request ID, model yoki kalit..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-8 pr-3 py-1.5 rounded-lg bg-zinc-900/60 border border-zinc-800 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
          />
        </div>

        <div className="flex items-center space-x-1 p-0.5 rounded-lg bg-zinc-900 border border-zinc-800">
          <button
            onClick={() => setStatusFilter('all')}
            className={`px-2.5 py-1 rounded text-xs font-medium transition cursor-pointer ${
              statusFilter === 'all' ? 'bg-zinc-800 text-zinc-100' : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            Barchasi
          </button>
          <button
            onClick={() => setStatusFilter('200')}
            className={`px-2.5 py-1 rounded text-xs font-medium transition cursor-pointer ${
              statusFilter === '200' ? 'bg-zinc-800 text-emerald-400' : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            200 OK
          </button>
          <button
            onClick={() => setStatusFilter('errors')}
            className={`px-2.5 py-1 rounded text-xs font-medium transition cursor-pointer ${
              statusFilter === 'errors' ? 'bg-zinc-800 text-rose-400' : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            Xatolar
          </button>
        </div>
      </div>

      {/* Split view: Table on Left + Inspector on Right */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Table Column */}
        <div className="lg:col-span-2 rounded-xl bg-zinc-900/40 border border-zinc-800/80 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-zinc-800 bg-zinc-950/60 text-zinc-500 font-medium text-[11px]">
                  <th className="py-2.5 px-3">Vaqt</th>
                  <th className="py-2.5 px-3">Model</th>
                  <th className="py-2.5 px-3">Status</th>
                  <th className="py-2.5 px-3">Tokenlar</th>
                  <th className="py-2.5 px-3">Vaqt</th>
                  <th className="py-2.5 px-3">Xarajat</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/60 font-mono text-[11px]">
                {filteredLogs.map((log) => {
                  const isSelected = selectedLog?.id === log.id;
                  return (
                    <tr
                      key={log.id}
                      onClick={() => setSelectedLog(log)}
                      className={`cursor-pointer transition ${
                        isSelected
                          ? 'bg-zinc-800/60 border-l-2 border-zinc-300'
                          : 'hover:bg-zinc-900/40'
                      }`}
                    >
                      <td className="py-2.5 px-3 text-zinc-500">
                        {new Date(log.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                      </td>
                      <td className="py-2.5 px-3 font-sans">
                        <div className="font-medium text-zinc-200">{log.model}</div>
                        <div className="text-[10px] text-zinc-500 font-mono truncate max-w-[120px]">{log.keyName}</div>
                      </td>
                      <td className="py-2.5 px-3 font-sans">
                        {log.statusCode === 200 ? (
                          <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 text-[10px] font-medium">
                            <CheckCircle2 className="w-3 h-3" />
                            <span>200</span>
                          </span>
                        ) : (
                          <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-rose-500/10 text-rose-400 text-[10px] font-medium">
                            <AlertTriangle className="w-3 h-3" />
                            <span>{log.statusCode}</span>
                          </span>
                        )}
                        {log.failoverOccurred && (
                          <span className="ml-1 inline-flex items-center text-amber-400 text-[10px]" title="Failover ishlatildi">
                            <GitFork className="w-3 h-3" />
                          </span>
                        )}
                      </td>
                      <td className="py-2.5 px-3 text-zinc-400">
                        {log.totalTokens.toLocaleString()}
                      </td>
                      <td className="py-2.5 px-3 text-zinc-400">
                        {log.latencyMs}ms
                      </td>
                      <td className="py-2.5 px-3 text-emerald-400 font-medium">
                        ${log.costUsd.toFixed(6)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        {/* Request Details Inspector */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-3.5">
          <div className="flex items-center justify-between pb-2.5 border-b border-zinc-800/80">
            <h3 className="text-xs font-semibold text-zinc-200">
              So'rov Tafsilotlari
            </h3>
            {selectedLog && (
              <button
                onClick={() => handleCopyId(selectedLog.id)}
                className="font-mono text-[10px] text-zinc-400 hover:text-zinc-200 flex items-center space-x-1 bg-zinc-950 px-2 py-0.5 rounded border border-zinc-800"
              >
                <span>{selectedLog.id}</span>
                {copiedId ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3 text-zinc-600" />}
              </button>
            )}
          </div>

          {selectedLog ? (
            <div className="space-y-3 text-xs">
              <div className="grid grid-cols-2 gap-2 p-2.5 rounded-lg bg-zinc-950 border border-zinc-800 font-mono text-[11px]">
                <div>
                  <span className="text-zinc-500 block text-[10px]">Model</span>
                  <span className="font-semibold text-zinc-200 font-sans">{selectedLog.model}</span>
                  <span className="text-zinc-500 block text-[10px]">({selectedLog.provider})</span>
                </div>
                <div>
                  <span className="text-zinc-500 block text-[10px]">Xarajat</span>
                  <span className="font-bold text-emerald-400">
                    ${selectedLog.costUsd.toFixed(6)}
                  </span>
                  <span className="text-zinc-500 block text-[10px]">
                    ≈ {(selectedLog.costUsd * 13000).toFixed(2)} so'm
                  </span>
                </div>
              </div>

              {/* Token Breakdown */}
              <div className="space-y-1.5">
                <div className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Tokenlar
                </div>
                <div className="p-2.5 rounded-lg bg-zinc-950 border border-zinc-800 space-y-1.5 font-mono text-[11px]">
                  <div className="flex justify-between">
                    <span className="text-zinc-500">Prompt:</span>
                    <span className="text-zinc-300">{selectedLog.promptTokens}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-zinc-500">Completion:</span>
                    <span className="text-zinc-300">{selectedLog.completionTokens}</span>
                  </div>
                  <div className="flex justify-between pt-1 border-t border-zinc-800/80">
                    <span className="text-zinc-300">Jami:</span>
                    <span className="text-zinc-100 font-semibold">{selectedLog.totalTokens}</span>
                  </div>
                </div>
              </div>

              {/* Failover note if present */}
              {selectedLog.failoverOccurred && (
                <div className="p-2.5 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-300 text-[11px]">
                  <div className="font-medium flex items-center space-x-1 mb-0.5">
                    <GitFork className="w-3 h-3" />
                    <span>Failover Faollashtirildi</span>
                  </div>
                  <p className="text-amber-200/80 text-[10px]">{selectedLog.failoverDetails}</p>
                </div>
              )}

              {/* Technical Metadata */}
              <div className="space-y-1.5">
                <div className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Metama'lumotlar
                </div>
                <div className="p-2.5 rounded-lg bg-zinc-950 border border-zinc-800 space-y-1 font-mono text-[11px]">
                  <div className="flex justify-between">
                    <span className="text-zinc-500">Kechikish:</span>
                    <span className="text-zinc-300">{selectedLog.latencyMs} ms</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-zinc-500">Kesh:</span>
                    <span className={selectedLog.cached ? "text-emerald-400" : "text-zinc-500"}>
                      {selectedLog.cached ? "HIT (5ms)" : "MISS"}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-zinc-500">Kalit:</span>
                    <span className="text-zinc-400 truncate max-w-[120px]">{selectedLog.keyName}</span>
                  </div>
                </div>
              </div>
            </div>
          ) : (
            <div className="py-6 text-center text-zinc-600 text-xs">
              Ko'rish uchun chap tomondan so'rovni tanlang
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
