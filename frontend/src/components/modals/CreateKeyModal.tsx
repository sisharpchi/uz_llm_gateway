import React from 'react';
import { Project, ApiKeyItem } from '../../types';
import { X, Key, Copy, Check, ShieldAlert } from 'lucide-react';

interface CreateKeyModalProps {
  isOpen: boolean;
  projects: Project[];
  currentProjectId: string;
  onClose: () => void;
  onCreated: (newKey: ApiKeyItem) => void;
}

export const CreateKeyModal: React.FC<CreateKeyModalProps> = ({
  isOpen,
  projects,
  currentProjectId,
  onClose,
  onCreated
}) => {
  const [name, setName] = React.useState('');
  const [selectedProjectId, setSelectedProjectId] = React.useState(currentProjectId);
  const [limitUsd, setLimitUsd] = React.useState<string>('50');
  const [generatedSecret, setGeneratedSecret] = React.useState<string | null>(null);
  const [copied, setCopied] = React.useState(false);

  if (!isOpen) return null;

  const handleGenerate = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name) return;

    const randomHex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16)).join('');
    const fullSecretKey = `uzllm_live_${randomHex}`;
    const masked = `uzllm_live_${randomHex.slice(0, 4)}...${randomHex.slice(-4)}`;

    const project = projects.find(p => p.id === selectedProjectId) || projects[0];

    const newKeyItem: ApiKeyItem = {
      id: `key_${Date.now()}`,
      name,
      maskedKey: masked,
      projectId: project.id,
      projectName: project.name,
      createdAt: new Date().toISOString(),
      expiresAt: null,
      lastUsedAt: null,
      status: 'active',
      spendLimitMonthlyUsd: limitUsd ? Number(limitUsd) : null,
      currentSpendMonthlyUsd: 0,
    };

    setGeneratedSecret(fullSecretKey);
    onCreated(newKeyItem);
  };

  const handleCopy = () => {
    if (generatedSecret) {
      navigator.clipboard.writeText(generatedSecret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const handleFinish = () => {
    setGeneratedSecret(null);
    setName('');
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl bg-zinc-950 border border-zinc-800 shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
        {/* Modal Header */}
        <div className="p-4 border-b border-zinc-800/80 flex items-center justify-between">
          <div>
            <h2 className="text-sm font-semibold text-zinc-100">Yangi API Kalit Yaratish</h2>
            <p className="text-[11px] text-zinc-500">Barcha modellar uchun yagona shlyuz kaliti</p>
          </div>
          <button
            onClick={onClose}
            className="text-zinc-500 hover:text-zinc-200 p-1 rounded-md transition cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Content */}
        {generatedSecret ? (
          <div className="p-4 space-y-4">
            <div className="p-3 rounded-lg bg-zinc-900 border border-zinc-800 flex items-start space-x-2.5 text-xs text-zinc-300">
              <ShieldAlert className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
              <div className="leading-relaxed">
                <span className="font-semibold text-zinc-100">Kalit faqat bir marta ko'rsatiladi:</span>
                <p className="text-zinc-400 mt-0.5">Ushbu kalitni xavfsiz joyga ko'chiring (.env). Oynani yopgach uni qayta ko'rib bo'lmaydi.</p>
              </div>
            </div>

            <div className="space-y-1">
              <div className="flex items-center space-x-1.5 p-2.5 rounded-lg bg-zinc-900 border border-zinc-800 font-mono text-xs text-zinc-100 break-all select-all">
                <span className="flex-1 text-[11px]">{generatedSecret}</span>
                <button
                  onClick={handleCopy}
                  className="px-2.5 py-1 rounded bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium flex items-center space-x-1 transition cursor-pointer shrink-0"
                >
                  {copied ? <Check className="w-3 h-3 text-emerald-600" /> : <Copy className="w-3 h-3" />}
                  <span>{copied ? 'Nusxalandi' : 'Nusxalash'}</span>
                </button>
              </div>
            </div>

            <button
              onClick={handleFinish}
              className="w-full py-2 rounded-lg bg-zinc-900 hover:bg-zinc-800 text-zinc-200 text-xs font-medium border border-zinc-800 transition cursor-pointer"
            >
              Kalitni saqlab oldim, oynani yopish
            </button>
          </div>
        ) : (
          <form onSubmit={handleGenerate} className="p-4 space-y-3.5">
            <div>
              <label className="text-[11px] font-medium text-zinc-400 block mb-1">
                Kalit nomi
              </label>
              <input
                type="text"
                required
                placeholder="Masalan: Telegram Bot Backend"
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
              />
            </div>

            <div>
              <label className="text-[11px] font-medium text-zinc-400 block mb-1">
                Loyiha
              </label>
              <select
                value={selectedProjectId}
                onChange={(e) => setSelectedProjectId(e.target.value)}
                className="w-full px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 focus:outline-none focus:border-zinc-700 cursor-pointer"
              >
                {projects.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.environment})
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="text-[11px] font-medium text-zinc-400 block mb-1">
                Oylik xarajat limiti ($ USD)
              </label>
              <input
                type="number"
                placeholder="Limitsiz uchun bo'sh qoldiring"
                value={limitUsd}
                onChange={(e) => setLimitUsd(e.target.value)}
                className="w-full px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 font-mono placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
              />
            </div>

            <div className="pt-2 flex items-center justify-end space-x-2">
              <button
                type="button"
                onClick={onClose}
                className="px-3 py-1.5 rounded-lg bg-zinc-900 hover:bg-zinc-800 text-zinc-400 text-xs font-medium transition cursor-pointer"
              >
                Bekor qilish
              </button>
              <button
                type="submit"
                className="px-3.5 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
              >
                Kalit Yaratish
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
};
