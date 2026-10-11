import React from 'react';
import { Organization, TransactionItem, Currency } from '../../types';
import {
  CreditCard,
  Plus,
  CheckCircle2,
  ExternalLink,
  Download,
  Receipt,
  Bell,
  Check
} from 'lucide-react';

interface BillingViewProps {
  currentOrg: Organization;
  transactions: TransactionItem[];
  currency: Currency;
  onOpenTopUp: () => void;
}

export const BillingView: React.FC<BillingViewProps> = ({
  currentOrg,
  transactions,
  currency,
  onOpenTopUp
}) => {
  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-zinc-800/80 gap-3">
        <div>
          <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
            Hamyon va To'lovlar
          </h1>
          <p className="text-xs text-zinc-500 mt-0.5">
            Prepaid balans boshqaruvi, Payme va CLICK orqali hisob to'ldirish va tranzaksiyalar auditi
          </p>
        </div>

        <button
          onClick={onOpenTopUp}
          className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
        >
          <Plus className="w-3.5 h-3.5" />
          <span>Hisobni To'ldirish (Payme / CLICK)</span>
        </button>
      </div>

      {/* Wallet Summary Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        {/* Card 1: Balance Main */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-2">
              <span>Hozirgi Balans</span>
              <span className="font-mono text-zinc-400">Prepaid</span>
            </div>
            <div className="text-2xl font-bold font-mono text-zinc-100 mb-0.5">
              ${currentOrg.balanceUsd.toFixed(2)}
            </div>
            <div className="text-xs font-mono text-zinc-400">
              ≈ {(currentOrg.balanceUzs).toLocaleString()} so'm UZS
            </div>
          </div>

          <div className="mt-4 pt-2.5 border-t border-zinc-800/80 text-[11px] text-zinc-500 flex items-center justify-between font-mono">
            <span>Fiksatsiyalangan kurs:</span>
            <span className="text-zinc-300">1 USD = 13,000 UZS</span>
          </div>
        </div>

        {/* Card 2: Alerts Threshold */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-2">
              <span>Kam Balans Xabarnomasi</span>
              <Bell className="w-3.5 h-3.5 text-zinc-500" />
            </div>
            <p className="text-xs text-zinc-400 leading-relaxed mb-3">
              Balans $10 dan kamayganda Telegram (@uzllm_gateway_bot) orqali xabarnoma yuboriladi.
            </p>
          </div>

          <div className="pt-2.5 border-t border-zinc-800/80 flex items-center justify-between text-[11px] text-zinc-500 font-mono">
            <span>Avto-bloklash:</span>
            <span className="text-emerald-400 font-medium">$0 ga yetganda</span>
          </div>
        </div>

        {/* Card 3: Supported Gateways */}
        <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col justify-between">
          <div>
            <div className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-2">
              To'lov Tizimlari
            </div>
            <p className="text-xs text-zinc-400 mb-3">
              O'zbekiston milliy kartalari va bank ilovalaridan 0% komissiya bilan:
            </p>
            <div className="flex items-center space-x-2 text-xs font-mono">
              <span className="px-2 py-1 rounded bg-zinc-950 border border-zinc-800 text-zinc-200">
                Payme
              </span>
              <span className="px-2 py-1 rounded bg-zinc-950 border border-zinc-800 text-zinc-200">
                CLICK
              </span>
              <span className="px-2 py-1 rounded bg-zinc-950 border border-zinc-800 text-zinc-400">
                UzCard / Humo
              </span>
            </div>
          </div>

          <div className="pt-2.5 border-t border-zinc-800/80 text-[11px] text-zinc-500">
            Fiskal chek darhol beriladi
          </div>
        </div>
      </div>

      {/* Transaction History Table */}
      <div className="p-5 rounded-xl bg-zinc-900/40 border border-zinc-800/80 space-y-3">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="text-sm font-semibold text-zinc-200">To'lovlar Tarixi (Ledger)</h3>
            <p className="text-[11px] text-zinc-500">O'zgartirib bo'lmas moliyaviy audit yozuvlari</p>
          </div>
          <button className="flex items-center space-x-1.5 px-2.5 py-1 rounded-md bg-zinc-900 hover:bg-zinc-800 text-zinc-400 hover:text-zinc-200 text-xs font-medium border border-zinc-800 transition cursor-pointer">
            <Download className="w-3.5 h-3.5" />
            <span className="font-mono text-[11px]">CSV</span>
          </button>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-zinc-800 bg-zinc-950/60 text-zinc-500 font-medium text-[11px]">
                <th className="py-2.5 px-3">Vaqt</th>
                <th className="py-2.5 px-3">Usul</th>
                <th className="py-2.5 px-3">To'langan (UZS)</th>
                <th className="py-2.5 px-3">Kredit (USD)</th>
                <th className="py-2.5 px-3">Kurs</th>
                <th className="py-2.5 px-3">Holati</th>
                <th className="py-2.5 px-3">Chek</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 font-mono text-[11px]">
              {transactions.map((tx) => (
                <tr key={tx.id} className="hover:bg-zinc-900/50 transition">
                  <td className="py-2.5 px-3 text-zinc-500">
                    {new Date(tx.timestamp).toLocaleDateString()} {new Date(tx.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                  </td>
                  <td className="py-2.5 px-3 text-zinc-200 font-sans font-medium">
                    <span className="px-1.5 py-0.5 rounded bg-zinc-800 border border-zinc-700/60 text-[10px]">
                      {tx.paymentMethod}
                    </span>
                  </td>
                  <td className="py-2.5 px-3 text-zinc-200">
                    {tx.amountUzs.toLocaleString()} so'm
                  </td>
                  <td className="py-2.5 px-3 text-emerald-400 font-medium">
                    +${tx.creditedUsd.toFixed(2)}
                  </td>
                  <td className="py-2.5 px-3 text-zinc-500">
                    1 USD = {tx.exchangeRate.toLocaleString()} UZS
                  </td>
                  <td className="py-2.5 px-3 font-sans">
                    <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-medium">
                      <CheckCircle2 className="w-3 h-3" />
                      <span>Bajarildi</span>
                    </span>
                  </td>
                  <td className="py-2.5 px-3">
                    {tx.receiptUrl ? (
                      <a
                        href={tx.receiptUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="text-zinc-400 hover:text-zinc-200 inline-flex items-center space-x-1"
                      >
                        <Receipt className="w-3 h-3 text-zinc-500" />
                        <span>#{tx.id.slice(-5)}</span>
                        <ExternalLink className="w-2.5 h-2.5 text-zinc-600" />
                      </a>
                    ) : (
                      <span className="text-zinc-600">-</span>
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
