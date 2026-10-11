import React from 'react';
import {
  ViewType,
  Currency,
  Organization,
  Project,
  ApiKeyItem,
  ModelItem,
  TransactionItem,
  UsageLogItem,
  TeamMember,
  AlertConfig
} from './types';
import {
  mockOrganizations,
  mockProjects,
  mockApiKeys,
  mockModels,
  mockTransactions,
  mockUsageLogs,
  mockTeamMembers,
  mockAlertConfig,
  CURRENT_EXCHANGE_RATE
} from './data/mockData';

import { LandingPage } from './components/landing/LandingPage';
import { Header } from './components/layout/Header';
import { Sidebar } from './components/layout/Sidebar';
import { OverviewView } from './components/views/OverviewView';
import { ApiKeysView } from './components/views/ApiKeysView';
import { ModelsView } from './components/views/ModelsView';
import { BillingView } from './components/views/BillingView';
import { ActivityView } from './components/views/ActivityView';
import { RoutingView } from './components/views/RoutingView';
import { AlertsView } from './components/views/AlertsView';
import { TeamView } from './components/views/TeamView';

import { TopUpModal } from './components/modals/TopUpModal';
import { CreateKeyModal } from './components/modals/CreateKeyModal';
import { PlaygroundDrawer } from './components/modals/PlaygroundDrawer';
import { CommandPalette } from './components/modals/CommandPalette';

export function App() {
  // Navigation & View state - Default to 'landing' as requested by the user
  const [currentView, setCurrentView] = React.useState<ViewType>('landing');
  const [currency, setCurrency] = React.useState<Currency>('USD');

  // Organizations & Projects state
  const [organizations, setOrganizations] = React.useState<Organization[]>(mockOrganizations);
  const [currentOrg, setCurrentOrg] = React.useState<Organization>(mockOrganizations[0]);
  const [projects, setProjects] = React.useState<Project[]>(mockProjects);
  const [currentProject, setCurrentProject] = React.useState<Project>(mockProjects[0]);

  // Data state
  const [apiKeys, setApiKeys] = React.useState<ApiKeyItem[]>(mockApiKeys);
  const [models, setModels] = React.useState<ModelItem[]>(mockModels);
  const [transactions, setTransactions] = React.useState<TransactionItem[]>(mockTransactions);
  const [usageLogs, setUsageLogs] = React.useState<UsageLogItem[]>(mockUsageLogs);
  const [teamMembers, setTeamMembers] = React.useState<TeamMember[]>(mockTeamMembers);
  const [alertConfig, setAlertConfig] = React.useState<AlertConfig>(mockAlertConfig);

  // Modals state
  const [topUpOpen, setTopUpOpen] = React.useState(false);
  const [createKeyOpen, setCreateKeyOpen] = React.useState(false);
  const [playgroundOpen, setPlaygroundOpen] = React.useState(false);
  const [playgroundModelId, setPlaygroundModelId] = React.useState('claude-3-5-sonnet');
  const [commandPaletteOpen, setCommandPaletteOpen] = React.useState(false);

  // Global Ctrl+K / Cmd+K listener
  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        setCommandPaletteOpen(prev => !prev);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Currency toggle
  const handleToggleCurrency = () => {
    setCurrency(prev => prev === 'USD' ? 'UZS' : 'USD');
  };

  // Top Up Success Handler
  const handleTopUpSuccess = (amountUzs: number, amountUsd: number, method: 'Payme' | 'CLICK') => {
    const updatedBalanceUsd = currentOrg.balanceUsd + amountUsd;
    const updatedBalanceUzs = updatedBalanceUsd * CURRENT_EXCHANGE_RATE;

    const updatedOrg = {
      ...currentOrg,
      balanceUsd: updatedBalanceUsd,
      balanceUzs: updatedBalanceUzs
    };

    setCurrentOrg(updatedOrg);
    setOrganizations(prev => prev.map(o => o.id === updatedOrg.id ? updatedOrg : o));

    const newTx: TransactionItem = {
      id: `tx_${method.toLowerCase()}_${Date.now()}`,
      timestamp: new Date().toISOString(),
      paymentMethod: method,
      amountUzs,
      creditedUsd: amountUsd,
      exchangeRate: CURRENT_EXCHANGE_RATE,
      status: 'Completed',
      externalTransactionId: `${method.toUpperCase()}-UZ-${Math.floor(100000000 + Math.random() * 900000000)}`,
      receiptUrl: `https://${method.toLowerCase()}.uz/receipt/mock-${Date.now()}`
    };

    setTransactions(prev => [newTx, ...prev]);
  };

  // API Key handlers
  const handleCreateKey = (newKey: ApiKeyItem) => {
    setApiKeys(prev => [newKey, ...prev]);
  };

  const handleRevokeKey = (keyId: string) => {
    setApiKeys(prev => prev.map(k => k.id === keyId ? { ...k, status: 'revoked' } : k));
  };

  // Model toggle handler
  const handleToggleModel = (modelId: string) => {
    setModels(prev => prev.map(m => m.id === modelId ? { ...m, enabled: !m.enabled } : m));
  };

  const handleOpenPlaygroundWithModel = (modelId?: string) => {
    if (modelId) {
      setPlaygroundModelId(modelId);
    }
    setPlaygroundOpen(true);
  };

  // Team invite handler
  const handleInviteMember = (email: string, role: any) => {
    const newMember: TeamMember = {
      id: `mem_${Date.now()}`,
      name: email.split('@')[0],
      email,
      role,
      status: 'active',
      joinedAt: new Date().toISOString().split('T')[0]
    };
    setTeamMembers(prev => [...prev, newMember]);
  };

  return (
    <div className="min-h-screen bg-black text-zinc-100 flex flex-col font-sans selection:bg-zinc-200 selection:text-zinc-900">
      {/* If Landing Page mode, render Full Landing Page */}
      {currentView === 'landing' ? (
        <LandingPage
          onEnterDashboard={() => setCurrentView('overview')}
          onOpenCommandPalette={() => setCommandPaletteOpen(true)}
        />
      ) : (
        /* Otherwise, render the Full Dashboard Interface */
        <div className="min-h-screen flex flex-col bg-zinc-950">
          {/* Top Header */}
          <Header
            currentOrg={currentOrg}
            organizations={organizations}
            onSelectOrg={setCurrentOrg}
            currentProject={currentProject}
            projects={projects}
            onSelectProject={setCurrentProject}
            currency={currency}
            onToggleCurrency={handleToggleCurrency}
            onOpenTopUp={() => setTopUpOpen(true)}
            onOpenPlayground={() => setPlaygroundOpen(true)}
            onOpenCommandPalette={() => setCommandPaletteOpen(true)}
            onGoToLanding={() => setCurrentView('landing')}
          />

          {/* Main Layout (Sidebar + Content) */}
          <div className="flex-1 flex overflow-hidden">
            {/* Left Sidebar */}
            <Sidebar
              currentView={currentView}
              onSelectView={setCurrentView}
              onOpenCommandPalette={() => setCommandPaletteOpen(true)}
            />

            {/* Center / Right Scrollable Canvas */}
            <main className="flex-1 overflow-y-auto p-4 md:p-6 bg-zinc-950">
              <div className="max-w-6xl mx-auto">
                {currentView === 'overview' && (
                  <OverviewView
                    currentOrg={currentOrg}
                    currentProject={currentProject}
                    currency={currency}
                    recentLogs={usageLogs}
                    onOpenTopUp={() => setTopUpOpen(true)}
                    onOpenCreateKey={() => setCreateKeyOpen(true)}
                    onNavigate={setCurrentView}
                  />
                )}

                {currentView === 'apikeys' && (
                  <ApiKeysView
                    apiKeys={apiKeys}
                    onOpenCreateKey={() => setCreateKeyOpen(true)}
                    onRevokeKey={handleRevokeKey}
                  />
                )}

                {currentView === 'models' && (
                  <ModelsView
                    models={models}
                    onToggleModel={handleToggleModel}
                    onOpenPlaygroundWithModel={handleOpenPlaygroundWithModel}
                  />
                )}

                {currentView === 'billing' && (
                  <BillingView
                    currentOrg={currentOrg}
                    transactions={transactions}
                    currency={currency}
                    onOpenTopUp={() => setTopUpOpen(true)}
                  />
                )}

                {currentView === 'activity' && (
                  <ActivityView
                    logs={usageLogs}
                  />
                )}

                {currentView === 'routing' && (
                  <RoutingView />
                )}

                {currentView === 'alerts' && (
                  <AlertsView
                    config={alertConfig}
                    onSaveConfig={setAlertConfig}
                  />
                )}

                {currentView === 'team' && (
                  <TeamView
                    currentOrg={currentOrg}
                    members={teamMembers}
                    onInviteMember={handleInviteMember}
                  />
                )}
              </div>
            </main>
          </div>
        </div>
      )}

      {/* Interactive Modals (Shared across Landing and Dashboard) */}
      <TopUpModal
        isOpen={topUpOpen}
        onClose={() => setTopUpOpen(false)}
        onSuccess={handleTopUpSuccess}
      />

      <CreateKeyModal
        isOpen={createKeyOpen}
        projects={projects}
        currentProjectId={currentProject.id}
        onClose={() => setCreateKeyOpen(false)}
        onCreated={handleCreateKey}
      />

      <PlaygroundDrawer
        isOpen={playgroundOpen}
        onClose={() => setPlaygroundOpen(false)}
        models={models}
        defaultModelId={playgroundModelId}
      />

      {/* Global Command Palette (Ctrl+K / Cmd+K) */}
      <CommandPalette
        isOpen={commandPaletteOpen}
        onClose={() => setCommandPaletteOpen(false)}
        onNavigate={setCurrentView}
        onOpenTopUp={() => setTopUpOpen(true)}
        onOpenCreateKey={() => setCreateKeyOpen(true)}
        onOpenPlayground={handleOpenPlaygroundWithModel}
        models={models}
      />
    </div>
  );
}

export default App;
