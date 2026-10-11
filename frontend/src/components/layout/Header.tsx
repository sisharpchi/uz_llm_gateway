import React from 'react';
import { Organization, Project, Currency } from '../../types';
import {
  ChevronDown,
  Terminal,
  Search,
  Plus,
  Sparkles,
  Command,
  Check
} from 'lucide-react';

interface HeaderProps {
  currentOrg: Organization;
  organizations: Organization[];
  onSelectOrg: (org: Organization) => void;
  currentProject: Project;
  projects: Project[];
  onSelectProject: (proj: Project) => void;
  currency: Currency;
  onToggleCurrency: () => void;
  onOpenTopUp: () => void;
  onOpenPlayground: () => void;
  onOpenCommandPalette: () => void;
  onGoToLanding?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  currentOrg,
  organizations,
  onSelectOrg,
  currentProject,
  projects,
  onSelectProject,
  currency,
  onToggleCurrency,
  onOpenTopUp,
  onOpenPlayground,
  onOpenCommandPalette,
  onGoToLanding
}) => {
  const [orgOpen, setOrgOpen] = React.useState(false);
  const [projOpen, setProjOpen] = React.useState(false);

  return (
    <header className="sticky top-0 z-30 h-14 border-b border-zinc-800/80 bg-zinc-950/80 backdrop-blur-md px-4 md:px-6 flex items-center justify-between">
      {/* Left: Minimalist Logo + Org/Project switcher */}
      <div className="flex items-center space-x-3 md:space-x-4">
        {/* Brand Link */}
        <button
          onClick={onGoToLanding}
          className="flex items-center space-x-2.5 pr-3 border-r border-zinc-800 hover:opacity-80 transition cursor-pointer text-left"
          title="Bosh sahifaga (Landing) o'tish"
        >
          <div className="w-7 h-7 rounded-lg bg-zinc-100 flex items-center justify-center text-zinc-950 font-bold text-xs shadow-sm">
            UZ
          </div>
          <div className="flex items-center space-x-1.5">
            <span className="font-semibold text-sm tracking-tight text-white">UZLLM</span>
            <span className="text-zinc-500 text-xs font-normal">/ gateway</span>
          </div>
        </button>

        {/* Minimalist Org Selector */}
        <div className="relative">
          <button
            onClick={() => { setOrgOpen(!orgOpen); setProjOpen(false); }}
            className="flex items-center space-x-1.5 px-2.5 py-1 rounded-md text-xs font-medium text-zinc-300 hover:text-white hover:bg-zinc-900 border border-transparent hover:border-zinc-800 transition cursor-pointer"
          >
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
            <span className="truncate max-w-[120px]">{currentOrg.name}</span>
            <ChevronDown className="w-3 h-3 text-zinc-500" />
          </button>

          {orgOpen && (
            <div className="absolute left-0 mt-1.5 w-52 rounded-lg bg-zinc-950 border border-zinc-800 shadow-2xl p-1 z-50">
              <div className="text-[10px] font-medium text-zinc-500 px-2 py-1 uppercase tracking-wider">
                Tashkilot
              </div>
              {organizations.map((org) => (
                <button
                  key={org.id}
                  onClick={() => { onSelectOrg(org); setOrgOpen(false); }}
                  className={`w-full text-left px-2.5 py-1.5 rounded-md text-xs flex items-center justify-between transition cursor-pointer ${
                    org.id === currentOrg.id ? 'bg-zinc-800/80 text-white font-medium' : 'text-zinc-400 hover:text-white hover:bg-zinc-900'
                  }`}
                >
                  <span className="truncate">{org.name}</span>
                  <span className="text-[10px] font-mono text-zinc-500">{org.tier}</span>
                </button>
              ))}
            </div>
          )}
        </div>

        <span className="text-zinc-700 text-xs hidden sm:inline">/</span>

        {/* Minimalist Project Selector */}
        <div className="relative hidden sm:block">
          <button
            onClick={() => { setProjOpen(!projOpen); setOrgOpen(false); }}
            className="flex items-center space-x-1.5 px-2.5 py-1 rounded-md text-xs font-medium text-zinc-300 hover:text-white hover:bg-zinc-900 border border-transparent hover:border-zinc-800 transition cursor-pointer"
          >
            <span className="truncate max-w-[140px]">{currentProject.name}</span>
            <ChevronDown className="w-3 h-3 text-zinc-500" />
          </button>

          {projOpen && (
            <div className="absolute left-0 mt-1.5 w-60 rounded-lg bg-zinc-950 border border-zinc-800 shadow-2xl p-1 z-50">
              <div className="text-[10px] font-medium text-zinc-500 px-2 py-1 uppercase tracking-wider">
                Loyiha
              </div>
              {projects.map((proj) => (
                <button
                  key={proj.id}
                  onClick={() => { onSelectProject(proj); setProjOpen(false); }}
                  className={`w-full text-left px-2.5 py-1.5 rounded-md text-xs flex items-center justify-between transition cursor-pointer ${
                    proj.id === currentProject.id ? 'bg-zinc-800/80 text-white font-medium' : 'text-zinc-400 hover:text-white hover:bg-zinc-900'
                  }`}
                >
                  <span className="truncate">{proj.name}</span>
                  <span className="text-[10px] px-1 py-0.5 rounded bg-zinc-900 text-zinc-400 font-mono">
                    {proj.environment}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Middle: Minimalist Search Button (Command Palette Trigger) */}
      <div className="hidden lg:flex items-center">
        <button
          onClick={onOpenCommandPalette}
          className="flex items-center space-x-2 px-3 py-1.5 rounded-full border border-zinc-800 bg-zinc-900/60 hover:bg-zinc-900 text-xs text-zinc-400 hover:text-zinc-200 transition cursor-pointer w-64 shadow-xs"
        >
          <Search className="w-3.5 h-3.5 text-zinc-500" />
          <span className="flex-1 text-left text-zinc-400 truncate">Qidirish yoki buyruqlar...</span>
          <span className="inline-flex items-center px-1.5 py-0.5 text-[10px] font-mono text-zinc-400 rounded bg-zinc-800 border border-zinc-700/60">
            ⌘K
          </span>
        </button>
      </div>

      {/* Right: Currency Toggle, Minimalist Balance Pill & Actions */}
      <div className="flex items-center space-x-2.5">
        {/* Playground Fast Trigger */}
        <button
          onClick={onOpenPlayground}
          className="hidden sm:flex items-center space-x-1.5 px-2.5 py-1 rounded-md text-xs font-medium text-zinc-300 hover:text-white hover:bg-zinc-900 border border-zinc-800 transition cursor-pointer"
        >
          <Terminal className="w-3.5 h-3.5 text-zinc-400" />
          <span>Playground</span>
        </button>

        {/* Currency Switcher Pill */}
        <button
          onClick={onToggleCurrency}
          title="Valyutani almashtirish"
          className="px-2 py-1 rounded-md text-[11px] font-mono font-medium text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900 border border-zinc-800/80 transition cursor-pointer"
        >
          {currency === 'USD' ? '$ USD' : "so'm UZS"}
        </button>

        {/* Minimalist Balance Pill */}
        <div className="flex items-center space-x-2 pl-2 border-l border-zinc-800">
          <div className="text-right">
            <div className="text-[10px] uppercase font-medium text-zinc-500 tracking-wider">
              Balans
            </div>
            <div className="text-xs font-mono font-semibold text-zinc-100">
              {currency === 'USD'
                ? `$${currentOrg.balanceUsd.toFixed(2)}`
                : `${(currentOrg.balanceUzs).toLocaleString()} so'm`}
            </div>
          </div>

          {/* Minimalist High-Contrast Top-Up Button */}
          <button
            onClick={onOpenTopUp}
            className="flex items-center space-x-1 px-2.5 py-1 rounded-md bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>To'ldirish</span>
          </button>
        </div>

        {/* User Avatar */}
        <div className="w-7 h-7 rounded-full bg-zinc-900 border border-zinc-800 flex items-center justify-center text-[11px] font-medium text-zinc-300 ml-1">
          AK
        </div>
      </div>
    </header>
  );
};
