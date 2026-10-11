import React from 'react';
import { ViewType, ModelItem } from '../../types';
import {
  Search,
  LayoutDashboard,
  KeyRound,
  Cpu,
  CreditCard,
  Activity,
  GitFork,
  BellRing,
  Users,
  Terminal,
  Copy,
  Plus,
  Check,
  ArrowRight,
  Sparkles
} from 'lucide-react';

interface CommandPaletteProps {
  isOpen: boolean;
  onClose: () => void;
  onNavigate: (view: ViewType) => void;
  onOpenTopUp: () => void;
  onOpenCreateKey: () => void;
  onOpenPlayground: (modelId?: string) => void;
  models: ModelItem[];
}

export const CommandPalette: React.FC<CommandPaletteProps> = ({
  isOpen,
  onClose,
  onNavigate,
  onOpenTopUp,
  onOpenCreateKey,
  onOpenPlayground,
  models
}) => {
  const [query, setQuery] = React.useState('');
  const [copiedUrl, setCopiedUrl] = React.useState(false);
  const inputRef = React.useRef<HTMLInputElement>(null);

  React.useEffect(() => {
    if (isOpen) {
      setTimeout(() => inputRef.current?.focus(), 50);
    } else {
      setQuery('');
    }
  }, [isOpen]);

  // Global keydown handler for ESC
  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  if (!isOpen) return null;

  const handleCopyBaseUrl = () => {
    navigator.clipboard.writeText('https://api.uzllm.uz/v1');
    setCopiedUrl(true);
    setTimeout(() => {
      setCopiedUrl(false);
      onClose();
    }, 1000);
  };

  const navItems: { id: ViewType; label: string; icon: React.ReactNode; group: string }[] = [
    { id: 'overview', label: 'Umumiy tahlil (Overview)', icon: <LayoutDashboard className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'apikeys', label: 'API Kalitlar (Keys)', icon: <KeyRound className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'models', label: 'Modellar katalogi (Catalog)', icon: <Cpu className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'billing', label: "Hamyon & To'lovlar (Billing)", icon: <CreditCard className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'activity', label: 'So\'rovlar tarixi (Logs)', icon: <Activity className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'routing', label: 'Routing & Failover', icon: <GitFork className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'alerts', label: 'Telegram & Alerts', icon: <BellRing className="w-4 h-4" />, group: 'Sahifalar' },
    { id: 'team', label: 'Jamoa & Sozlamalar', icon: <Users className="w-4 h-4" />, group: 'Sahifalar' },
  ];

  const filteredNav = navItems.filter(item =>
    item.label.toLowerCase().includes(query.toLowerCase())
  );

  const filteredModels = models.filter(m =>
    m.name.toLowerCase().includes(query.toLowerCase()) ||
    m.id.toLowerCase().includes(query.toLowerCase()) ||
    m.provider.toLowerCase().includes(query.toLowerCase())
  );

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-24 bg-black/70 backdrop-blur-sm p-4">
      <div
        className="w-full max-w-xl rounded-xl bg-zinc-950 border border-zinc-800 shadow-2xl overflow-hidden divide-y divide-zinc-800 animate-in fade-in zoom-in-95 duration-150"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Search Input Bar */}
        <div className="flex items-center px-4 py-3">
          <Search className="w-4 h-4 text-zinc-500 shrink-0 mr-3" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Buyruq, sahifa yoki model qidiring... (Esc - yopish)"
            className="w-full bg-transparent text-sm text-zinc-200 placeholder-zinc-500 focus:outline-none"
          />
          {query && (
            <button
              onClick={() => setQuery('')}
              className="text-xs text-zinc-500 hover:text-zinc-300 px-1.5 py-0.5 rounded bg-zinc-900"
            >
              Tozalash
            </button>
          )}
        </div>

        {/* Results List */}
        <div className="max-h-96 overflow-y-auto p-2 space-y-4 text-xs">
          {/* Quick Actions */}
          <div>
            <div className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider px-2 py-1">
              Tezkor Harakatlar
            </div>
            <div className="space-y-0.5">
              <button
                onClick={() => { onOpenTopUp(); onClose(); }}
                className="w-full flex items-center justify-between px-2.5 py-2 rounded-lg text-zinc-300 hover:text-white hover:bg-zinc-900 transition text-left cursor-pointer"
              >
                <div className="flex items-center space-x-2.5">
                  <div className="w-6 h-6 rounded bg-emerald-500/10 text-emerald-400 flex items-center justify-center">
                    <Plus className="w-3.5 h-3.5" />
                  </div>
                  <span>Balansni to'ldirish (Payme / CLICK)</span>
                </div>
                <span className="text-[10px] text-zinc-500 font-mono">Top-up</span>
              </button>

              <button
                onClick={() => { onOpenCreateKey(); onClose(); }}
                className="w-full flex items-center justify-between px-2.5 py-2 rounded-lg text-zinc-300 hover:text-white hover:bg-zinc-900 transition text-left cursor-pointer"
              >
                <div className="flex items-center space-x-2.5">
                  <div className="w-6 h-6 rounded bg-zinc-800 text-zinc-300 flex items-center justify-center">
                    <KeyRound className="w-3.5 h-3.5" />
                  </div>
                  <span>Yangi API kalit yaratish</span>
                </div>
                <span className="text-[10px] text-zinc-500 font-mono">Create Key</span>
              </button>

              <button
                onClick={handleCopyBaseUrl}
                className="w-full flex items-center justify-between px-2.5 py-2 rounded-lg text-zinc-300 hover:text-white hover:bg-zinc-900 transition text-left cursor-pointer"
              >
                <div className="flex items-center space-x-2.5">
                  <div className="w-6 h-6 rounded bg-zinc-800 text-zinc-300 flex items-center justify-center">
                    {copiedUrl ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                  </div>
                  <span>OpenAI Base URL nusxalash (https://api.uzllm.uz/v1)</span>
                </div>
                <span className="text-[10px] text-zinc-500 font-mono">Base URL</span>
              </button>
            </div>
          </div>

          {/* Navigation Pages */}
          {filteredNav.length > 0 && (
            <div>
              <div className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider px-2 py-1">
                Bo'limlar
              </div>
              <div className="space-y-0.5">
                {filteredNav.map((item) => (
                  <button
                    key={item.id}
                    onClick={() => { onNavigate(item.id); onClose(); }}
                    className="w-full flex items-center justify-between px-2.5 py-2 rounded-lg text-zinc-300 hover:text-white hover:bg-zinc-900 transition text-left cursor-pointer"
                  >
                    <div className="flex items-center space-x-2.5">
                      <span className="text-zinc-400">{item.icon}</span>
                      <span>{item.label}</span>
                    </div>
                    <ArrowRight className="w-3 h-3 text-zinc-600" />
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* Models */}
          {filteredModels.length > 0 && (
            <div>
              <div className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider px-2 py-1">
                Modellar ({filteredModels.length})
              </div>
              <div className="space-y-0.5">
                {filteredModels.slice(0, 5).map((m) => (
                  <button
                    key={m.id}
                    onClick={() => { onOpenPlayground(m.id); onClose(); }}
                    className="w-full flex items-center justify-between px-2.5 py-2 rounded-lg text-zinc-300 hover:text-white hover:bg-zinc-900 transition text-left cursor-pointer"
                  >
                    <div className="flex items-center space-x-2.5">
                      <div className="w-6 h-6 rounded bg-zinc-800 text-zinc-400 flex items-center justify-center font-mono text-[10px]">
                        AI
                      </div>
                      <div>
                        <span className="font-medium text-zinc-200">{m.name}</span>
                        <span className="text-zinc-500 ml-1.5 font-mono text-[11px]">{m.id}</span>
                      </div>
                    </div>
                    <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-900 text-zinc-400 font-mono">
                      ${m.inputPricePer1M}/1M
                    </span>
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Footer Shortcut Guide */}
        <div className="px-4 py-2 bg-zinc-900/50 flex items-center justify-between text-[11px] text-zinc-500 font-mono">
          <div className="flex items-center space-x-3">
            <span>↑↓ tanlash</span>
            <span>↵ ochish</span>
            <span>esc chiqish</span>
          </div>
          <span className="text-zinc-400">UZLLM Gateway</span>
        </div>
      </div>
    </div>
  );
};
