import React from 'react';
import { AlertConfig } from '../../types';
import { BellRing, Send, Check, MessageSquare, AlertTriangle } from 'lucide-react';

interface AlertsViewProps {
  config: AlertConfig;
  onSaveConfig: (cfg: AlertConfig) => void;
}

export const AlertsView: React.FC<AlertsViewProps> = ({ config, onSaveConfig }) => {
  const [formData, setFormData] = React.useState<AlertConfig>(config);
  const [testSent, setTestSent] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  const handleSendTest = () => {
    setTestSent(true);
    setTimeout(() => setTestSent(false), 2500);
  };

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    onSaveConfig(formData);
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="pb-4 border-b border-zinc-800/80">
        <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
          Telegram & Xabarnomalar (Alerts)
        </h1>
        <p className="text-xs text-zinc-500 mt-0.5">
          Balans tugashi, provayder xatolari yoki kutilmagan trafik oshishida darhol Telegramga xabar olish
        </p>
      </div>

      <form onSubmit={handleSave} className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Main Settings on Left */}
        <div className="lg:col-span-2 space-y-4">
          {/* Telegram Bot Card */}
          <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-zinc-800/80">
              <div className="flex items-center space-x-2.5">
                <div className="w-7 h-7 rounded-lg bg-zinc-800 flex items-center justify-center text-zinc-300">
                  <MessageSquare className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-xs font-semibold text-zinc-200">Telegram Bot Integratsiyasi</h3>
                  <p className="text-[11px] text-zinc-500 font-mono">@uzllm_gateway_bot</p>
                </div>
              </div>

              <input
                type="checkbox"
                checked={formData.telegramEnabled}
                onChange={(e) => setFormData({ ...formData, telegramEnabled: e.target.checked })}
                className="w-4 h-4 accent-white cursor-pointer"
              />
            </div>

            <div className="space-y-2">
              <label className="text-xs font-medium text-zinc-300 block">
                Telegram Chat ID yoki Guruh ID
              </label>
              <div className="flex items-center space-x-2">
                <input
                  type="text"
                  value={formData.telegramChatId}
                  onChange={(e) => setFormData({ ...formData, telegramChatId: e.target.value })}
                  placeholder="Masalan: 123456789 yoki -100123456789"
                  className="flex-1 px-3 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-zinc-200 font-mono focus:outline-none focus:border-zinc-700"
                />
                <button
                  type="button"
                  onClick={handleSendTest}
                  className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-zinc-800 hover:bg-zinc-700 text-zinc-200 text-xs font-medium border border-zinc-700 transition cursor-pointer shrink-0"
                >
                  {testSent ? (
                    <>
                      <Check className="w-3.5 h-3.5 text-emerald-400" />
                      <span className="text-emerald-400 font-mono text-[11px]">Yuborildi</span>
                    </>
                  ) : (
                    <>
                      <Send className="w-3.5 h-3.5 text-zinc-400" />
                      <span>Test xabar</span>
                    </>
                  )}
                </button>
              </div>
              <p className="text-[11px] text-zinc-500">
                Telegramda <code className="text-zinc-300">@uzllm_gateway_bot</code> ga kiring va <code className="text-zinc-300">/start</code> bosing.
              </p>
            </div>
          </div>

          {/* Trigger Thresholds Card */}
          <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-4">
            <h3 className="text-xs font-semibold text-zinc-200 pb-2 border-b border-zinc-800/80">
              Ogohlantirish Shartlari
            </h3>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="text-xs font-medium text-zinc-400 block mb-1">
                  Kam balans chegarasi ($ USD)
                </label>
                <div className="relative">
                  <input
                    type="number"
                    value={formData.lowBalanceThresholdUsd}
                    onChange={(e) => setFormData({ ...formData, lowBalanceThresholdUsd: Number(e.target.value) })}
                    className="w-full px-3 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-zinc-200 font-mono focus:outline-none focus:border-zinc-700"
                  />
                  <span className="absolute right-3 top-1.5 text-[11px] text-zinc-500 font-mono">
                    ≈ {(formData.lowBalanceThresholdUsd * 13000).toLocaleString()} so'm
                  </span>
                </div>
              </div>

              <div>
                <label className="text-xs font-medium text-zinc-400 block mb-1">
                  Xatolar foizi chegarasi (%)
                </label>
                <input
                  type="number"
                  step="0.5"
                  value={formData.errorRateThresholdPercent}
                  onChange={(e) => setFormData({ ...formData, errorRateThresholdPercent: Number(e.target.value) })}
                  className="w-full px-3 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-zinc-200 font-mono focus:outline-none focus:border-zinc-700"
                />
              </div>
            </div>

            <div>
              <label className="flex items-center space-x-2 text-xs text-zinc-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.notifyOnFailover}
                  onChange={(e) => setFormData({ ...formData, notifyOnFailover: e.target.checked })}
                  className="w-3.5 h-3.5 accent-white rounded"
                />
                <span>Zaxira provayderga o'tish (Failover) sodir bo'lganda ogohlantirish</span>
              </label>
            </div>
          </div>

          <button
            type="submit"
            className="flex items-center space-x-1.5 px-4 py-2 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
          >
            {saved ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : null}
            <span>{saved ? 'Sozlamalar Saqlandi' : 'Sozlamalarni Saqlash'}</span>
          </button>
        </div>

        {/* Telegram Preview Mockup */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider mb-2.5">
              Telegram Bildirishnomasi Namunasi
            </div>

            <div className="p-3.5 rounded-lg bg-zinc-950 border border-zinc-800 text-xs space-y-2 font-mono">
              <div className="flex items-center space-x-2 border-b border-zinc-800/80 pb-1.5">
                <span className="w-4 h-4 rounded-full bg-zinc-800 flex items-center justify-center text-[9px] text-zinc-300">
                  UZ
                </span>
                <span className="font-semibold text-zinc-200 font-sans">UZLLM Alerts</span>
              </div>

              <div className="text-amber-400 font-medium font-sans flex items-center space-x-1">
                <AlertTriangle className="w-3.5 h-3.5" />
                <span>Diqqat: Balans kam qoldi</span>
              </div>

              <p className="text-zinc-400 text-[11px] leading-relaxed font-sans">
                Tashkilot: <b>SmartTech AI Lab</b><br />
                Mavjud balans: <b>$9.85 (128,050 so'm)</b><br />
                So'rovlar to'xtamasligi uchun balansni to'ldiring.
              </p>
            </div>
          </div>

          <div className="mt-4 text-[11px] text-zinc-500 text-center font-mono">
            24/7 tezkor yetkazish
          </div>
        </div>
      </form>
    </div>
  );
};
