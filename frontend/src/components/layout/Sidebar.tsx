import React from 'react';
import { ViewType } from '../../types';
import {
  LayoutDashboard,
  KeyRound,
  Cpu,
  CreditCard,
  Activity,
  GitFork,
  BellRing,
  Users,
  Code2,
  Copy,
  Check,
  Globe
} from 'lucide-react';

interface SidebarProps {
  currentView: ViewType;
  onSelectView: (view: ViewType) => void;
  onOpenCommandPalette: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ currentView, onSelectView, onOpenCommandPalette }) => {
  const [copiedUrl, setCopiedUrl] = React.useState(false);

  const navItems: { id: ViewType; label: string; icon: React.ReactNode; badge?: string }[] = [
    { id: 'landing', label: 'Bosh sahifa (Landing)', icon: <Globe className="w-4 h-4" /> },
    { id: 'overview', label: 'Umumiy tahlil', icon: <LayoutDashboard className="w-4 h-4" /> },
    { id: 'apikeys', label: 'API Kalitlar', icon: <KeyRound className="w-4 h-4" />, badge: '3' },
    { id: 'models', label: 'Modellar', icon: <Cpu className="w-4 h-4" />, badge: '7' },
    { id: 'billing', label: "Hamyon & To'lovlar", icon: <CreditCard className="w-4 h-4" /> },
    { id: 'activity', label: 'So\'rovlar tarixi', icon: <Activity className="w-4 h-4" /> },
    { id: 'routing', label: 'Routing & Failover', icon: <GitFork className="w-4 h-4" /> },
    { id: 'alerts', label: 'Telegram & Alerts', icon: <BellRing className="w-4 h-4" /> },
    { id: 'team', label: 'Jamoa sozlamalari', icon: <Users className="w-4 h-4" /> },
  ];

  const handleCopyBaseUrl = () => {
    navigator.clipboard.writeText('https://api.uzllm.uz/v1');
    setCopiedUrl(true);
    setTimeout(() => setCopiedUrl(false), 2000);
  };

  return (
    <aside className="w-60 border-r border-zinc-800/80 bg-zinc-950 flex flex-col justify-between shrink-0 h-[calc(100vh-3.5rem)] select-none">
      {/* Navigation List */}
      <div className="p-3 space-y-1">
        <div className="text-[10px] font-medium text-zinc-500 px-2.5 py-1 uppercase tracking-wider">
          Gateway Boshqaruvi
        </div>
        {navItems.map((item) => {
          const isActive = currentView === item.id;
          return (
            <button
              key={item.id}
              onClick={() => onSelectView(item.id)}
              className={`w-full flex items-center justify-between px-2.5 py-2 rounded-lg text-xs font-medium transition cursor-pointer ${
                isActive
                  ? 'bg-zinc-900 text-zinc-100 shadow-xs'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/60'
              }`}
            >
              <div className="flex items-center space-x-2.5">
                <span className={isActive ? 'text-zinc-100' : 'text-zinc-400'}>
                  {item.icon}
                </span>
                <span>{item.label}</span>
              </div>
              {item.badge && (
                <span className={`text-[10px] px-1.5 py-0.2 rounded font-mono ${
                  isActive
                    ? 'bg-zinc-800 text-zinc-300'
                    : 'bg-zinc-900 text-zinc-500'
                }`}>
                  {item.badge}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {/* Bottom Minimalist Info Box & Status */}
      <div className="p-3 border-t border-zinc-800/80 space-y-2.5">
        {/* OpenAI Compatible API Box */}
        <div className="p-2.5 rounded-lg bg-zinc-900/50 border border-zinc-800/80 space-y-1.5">
          <div className="flex items-center justify-between">
            <span className="text-[10px] font-medium text-zinc-400 flex items-center space-x-1">
              <Code2 className="w-3 h-3 text-zinc-400" />
              <span>OpenAI Base URL</span>
            </span>
            <button
              onClick={handleCopyBaseUrl}
              className="text-zinc-400 hover:text-zinc-200 p-0.5 rounded transition cursor-pointer"
              title="Nusxalash"
            >
              {copiedUrl ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
            </button>
          </div>
          <div className="text-[11px] font-mono text-zinc-300 truncate bg-zinc-950 px-2 py-1 rounded border border-zinc-800/80">
            https://api.uzllm.uz/v1
          </div>
        </div>

        {/* System Uptime Status */}
        <div className="flex items-center justify-between px-1 text-[11px]">
          <div className="flex items-center space-x-1.5 text-zinc-400">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
            <span>Operatsion holat</span>
          </div>
          <span className="text-[10px] font-mono text-zinc-500">99.99%</span>
        </div>
      </div>
    </aside>
  );
};
