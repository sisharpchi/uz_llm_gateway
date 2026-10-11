import React from 'react';
import { CURRENT_EXCHANGE_RATE } from '../../data/mockData';
import {
  X,
  CreditCard,
  CheckCircle2,
  ArrowRight,
  Loader2
} from 'lucide-react';

interface TopUpModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (amountUzs: number, amountUsd: number, method: 'Payme' | 'CLICK') => void;
}

export const TopUpModal: React.FC<TopUpModalProps> = ({ isOpen, onClose, onSuccess }) => {
  const [method, setMethod] = React.useState<'Payme' | 'CLICK'>('Payme');
  const [amountUzs, setAmountUzs] = React.useState<number>(150000);
  const [customInput, setCustomInput] = React.useState<string>('150000');
  const [isProcessing, setIsProcessing] = React.useState(false);
  const [success, setSuccess] = React.useState(false);

  if (!isOpen) return null;

  const presets = [50000, 150000, 500000, 1300000];
  const creditedUsd = Number((amountUzs / CURRENT_EXCHANGE_RATE).toFixed(2));

  const handleSelectPreset = (val: number) => {
    setAmountUzs(val);
    setCustomInput(val.toString());
  };

  const handleCustomChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = Number(e.target.value.replace(/\D/g, ''));
    setAmountUzs(val);
    setCustomInput(val ? val.toString() : '');
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (amountUzs < 10000) return;

    setIsProcessing(true);
    setTimeout(() => {
      setIsProcessing(false);
      setSuccess(true);
      setTimeout(() => {
        onSuccess(amountUzs, creditedUsd, method);
        setSuccess(false);
        onClose();
      }, 1200);
    }, 1000);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl bg-zinc-950 border border-zinc-800 shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
        {/* Top Header */}
        <div className="p-4 border-b border-zinc-800/80 flex items-center justify-between">
          <div>
            <h2 className="text-sm font-semibold text-zinc-100">Hisobni To'ldirish</h2>
            <p className="text-[11px] text-zinc-500">O'zbekiston milliy to'lov tizimlari orqali</p>
          </div>
          <button
            onClick={onClose}
            className="text-zinc-500 hover:text-zinc-200 p-1 rounded-md transition cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {success ? (
          <div className="p-6 text-center space-y-2.5">
            <div className="w-10 h-10 rounded-full bg-emerald-500/10 text-emerald-400 flex items-center justify-center mx-auto">
              <CheckCircle2 className="w-6 h-6" />
            </div>
            <h3 className="text-sm font-semibold text-zinc-100">To'lov Muvaffaqiyatli Bajarildi</h3>
            <p className="text-xs text-zinc-400 font-mono">
              +${creditedUsd.toFixed(2)} USD balansingizga qo'shildi
            </p>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="p-4 space-y-4">
            {/* Payment Method Selector */}
            <div>
              <label className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider block mb-1.5">
                To'lov Tizimi
              </label>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => setMethod('Payme')}
                  className={`p-2.5 rounded-lg border text-left transition cursor-pointer ${
                    method === 'Payme'
                      ? 'bg-zinc-900 border-zinc-500 text-zinc-100'
                      : 'bg-zinc-950 border-zinc-800 text-zinc-400 hover:border-zinc-700'
                  }`}
                >
                  <div className="font-semibold text-xs text-cyan-400">Payme</div>
                  <div className="text-[10px] text-zinc-500">0% komissiya</div>
                </button>

                <button
                  type="button"
                  onClick={() => setMethod('CLICK')}
                  className={`p-2.5 rounded-lg border text-left transition cursor-pointer ${
                    method === 'CLICK'
                      ? 'bg-zinc-900 border-zinc-500 text-zinc-100'
                      : 'bg-zinc-950 border-zinc-800 text-zinc-400 hover:border-zinc-700'
                  }`}
                >
                  <div className="font-semibold text-xs text-emerald-400">CLICK</div>
                  <div className="text-[10px] text-zinc-500">0% komissiya</div>
                </button>
              </div>
            </div>

            {/* Presets */}
            <div>
              <label className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider block mb-1.5">
                Tezkor Summa (UZS)
              </label>
              <div className="grid grid-cols-4 gap-1.5">
                {presets.map((p) => (
                  <button
                    key={p}
                    type="button"
                    onClick={() => handleSelectPreset(p)}
                    className={`py-1.5 rounded-md text-xs font-mono transition cursor-pointer border ${
                      amountUzs === p
                        ? 'bg-zinc-800 border-zinc-600 text-zinc-100 font-medium'
                        : 'bg-zinc-900/60 border-zinc-800 text-zinc-400 hover:text-zinc-200'
                    }`}
                  >
                    {(p / 1000).toLocaleString()}k
                  </button>
                ))}
              </div>
            </div>

            {/* Custom Input & USD preview */}
            <div className="p-3 rounded-lg bg-zinc-900/40 border border-zinc-800 space-y-2">
              <div className="relative">
                <input
                  type="text"
                  value={Number(customInput || 0).toLocaleString()}
                  onChange={handleCustomChange}
                  className="w-full px-3 py-1.5 rounded-md bg-zinc-950 border border-zinc-800 text-xs font-mono text-zinc-100 focus:outline-none focus:border-zinc-700"
                />
                <span className="absolute right-3 top-2 text-[10px] text-zinc-500 font-mono">UZS</span>
              </div>

              <div className="pt-2 border-t border-zinc-800/80 flex items-center justify-between text-xs font-mono">
                <span className="text-zinc-500">Kredit:</span>
                <span className="font-bold text-emerald-400 text-sm">
                  +${creditedUsd.toFixed(2)} USD
                </span>
              </div>
              <div className="text-[10px] text-zinc-500 flex items-center justify-between font-mono">
                <span>Kurs:</span>
                <span>1 USD = 13,000 UZS</span>
              </div>
            </div>

            {/* Submit */}
            <button
              type="submit"
              disabled={isProcessing || amountUzs < 10000}
              className="w-full py-2.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 font-medium text-xs transition flex items-center justify-center space-x-1.5 cursor-pointer disabled:opacity-50 shadow-xs active:scale-[0.98]"
            >
              {isProcessing ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Ulanmoqda...</span>
                </>
              ) : (
                <>
                  <span>{method} orqali to'lash ({amountUzs.toLocaleString()} so'm)</span>
                  <ArrowRight className="w-3.5 h-3.5" />
                </>
              )}
            </button>
          </form>
        )}
      </div>
    </div>
  );
};
