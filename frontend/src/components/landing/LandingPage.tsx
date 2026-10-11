import React from 'react';
import {
  ArrowRight,
  Check,
  Copy,
  Terminal,
  Cpu,
  ShieldCheck,
  Zap,
  Activity,
  GitFork,
  Lock,
  Server,
  LineChart,
  Sliders,
  ChevronDown,
  ChevronUp,
  ExternalLink,
  Sparkles,
  KeyRound,
  CreditCard,
  Search,
  CheckCircle2,
  Globe
} from 'lucide-react';

interface LandingPageProps {
  onEnterDashboard: () => void;
  onOpenCommandPalette: () => void;
}

export const LandingPage: React.FC<LandingPageProps> = ({ onEnterDashboard, onOpenCommandPalette }) => {
  const [copiedCode, setCopiedCode] = React.useState(false);
  const [activeCodeTab, setActiveCodeTab] = React.useState<'python' | 'ts' | 'curl' | 'csharp'>('python');
  const [openFaq, setOpenFaq] = React.useState<number | null>(0);

  const codeSnippets = {
    python: `from openai import OpenAI

# OpenAI SDK kodini o'zgartirish shart emas — faqat base_url va API key bering
client = OpenAI(
    base_url="https://api.uzllm.uz/v1",
    api_key="uzllm_live_your_secret_key"
)

response = client.chat.completions.create(
    model="claude-3-5-sonnet", # yoki "gpt-4o", "deepseek-chat-v3"
    messages=[{"role": "user", "content": "Salom, O'zbekiston AI ekotizimi!"}]
)

print(response.choices[0].message.content)`,
    ts: `import OpenAI from "openai";

const client = new OpenAI({
  baseURL: "https://api.uzllm.uz/v1",
  apiKey: "uzllm_live_your_secret_key",
});

const response = await client.chat.completions.create({
  model: "gpt-4o",
  messages: [{ role: "user", content: "Assalomu alaykum!" }],
});

console.log(response.choices[0].message.content);`,
    curl: `curl https://api.uzllm.uz/v1/chat/completions \\
  -H "Content-Type: application/json" \\
  -H "Authorization: Bearer uzllm_live_your_secret_key" \\
  -d '{
    "model": "deepseek-chat-v3",
    "messages": [{"role": "user", "content": "Toshkent IT ekotizimi haqida yoz"}]
  }'`,
    csharp: `using System.Net.Http.Json;

// .NET 10 HttpClient orqali UZLLM Gateway'ga ulanish
using var client = new HttpClient { BaseAddress = new Uri("https://api.uzllm.uz/v1/") };
client.DefaultRequestHeaders.Add("Authorization", "Bearer uzllm_live_your_secret_key");

var payload = new {
    model = "claude-3-5-sonnet",
    messages = new[] { new { role = "user", content = "Salom C# .NET 10 dan!" } }
};

var response = await client.PostAsJsonAsync("chat/completions", payload);
Console.WriteLine(await response.Content.ReadAsStringAsync());`
  };

  const handleCopyCode = () => {
    navigator.clipboard.writeText(codeSnippets[activeCodeTab]);
    setCopiedCode(true);
    setTimeout(() => setCopiedCode(false), 2000);
  };

  const faqs = [
    {
      q: "UZLLM Gateway'ning OpenRouter va boshqa xizmatlardan asosiy farqi nimada?",
      a: "UZLLM Gateway O'zbekiston bozori uchun maxsus moslashtirilgan. U Payme va CLICK orqali so'mda (UZS) to'lovlarni qabul qiladi, fiksatsiyalangan kurs bilan to'lovlarni qayd etadi, mahalliy qonunchilikka mos fiskal chek taqdim etadi va Toshkent tranzit tuguni orqali eng past kechikish (latency) bilan ishlaydi."
    },
    {
      q: "Mavjud OpenAI SDK kodimni o'zgartirishim kerakmi?",
      a: "Yo'q, mutlaqo shart emas! UZLLM Gateway OpenAI API spetsifikatsiyasi bilan 100% mos keladi. Siz faqat client sozlamasida base_url=\"https://api.uzllm.uz/v1\" va UZLLM kalitini ko'rsatsangiz kifoya."
    },
    {
      q: "Qaysi AI modellarni qo'llab-quvvatlaysiz?",
      a: "Biz barcha yetakchi provayderlar modellarini qo'llab-quvvatlaymiz: OpenAI (GPT-4o, o3-mini, GPT-4o Mini), Anthropic (Claude 3.5 Sonnet, Claude 3.5 Haiku), Google (Gemini 2.5 Flash, Pro), DeepSeek (DeepSeek-V3, DeepSeek-R1) va boshqa 40+ modellarga bitta kalit bilan ulanish mumkin."
    },
    {
      q: "Provayder uzilib qolsa tizim qanday yo'l tutadi (Failover)?",
      a: "UZLLM Gateway avtomatik routing va failover tizimiga ega. Agar asosiy provayder (masalan OpenAI) javob bermasa yoki 5xx xato qaytarsa, so'rovingiz avtomatik tarzda ikkinchi zaxira provayderga (masalan Claude yoki DeepSeek) yo'naltiriladi."
    },
    {
      q: "Hisobni to'ldirish qanday amalga oshiriladi?",
      a: "Hamyoningizni Payme yoki CLICK orqali istalgan milliy karta (UzCard, Humo, Visa/Mastercard) yordamida bir necha soniyada to'ldirishingiz mumkin. Komissiya 0%, mablag' bir zumda balansingizga USD ekvivalentida o'tiradi."
    }
  ];

  return (
    <div className="min-h-screen bg-black text-zinc-100 font-sans selection:bg-zinc-200 selection:text-zinc-950 overflow-x-hidden">
      {/* 1. TOP ANNOUNCEMENT BAR */}
      <div className="w-full bg-zinc-950 border-b border-zinc-800/80 px-4 py-2 text-center text-xs text-zinc-400 flex items-center justify-center space-x-2">
        <span className="px-2 py-0.5 rounded-full bg-white/10 text-zinc-200 font-mono text-[10px] font-medium border border-white/10">
          YANGILIK
        </span>
        <span>O'zbekiston AI Gateway — Payme & CLICK orqali hisob to'ldirish va 200+ modellar integratsiyasi!</span>
        <button
          onClick={onEnterDashboard}
          className="text-white hover:underline font-medium inline-flex items-center space-x-0.5 ml-1 cursor-pointer"
        >
          <span>Dashboardni ochish</span>
          <ArrowRight className="w-3 h-3" />
        </button>
      </div>

      {/* 2. NAVIGATION BAR */}
      <header className="sticky top-0 z-40 w-full border-b border-zinc-900 bg-black/80 backdrop-blur-md px-6 lg:px-12 h-16 flex items-center justify-between">
        <div className="flex items-center space-x-8">
          {/* Logo */}
          <div className="flex items-center space-x-2.5">
            <div className="w-7 h-7 rounded-lg bg-white flex items-center justify-center text-black font-bold text-xs shadow-sm">
              UZ
            </div>
            <span className="font-bold text-base tracking-tight text-white">UZLLM Gateway</span>
          </div>

          {/* Nav Links */}
          <nav className="hidden md:flex items-center space-x-6 text-xs text-zinc-400">
            <a href="#capabilities" className="hover:text-white transition">Imkoniyatlar</a>
            <a href="#how-it-works" className="hover:text-white transition">Qanday ishlaydi</a>
            <a href="#reliability" className="hover:text-white transition">Ishonchlilik</a>
            <a href="#pricing" className="hover:text-white transition">Narxlar</a>
            <a href="#faq" className="hover:text-white transition">FAQ</a>
            <button
              onClick={onEnterDashboard}
              className="text-zinc-400 hover:text-white transition cursor-pointer"
            >
              Modellar (200+)
            </button>
          </nav>
        </div>

        {/* Right Nav Actions */}
        <div className="flex items-center space-x-3">
          <button
            onClick={onOpenCommandPalette}
            className="hidden sm:flex items-center space-x-2 px-3 py-1.5 rounded-full border border-zinc-800 bg-zinc-950 hover:bg-zinc-900 text-xs text-zinc-400 hover:text-zinc-200 transition cursor-pointer"
          >
            <Search className="w-3 h-3 text-zinc-500" />
            <span>Qidirish...</span>
            <span className="px-1.5 py-0.5 text-[9px] font-mono text-zinc-500 rounded bg-zinc-900 border border-zinc-800">⌘K</span>
          </button>

          <button
            onClick={onEnterDashboard}
            className="px-3 py-1.5 rounded-lg text-xs font-medium text-zinc-300 hover:text-white hover:bg-zinc-900 transition cursor-pointer"
          >
            Kirish
          </button>

          <button
            onClick={onEnterDashboard}
            className="flex items-center space-x-1 px-4 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-semibold transition cursor-pointer shadow-sm active:scale-[0.98]"
          >
            <span>Dashboardga O'tish</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </header>

      {/* 3. HERO SECTION (Matching Page 1 of PDF) */}
      <section className="relative pt-16 pb-20 px-6 max-w-6xl mx-auto text-center space-y-8">
        {/* Certification Badge */}
        <div className="inline-flex items-center space-x-2 px-3.5 py-1 rounded-full border border-zinc-800 bg-zinc-950 text-xs text-zinc-400 shadow-sm">
          <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
          <span>UZLLM Gateway — O'zbekistonning birinchi AI shlyuzi</span>
          <span className="text-zinc-600">•</span>
          <span className="text-zinc-200 font-medium">PCI-DSS & SOC 2 tayyor</span>
        </div>

        {/* Main Headline */}
        <div className="space-y-4 max-w-4xl mx-auto">
          <h1 className="text-4xl sm:text-5xl lg:text-6xl font-extrabold tracking-tight text-white leading-[1.12]">
            UZLLM Gateway — Yagona API orqali <br />
            <span className="text-zinc-400 font-bold">40+ provayderlarga</span>, jumladan OpenAI, Anthropic va Google'ga ulaning
          </h1>
          <p className="text-base sm:text-lg text-zinc-400 max-w-2xl mx-auto leading-relaxed">
            Ko'plab API kalitlar va to'lov muammolarini unuting. 200+ modellarga Payme va CLICK orqali to'lov qiling, real vaqtda xarajatlarni kuzating va kodingizni o'zgartirmasdan provayderlarni almashtiring.
          </p>
        </div>

        {/* Hero CTA Button */}
        <div className="flex flex-col sm:flex-row items-center justify-center gap-3 pt-2">
          <button
            onClick={onEnterDashboard}
            className="px-8 py-3.5 rounded-xl bg-white hover:bg-zinc-200 text-black font-semibold text-sm shadow-xl transition flex items-center space-x-2 cursor-pointer active:scale-[0.98]"
          >
            <span>API Kalit Olish (Dashboard)</span>
            <ArrowRight className="w-4 h-4" />
          </button>
          <a
            href="#dropin-code"
            className="px-6 py-3.5 rounded-xl bg-zinc-900 hover:bg-zinc-800 text-zinc-300 font-medium text-sm border border-zinc-800 transition"
          >
            1 daqiqalik kod integratsiyasi
          </a>
        </div>

        {/* Trust bullet points */}
        <div className="flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-xs text-zinc-500 pt-1">
          <span className="flex items-center space-x-1.5 text-zinc-400">
            <Check className="w-3.5 h-3.5 text-emerald-400" />
            <span>BYOK (O'z kalitlaringiz) — bepul</span>
          </span>
          <span className="flex items-center space-x-1.5 text-zinc-400">
            <Check className="w-3.5 h-3.5 text-emerald-400" />
            <span>Payme & CLICK orqali UZS to'lov</span>
          </span>
          <span className="flex items-center space-x-1.5 text-zinc-400">
            <Check className="w-3.5 h-3.5 text-emerald-400" />
            <span>30 soniyada o'rnatish</span>
          </span>
        </div>

        {/* Switching from another provider badges */}
        <div className="pt-4 text-xs text-zinc-500 flex flex-wrap items-center justify-center gap-2">
          <span>Boshqa xizmatdan ko'chmoqchimisiz?</span>
          <span className="px-2 py-0.5 rounded bg-zinc-900 border border-zinc-800 text-zinc-300 font-mono text-[11px]">OpenRouter</span>
          <span className="px-2 py-0.5 rounded bg-zinc-900 border border-zinc-800 text-zinc-300 font-mono text-[11px]">LiteLLM</span>
          <span className="px-2 py-0.5 rounded bg-zinc-900 border border-zinc-800 text-zinc-300 font-mono text-[11px]">LangChain</span>
          <span className="px-2 py-0.5 rounded bg-zinc-900 border border-zinc-800 text-zinc-300 font-mono text-[11px]">GitHub Copilot</span>
        </div>

        {/* 4. PRODUCT DASHBOARD PREVIEW MOCKUP (Matching Page 1 screenshot) */}
        <div className="pt-8">
          <div
            onClick={onEnterDashboard}
            className="rounded-2xl border border-zinc-800 bg-zinc-950 shadow-2xl p-4 sm:p-6 text-left relative overflow-hidden group cursor-pointer hover:border-zinc-700 transition"
          >
            {/* Top Mockup Window Bar */}
            <div className="flex items-center justify-between pb-4 border-b border-zinc-800/80 mb-5">
              <div className="flex items-center space-x-2">
                <div className="w-3 h-3 rounded-full bg-zinc-800"></div>
                <div className="w-3 h-3 rounded-full bg-zinc-800"></div>
                <div className="w-3 h-3 rounded-full bg-zinc-800"></div>
                <span className="text-xs text-zinc-500 font-mono ml-2">app.uzllm.uz/dashboard</span>
              </div>
              <div className="flex items-center space-x-2">
                <span className="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-mono font-medium">
                  ● Production API Active
                </span>
                <span className="text-xs text-zinc-400 group-hover:text-white transition flex items-center space-x-1">
                  <span>Dashboardga o'tish</span>
                  <ArrowRight className="w-3 h-3" />
                </span>
              </div>
            </div>

            {/* Dashboard Inner KPI Row */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 mb-6">
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800/80">
                <div className="text-[10px] text-zinc-500 uppercase font-medium">Hamyon Balansi</div>
                <div className="text-lg font-bold font-mono text-zinc-100 mt-1">$12,500.00</div>
                <div className="text-[10px] text-emerald-400 font-mono">≈ 162,500,000 UZS</div>
              </div>
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800/80">
                <div className="text-[10px] text-zinc-500 uppercase font-medium">Jami So'rovlar</div>
                <div className="text-lg font-bold font-mono text-zinc-100 mt-1">11,898</div>
                <div className="text-[10px] text-zinc-500 font-mono">99.98% muvaffaqiyat</div>
              </div>
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800/80">
                <div className="text-[10px] text-zinc-500 uppercase font-medium">Bugungi Xarajat</div>
                <div className="text-lg font-bold font-mono text-zinc-100 mt-1">$289.23</div>
                <div className="text-[10px] text-zinc-500 font-mono">1.2M token</div>
              </div>
              <div className="p-3.5 rounded-xl bg-zinc-900/60 border border-zinc-800/80">
                <div className="text-[10px] text-zinc-500 uppercase font-medium">Asosiy Model</div>
                <div className="text-lg font-bold font-mono text-zinc-100 mt-1">Claude 3.5 Sonnet</div>
                <div className="text-[10px] text-zinc-500 font-mono">Anthropic (Failover: GPT-4o)</div>
              </div>
            </div>

            {/* Interactive Graph preview banner */}
            <div className="p-4 rounded-xl bg-zinc-900/30 border border-zinc-800/80 flex items-center justify-between text-xs text-zinc-400">
              <div className="flex items-center space-x-3">
                <Activity className="w-4 h-4 text-emerald-400" />
                <span>Jonli inferensiya oqimi: O'rtacha kechikish <b>312ms</b>, kesh zarbasi <b>34%</b></span>
              </div>
              <span className="text-zinc-300 font-mono text-[11px] underline">To'liq boshqaruv panelini ochish →</span>
            </div>
          </div>
        </div>

        {/* 5. PROVIDERS LOGOS GRID */}
        <div className="pt-12 border-t border-zinc-900">
          <p className="text-[11px] uppercase tracking-wider text-zinc-500 font-medium mb-6">
            Barcha global va ochiq kodli AI provayderlari bilan integratsiyalashgan
          </p>
          <div className="flex flex-wrap items-center justify-center gap-6 sm:gap-10 opacity-70 grayscale hover:grayscale-0 transition duration-300">
            <span className="font-bold text-sm text-zinc-300 tracking-wider">OpenAI</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">Anthropic</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">Google DeepMind</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">DeepSeek</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">Groq</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">AWS Bedrock</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">Meta Llama</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">Mistral AI</span>
            <span className="font-bold text-sm text-zinc-300 tracking-wider">Perplexity</span>
          </div>
        </div>
      </section>

      {/* 6. PLATFORM CAPABILITIES (Matching Page 2 of PDF) */}
      <section id="capabilities" className="py-20 px-6 max-w-6xl mx-auto border-t border-zinc-900">
        <div className="text-center max-w-2xl mx-auto mb-16 space-y-3">
          <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest block">
            PLATFORM CAPABILITIES
          </span>
          <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-white">
            Ishonch bilan ishlab chiqarishga chiqing
          </h2>
          <p className="text-sm text-zinc-400">
            UZLLM Gateway ishlab chiquvchilar va korxonalar uchun barqaror, tezkor va audit qilinadigan infratuzilmani taqdim etadi.
          </p>
        </div>

        {/* Top 3 Primary Cards */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-5 mb-6">
          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 space-y-3 hover:border-zinc-700 transition">
            <div className="w-9 h-9 rounded-lg bg-zinc-900 flex items-center justify-center text-white mb-2">
              <Terminal className="w-5 h-5" />
            </div>
            <h3 className="text-base font-semibold text-white">Unified API Interface</h3>
            <p className="text-xs text-zinc-400 leading-relaxed">
              Mavjud OpenAI SDK kodingizni saqlab qoling — shunchaki base_url manzilini o'zgartiring va barcha yangi modellarga bir zumda ulaning.
            </p>
          </div>

          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 space-y-3 hover:border-zinc-700 transition">
            <div className="w-9 h-9 rounded-lg bg-zinc-900 flex items-center justify-center text-white mb-2">
              <Cpu className="w-5 h-5" />
            </div>
            <h3 className="text-base font-semibold text-white">Multi-provider Support</h3>
            <p className="text-xs text-zinc-400 leading-relaxed">
              40+ provayderlar va 200+ modellarga bitta shartnoma va hisob orqali ulaning. Hech qanday vendor lock-in yo'q.
            </p>
          </div>

          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 space-y-3 hover:border-zinc-700 transition">
            <div className="w-9 h-9 rounded-lg bg-zinc-900 flex items-center justify-center text-white mb-2">
              <LineChart className="w-5 h-5" />
            </div>
            <h3 className="text-base font-semibold text-white">Performance Monitoring</h3>
            <p className="text-xs text-zinc-400 leading-relaxed">
              Har bir so'rov bo'yicha kechikish (latency), tokenlar narxi va sifatni real vaqtda taqqoslang va optimal modelni tanlang.
            </p>
          </div>
        </div>

        {/* 8 Feature Sub-Cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <KeyRound className="w-4 h-4 text-zinc-400" />
              <span>Secure Key Management</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              Show-once maxfiy kalitlar, loyiha bo'yicha ajratish va oylik xarajat limitlari.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <CreditCard className="w-4 h-4 text-emerald-400" />
              <span>Payme & CLICK Top-Up</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              UZS da hisobni to'ldirish, qat'iy buxgalteriya balansi va fiskal cheklar.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <GitFork className="w-4 h-4 text-zinc-400" />
              <span>Automatic Failover</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              Provayder uzilsa, so'rov foydalanuvchiga sezilmasdan zaxiradagi modelga o'tadi.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <Zap className="w-4 h-4 text-amber-400" />
              <span>Smart Response Cache</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              Takroriy so'rovlarni Redis keshidan 5ms ichida 0 narx bilan qaytarish.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <Lock className="w-4 h-4 text-zinc-400" />
              <span>Enterprise Audit Logs</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              O'zgartirib bo'lmas (immutable) append-only xavfsizlik va moliya jurnali.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <ShieldCheck className="w-4 h-4 text-zinc-400" />
              <span>LLM Guardrails</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              Prompt injection va zararli so'rovlarni to'xtatuvchi intellektual xavfsizlik.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <Server className="w-4 h-4 text-zinc-400" />
              <span>Self-hosted or Cloud</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              Bulutda foydalaning yoki to'liq nazorat uchun o'z serveringizga o'rnating.
            </p>
          </div>

          <div className="p-4 rounded-xl bg-zinc-950/60 border border-zinc-900 hover:border-zinc-800 transition space-y-2">
            <div className="text-zinc-300 font-semibold text-xs flex items-center space-x-1.5">
              <Sliders className="w-4 h-4 text-zinc-400" />
              <span>Telegram Xabarnomalar</span>
            </div>
            <p className="text-[11px] text-zinc-500 leading-relaxed">
              Balans kam qolganda yoki xatolar ko'payganda Telegram botdan tezkor xabar.
            </p>
          </div>
        </div>
      </section>

      {/* 7. HOW IT WORKS & ROUTING DIAGRAM (Matching Page 2 of PDF) */}
      <section id="how-it-works" className="py-20 px-6 max-w-6xl mx-auto border-t border-zinc-900">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-12 items-center">
          <div className="space-y-5">
            <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest block">
              HOW IT WORKS
            </span>
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-white leading-tight">
              One request. Any model. <br />
              <span className="text-zinc-500">Bitta so'rov. Istalgan model.</span>
            </h2>
            <p className="text-sm text-zinc-400 leading-relaxed">
              Ilovangiz faqat bitta so'rov yuboradi. UZLLM Gateway shlyuzi uni avtomatik tarzda OpenAI, Anthropic, Google yoki DeepSeek'ka eng arzon va eng tezkor yo'l bilan yetkazadi.
            </p>

            <div className="grid grid-cols-3 gap-4 pt-4 border-t border-zinc-900 font-mono">
              <div>
                <div className="text-2xl font-bold text-white">40+</div>
                <div className="text-[11px] text-zinc-500 font-sans">Provayderlar</div>
              </div>
              <div>
                <div className="text-2xl font-bold text-white">200+</div>
                <div className="text-[11px] text-zinc-500 font-sans">AI Modellar</div>
              </div>
              <div>
                <div className="text-2xl font-bold text-white">&lt;5ms</div>
                <div className="text-[11px] text-zinc-500 font-sans">Shlyuz kechikishi</div>
              </div>
            </div>
          </div>

          {/* Interactive Routing Visualizer Box */}
          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 space-y-4">
            <div className="text-xs font-mono text-zinc-400 pb-2 border-b border-zinc-900 flex justify-between items-center">
              <span>Client Application</span>
              <span className="text-emerald-400">● Live Routing</span>
            </div>

            <div className="space-y-3 font-mono text-xs">
              <div className="p-3 rounded-lg bg-zinc-900/80 border border-zinc-800 text-zinc-200 flex items-center justify-between">
                <span>POST https://api.uzllm.uz/v1/chat/completions</span>
                <span className="text-[10px] text-zinc-500">Inbound</span>
              </div>

              <div className="flex justify-center text-zinc-600 text-xs">
                ↓ (UZLLM Gateway Intelligent Router)
              </div>

              <div className="space-y-2">
                <div className="p-2.5 rounded-lg bg-zinc-950 border border-emerald-500/40 flex items-center justify-between">
                  <div className="flex items-center space-x-2">
                    <span className="w-2 h-2 rounded-full bg-emerald-400"></span>
                    <span className="text-zinc-200">OpenAI GPT-4o</span>
                  </div>
                  <span className="text-emerald-400 text-[11px]">Primary • 240ms</span>
                </div>

                <div className="p-2.5 rounded-lg bg-zinc-950 border border-zinc-800 flex items-center justify-between">
                  <div className="flex items-center space-x-2">
                    <span className="w-2 h-2 rounded-full bg-zinc-600"></span>
                    <span className="text-zinc-400">Claude 3.5 Sonnet</span>
                  </div>
                  <span className="text-zinc-500 text-[11px]">Fallback 1 • Standby</span>
                </div>

                <div className="p-2.5 rounded-lg bg-zinc-950 border border-zinc-800 flex items-center justify-between">
                  <div className="flex items-center space-x-2">
                    <span className="w-2 h-2 rounded-full bg-zinc-600"></span>
                    <span className="text-zinc-400">DeepSeek-V3</span>
                  </div>
                  <span className="text-zinc-500 text-[11px]">Fallback 2 • Standby</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* 8. DROP-IN COMPATIBLE CODE SECTION (Matching Page 2 of PDF) */}
      <section id="dropin-code" className="py-20 px-6 max-w-6xl mx-auto border-t border-zinc-900">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-12 items-center">
          <div className="space-y-5">
            <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest block">
              INTEGRATION
            </span>
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-white leading-tight">
              Drop-in compatible. <br />
              Zero learning curve.
            </h2>
            <p className="text-sm text-zinc-400 leading-relaxed">
              OpenAI rasmiy kutubxonasidan foydalanyapsizmi? Faqat <b>bitta qatorni</b> — <code className="text-zinc-200 font-mono">base_url</code> ni o'zgartiring va siz tayyorsiz. Istalgan dasturlash tili va freymvork bilan ishlaydi.
            </p>

            <ul className="space-y-2 text-xs text-zinc-400">
              <li className="flex items-center space-x-2">
                <Check className="w-4 h-4 text-emerald-400 shrink-0" />
                <span>OpenAI, Anthropic va Vercel AI SDK bilan to'liq mos</span>
              </li>
              <li className="flex items-center space-x-2">
                <Check className="w-4 h-4 text-emerald-400 shrink-0" />
                <span>Streaming (SSE) va JSON Structured Outputs to'liq qo'llab-quvvatlanadi</span>
              </li>
              <li className="flex items-center space-x-2">
                <Check className="w-4 h-4 text-emerald-400 shrink-0" />
                <span>.NET 10, Python, TypeScript, Go, Java va cURL uchun tayyor misollar</span>
              </li>
            </ul>
          </div>

          {/* Interactive Code Window */}
          <div className="rounded-xl bg-zinc-950 border border-zinc-800 overflow-hidden shadow-2xl">
            {/* Tabs */}
            <div className="flex items-center justify-between px-4 py-2.5 bg-zinc-900 border-b border-zinc-800">
              <div className="flex items-center space-x-1">
                {(['python', 'ts', 'curl', 'csharp'] as const).map(lang => (
                  <button
                    key={lang}
                    onClick={() => setActiveCodeTab(lang)}
                    className={`px-3 py-1 rounded text-xs font-mono transition cursor-pointer ${
                      activeCodeTab === lang
                        ? 'bg-zinc-800 text-white font-medium'
                        : 'text-zinc-500 hover:text-zinc-300'
                    }`}
                  >
                    {lang === 'python' ? 'Python' : lang === 'ts' ? 'TypeScript' : lang === 'curl' ? 'cURL' : '.NET / C#'}
                  </button>
                ))}
              </div>

              <button
                onClick={handleCopyCode}
                className="flex items-center space-x-1 text-xs text-zinc-400 hover:text-white transition cursor-pointer"
              >
                {copiedCode ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                <span className="font-mono text-[11px]">{copiedCode ? 'Nusxalandi' : 'Nusxalash'}</span>
              </button>
            </div>

            {/* Code */}
            <div className="p-4 font-mono text-xs text-zinc-300 overflow-x-auto leading-relaxed">
              <pre>{codeSnippets[activeCodeTab]}</pre>
            </div>
          </div>
        </div>
      </section>

      {/* 9. RELIABILITY & FAILOVER (Matching Page 3 of PDF) */}
      <section id="reliability" className="py-20 px-6 max-w-6xl mx-auto border-t border-zinc-900">
        <div className="text-center max-w-2xl mx-auto mb-14 space-y-3">
          <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest block">
            RELIABILITY
          </span>
          <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-white">
            Provayderlar uzilsa ham, siz uzilmaysiz.
          </h2>
          <p className="text-sm text-zinc-400">
            UZLLM Gateway so'rovlarni avtomatik ravishda sog'lom provayderlarga yo'naltiradi. Foydalanuvchilaringiz hech qachon 500 yoki 503 xatolarini ko'rmaydi.
          </p>
        </div>

        {/* Uptime Comparison Box */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 max-w-4xl mx-auto mb-12">
          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 space-y-3">
            <div className="text-xs text-zinc-500 uppercase font-mono">UZLLM Gateway'siz</div>
            <div className="text-3xl font-extrabold text-zinc-400 font-mono">94% uptime</div>
            <div className="text-xs text-rose-400 font-mono">~22 kun uzilish / yiliga</div>
            <p className="text-xs text-zinc-500 leading-relaxed">
              Har bir provayder o'rtacha 94% mustaqil uptime'ga ega. Alohida ishlaganda tizimingiz tez-tez to'xtab qoladi.
            </p>
          </div>

          <div className="p-6 rounded-2xl bg-zinc-950 border border-emerald-500/40 space-y-3 relative overflow-hidden">
            <div className="text-xs text-emerald-400 uppercase font-mono font-semibold">UZLLM Gateway Bilan</div>
            <div className="text-3xl font-extrabold text-white font-mono">99.999% uptime</div>
            <div className="text-xs text-emerald-400 font-mono">&lt; 32 soniya uzilish / yiliga</div>
            <p className="text-xs text-zinc-400 leading-relaxed">
              Avtomatik failover bilan barcha provayderlar bir vaqtda uzilish ehtimoli deyarli 0 ga teng bo'ladi.
            </p>
          </div>
        </div>
      </section>

      {/* 10. PRICING SECTION (Matching Page 4 of PDF) */}
      <section id="pricing" className="py-20 px-6 max-w-6xl mx-auto border-t border-zinc-900">
        <div className="text-center max-w-2xl mx-auto mb-16 space-y-3">
          <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest block">
            PRICING
          </span>
          <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-white">
            Uch xil foydalanish usuli. Ikkisi mutlaqo bepul.
          </h2>
          <p className="text-sm text-zinc-400">
            Obuna to'lovlari yo'q, minimal chegara yo'q. Faqat ishlatsangiz to'laysiz.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {/* Plan 1 */}
          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 flex flex-col justify-between space-y-6 hover:border-zinc-700 transition">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-white">Credits (Prepaid)</h3>
                <span className="text-[11px] font-mono text-emerald-400">Payme / CLICK</span>
              </div>
              <p className="text-xs text-zinc-400">
                Kartangizdan so'mda hisob to'ldiring va barcha 40+ modellarga bitta kalit bilan ulaning.
              </p>
              <div className="text-2xl font-bold text-white font-mono">
                Pay-as-you-go
              </div>
              <ul className="space-y-2 text-xs text-zinc-400 pt-2 border-t border-zinc-900">
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>0% qo'shimcha komissiya</span>
                </li>
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>Payme & CLICK integratsiyasi</span>
                </li>
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>Rasmiy elektron cheklar</span>
                </li>
              </ul>
            </div>

            <button
              onClick={onEnterDashboard}
              className="w-full py-2.5 rounded-xl bg-white hover:bg-zinc-200 text-black font-semibold text-xs transition cursor-pointer"
            >
              Hisobni To'ldirish
            </button>
          </div>

          {/* Plan 2 */}
          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 flex flex-col justify-between space-y-6 hover:border-zinc-700 transition">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-white">Bring Your Own Keys</h3>
                <span className="text-[11px] font-mono text-zinc-400">100% Bepul</span>
              </div>
              <p className="text-xs text-zinc-400">
                O'z shaxsiy OpenAI yoki Anthropic kalitlaringizni kiriting va shlyuz imkoniyatlaridan tekinga foydalaning.
              </p>
              <div className="text-2xl font-bold text-white font-mono">
                $0 / oy
              </div>
              <ul className="space-y-2 text-xs text-zinc-400 pt-2 border-t border-zinc-900">
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>Barcha provayderlar kalitlari</span>
                </li>
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>To'liq routing & failover</span>
                </li>
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>Kengaytirilgan analitika</span>
                </li>
              </ul>
            </div>

            <button
              onClick={onEnterDashboard}
              className="w-full py-2.5 rounded-xl bg-zinc-900 hover:bg-zinc-800 text-zinc-200 font-medium text-xs border border-zinc-800 transition cursor-pointer"
            >
              BYOK bilan boshlash
            </button>
          </div>

          {/* Plan 3 */}
          <div className="p-6 rounded-2xl bg-zinc-950 border border-zinc-800 flex flex-col justify-between space-y-6 hover:border-zinc-700 transition">
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-white">Self-Hosted / Enterprise</h3>
                <span className="text-[11px] font-mono text-zinc-400">On-premise</span>
              </div>
              <p className="text-xs text-zinc-400">
                Banklar va yirik tashkilotlar uchun shaxsiy infratuzilmada o'rnatish va maxsus SLA kafolati.
              </p>
              <div className="text-2xl font-bold text-white font-mono">
                Maxsus tarif
              </div>
              <ul className="space-y-2 text-xs text-zinc-400 pt-2 border-t border-zinc-900">
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>99.999% SLA kafolati</span>
                </li>
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>Shaxsiy VPC va ma'lumotlar xavfsizligi</span>
                </li>
                <li className="flex items-center space-x-2">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>24/7 muhandislik qo'llab-quvvatlashi</span>
                </li>
              </ul>
            </div>

            <button
              onClick={onEnterDashboard}
              className="w-full py-2.5 rounded-xl bg-zinc-900 hover:bg-zinc-800 text-zinc-200 font-medium text-xs border border-zinc-800 transition cursor-pointer"
            >
              Biz bilan bog'lanish
            </button>
          </div>
        </div>
      </section>

      {/* 11. FAQ SECTION (Matching Page 4 of PDF) */}
      <section id="faq" className="py-20 px-6 max-w-4xl mx-auto border-t border-zinc-900">
        <div className="text-center mb-12 space-y-2">
          <span className="text-[11px] font-mono text-zinc-500 uppercase tracking-widest block">
            FAQ
          </span>
          <h2 className="text-3xl font-bold text-white">Ko'p beriladigan savollar</h2>
        </div>

        <div className="space-y-3">
          {faqs.map((faq, index) => {
            const isOpen = openFaq === index;
            return (
              <div
                key={index}
                className="rounded-xl border border-zinc-800/80 bg-zinc-950 overflow-hidden"
              >
                <button
                  onClick={() => setOpenFaq(isOpen ? null : index)}
                  className="w-full px-5 py-4 text-left flex items-center justify-between text-sm font-medium text-zinc-200 hover:text-white transition cursor-pointer"
                >
                  <span>{faq.q}</span>
                  {isOpen ? <ChevronUp className="w-4 h-4 text-zinc-500 shrink-0" /> : <ChevronDown className="w-4 h-4 text-zinc-500 shrink-0" />}
                </button>
                {isOpen && (
                  <div className="px-5 pb-4 text-xs text-zinc-400 leading-relaxed border-t border-zinc-900 pt-3">
                    {faq.a}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </section>

      {/* 12. BOTTOM CTA BANNER (Matching Page 5 of PDF) */}
      <section className="py-20 px-6 max-w-4xl mx-auto text-center space-y-6">
        <h2 className="text-3xl sm:text-5xl font-extrabold text-white tracking-tight">
          30 soniyada so'rovlarni <br />
          yo'naltirishni boshlang
        </h2>
        <p className="text-sm text-zinc-400 max-w-lg mx-auto">
          Hoziroq ro'yxatdan o'ting, API kalitingizni oling va O'zbekistonning eng ilg'or AI shlyuzidan foydalaning.
        </p>
        <button
          onClick={onEnterDashboard}
          className="px-8 py-3 rounded-xl bg-white hover:bg-zinc-200 text-black font-semibold text-sm shadow-xl transition cursor-pointer"
        >
          Bepul Boshlash →
        </button>
      </section>

      {/* 13. FULL FOOTER (Matching Page 5 & 6 of PDF) */}
      <footer className="border-t border-zinc-900 bg-black pt-16 pb-12 px-6 lg:px-12 text-xs text-zinc-500">
        <div className="max-w-6xl mx-auto grid grid-cols-2 md:grid-cols-5 gap-8 mb-12">
          {/* Col 1 */}
          <div className="col-span-2 space-y-3">
            <div className="flex items-center space-x-2">
              <div className="w-6 h-6 rounded bg-white text-black font-bold flex items-center justify-center text-xs">
                UZ
              </div>
              <span className="font-bold text-white text-sm">UZLLM Gateway</span>
            </div>
            <p className="text-[11px] text-zinc-500 max-w-xs leading-relaxed">
              O'zbekiston dasturchilari va bizneslari uchun yagona OpenAI-mos ko'p provayderli AI API shlyuzi.
            </p>
            <div className="flex items-center space-x-2 pt-1 text-[11px] text-emerald-400 font-mono">
              <span className="w-2 h-2 rounded-full bg-emerald-400"></span>
              <span>Barcha tizimlar operatsion holatda</span>
            </div>
          </div>

          {/* Col 2: Mahsulot */}
          <div className="space-y-2">
            <div className="text-zinc-300 font-semibold text-xs">Mahsulot</div>
            <ul className="space-y-1.5 text-zinc-500">
              <li><a href="#capabilities" className="hover:text-zinc-300">Imkoniyatlar</a></li>
              <li><button onClick={onEnterDashboard} className="hover:text-zinc-300 cursor-pointer text-left">API Kalitlar</button></li>
              <li><button onClick={onEnterDashboard} className="hover:text-zinc-300 cursor-pointer text-left">Modellar</button></li>
              <li><button onClick={onEnterDashboard} className="hover:text-zinc-300 cursor-pointer text-left">Hamyon & Billing</button></li>
              <li><a href="#reliability" className="hover:text-zinc-300">Routing & Failover</a></li>
            </ul>
          </div>

          {/* Col 3: Modellar */}
          <div className="space-y-2">
            <div className="text-zinc-300 font-semibold text-xs">Modellar</div>
            <ul className="space-y-1.5 text-zinc-500 font-mono text-[11px]">
              <li>GPT-4o (Omni)</li>
              <li>Claude 3.5 Sonnet</li>
              <li>DeepSeek-V3</li>
              <li>DeepSeek-R1</li>
              <li>Gemini 2.5 Flash</li>
            </ul>
          </div>

          {/* Col 4: Integratsiya */}
          <div className="space-y-2">
            <div className="text-zinc-300 font-semibold text-xs">Integratsiya</div>
            <ul className="space-y-1.5 text-zinc-500">
              <li>Payme Merchant API</li>
              <li>CLICK API</li>
              <li>OpenAI SDK (.NET, Python, JS)</li>
              <li>Telegram Alert Bot</li>
              <li>Docker / On-Premise</li>
            </ul>
          </div>
        </div>

        <div className="max-w-6xl mx-auto pt-8 border-t border-zinc-900 flex flex-col sm:flex-row items-center justify-between text-[11px] text-zinc-600 gap-4">
          <div>
            © 2026 UZLLM Gateway. Barcha huquqlar himoyalangan.
          </div>
          <div className="flex items-center space-x-4">
            <button onClick={onEnterDashboard} className="hover:text-zinc-400 cursor-pointer">Boshqaruv Paneli</button>
            <a href="#pricing" className="hover:text-zinc-400">Maxfiylik Siyosati</a>
            <a href="#faq" className="hover:text-zinc-400">Foydalanish Shartlari</a>
          </div>
        </div>
      </footer>
    </div>
  );
};
