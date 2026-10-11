import React from 'react';
import { ModelItem } from '../../types';
import {
  X,
  Terminal,
  Send,
  Loader2,
  Bot,
  User,
  Clock,
  Coins
} from 'lucide-react';

interface PlaygroundDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  models: ModelItem[];
  defaultModelId?: string;
}

export const PlaygroundDrawer: React.FC<PlaygroundDrawerProps> = ({
  isOpen,
  onClose,
  models,
  defaultModelId = 'claude-3-5-sonnet'
}) => {
  const [selectedModel, setSelectedModel] = React.useState(defaultModelId);
  const [inputMessage, setInputMessage] = React.useState('');
  const [messages, setMessages] = React.useState<{ role: 'user' | 'assistant'; content: string }[]>([
    { role: 'assistant', content: "Assalomu alaykum! Men UZLLM Gateway shlyuzi orqali ulangan AI yordamchisiman. Modellar tezligi, o'tkazuvchanligi va narxlarini shu yerda to'g'ridan-to'g'ri sinab ko'rishingiz mumkin." }
  ]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [lastStats, setLastStats] = React.useState<{ tokens: number; cost: number; latency: number } | null>(null);

  React.useEffect(() => {
    if (defaultModelId) {
      setSelectedModel(defaultModelId);
    }
  }, [defaultModelId]);

  if (!isOpen) return null;

  const currentModelObj = models.find(m => m.id === selectedModel) || models[0];

  const handleSend = () => {
    if (!inputMessage.trim() || isLoading) return;

    const userText = inputMessage;
    setInputMessage('');
    setMessages(prev => [...prev, { role: 'user', content: userText }]);
    setIsLoading(true);

    setTimeout(() => {
      let reply = '';
      if (selectedModel.includes('claude')) {
        reply = `Claude 3.5 Sonnet: "${userText}" bo'yicha tahlil yakunlandi. O'zbekiston shlyuzidan tranzit kechikishi minimal darajada saqlandi.`;
      } else if (selectedModel.includes('deepseek')) {
        reply = `DeepSeek-V3: So'rovingiz yuqori samaradorlik va optimal token narxida bajarildi.`;
      } else if (selectedModel.includes('gpt-4o')) {
        reply = `GPT-4o: Javob tayyor. Har qanday kod generatsiyasi yoki tahlil so'rovlarini qabul qilishga tayyorman.`;
      } else {
        reply = `Gemini 2.5 Flash: Tezkor va keng kontekstli javob taqdim etildi.`;
      }

      const approxTokens = Math.round((userText.length + reply.length) / 3.5);
      const approxCost = (approxTokens / 1000000) * currentModelObj.outputPricePer1M;

      setMessages(prev => [...prev, { role: 'assistant', content: reply }]);
      setLastStats({
        tokens: approxTokens,
        cost: approxCost,
        latency: Math.round(currentModelObj.latencyAvgMs * (0.8 + Math.random() * 0.4))
      });
      setIsLoading(false);
    }, 600);
  };

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/75 backdrop-blur-sm">
      <div className="w-full max-w-xl bg-zinc-950 border-l border-zinc-800 h-full flex flex-col justify-between shadow-2xl animate-in slide-in-from-right duration-200">
        {/* Top Header */}
        <div className="p-3.5 border-b border-zinc-800 flex items-center justify-between bg-zinc-950">
          <div className="flex items-center space-x-2">
            <div className="w-6 h-6 rounded-md bg-zinc-800 flex items-center justify-center text-zinc-300">
              <Terminal className="w-3.5 h-3.5" />
            </div>
            <div>
              <h2 className="text-xs font-semibold text-zinc-200">API Playground</h2>
              <p className="text-[10px] text-zinc-500 font-mono">api.uzllm.uz/v1/chat/completions</p>
            </div>
          </div>

          <div className="flex items-center space-x-2">
            {/* Model Selector */}
            <select
              value={selectedModel}
              onChange={(e) => setSelectedModel(e.target.value)}
              className="px-2.5 py-1 rounded-md bg-zinc-900 border border-zinc-700 text-xs font-mono text-zinc-200 focus:outline-none focus:border-zinc-500 cursor-pointer"
            >
              {models.map(m => (
                <option key={m.id} value={m.id}>
                  {m.name} ({m.provider})
                </option>
              ))}
            </select>

            <button
              onClick={onClose}
              className="text-zinc-500 hover:text-zinc-200 p-1 rounded-md transition cursor-pointer"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* Chat Messages Body */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3 text-xs">
          {messages.map((m, idx) => (
            <div
              key={idx}
              className={`flex items-start space-x-2 ${
                m.role === 'user' ? 'justify-end' : 'justify-start'
              }`}
            >
              {m.role === 'assistant' && (
                <div className="w-6 h-6 rounded-md bg-zinc-900 border border-zinc-800 text-zinc-300 flex items-center justify-center shrink-0 mt-0.5">
                  <Bot className="w-3.5 h-3.5" />
                </div>
              )}
              <div
                className={`max-w-[85%] rounded-lg p-2.5 leading-relaxed ${
                  m.role === 'user'
                    ? 'bg-zinc-800 text-zinc-100'
                    : 'bg-zinc-900/60 border border-zinc-800 text-zinc-300'
                }`}
              >
                {m.content}
              </div>
              {m.role === 'user' && (
                <div className="w-6 h-6 rounded-md bg-zinc-800 text-zinc-300 flex items-center justify-center shrink-0 mt-0.5">
                  <User className="w-3.5 h-3.5" />
                </div>
              )}
            </div>
          ))}

          {isLoading && (
            <div className="flex items-center space-x-2 text-xs text-zinc-400 p-2 font-mono">
              <Loader2 className="w-3.5 h-3.5 animate-spin text-zinc-400" />
              <span>{currentModelObj.name} javob qaytarmoqda...</span>
            </div>
          )}
        </div>

        {/* Bottom Input Area */}
        <div className="p-3.5 border-t border-zinc-800 bg-zinc-950 space-y-2.5">
          {lastStats && (
            <div className="flex items-center justify-between text-[11px] text-zinc-500 px-1 font-mono">
              <span className="flex items-center space-x-1">
                <Clock className="w-3 h-3 text-zinc-600" />
                <span>Kechikish: {lastStats.latency}ms</span>
              </span>
              <span className="flex items-center space-x-1 text-emerald-400 font-medium">
                <Coins className="w-3 h-3" />
                <span>Xarajat: ${lastStats.cost.toFixed(6)} ({lastStats.tokens} tok)</span>
              </span>
            </div>
          )}

          <div className="flex items-center space-x-2">
            <input
              type="text"
              placeholder="Prompt yozing..."
              value={inputMessage}
              onChange={(e) => setInputMessage(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSend()}
              className="flex-1 px-3 py-2 rounded-lg bg-zinc-900 border border-zinc-800 text-xs text-zinc-100 placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
            />
            <button
              onClick={handleSend}
              disabled={isLoading || !inputMessage.trim()}
              className="p-2 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 transition cursor-pointer disabled:opacity-50 shadow-xs active:scale-[0.98]"
            >
              <Send className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
