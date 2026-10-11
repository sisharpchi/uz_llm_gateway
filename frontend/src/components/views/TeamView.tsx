import React from 'react';
import { Organization, TeamMember } from '../../types';
import { Users, UserPlus, CheckCircle2, MoreVertical, Building } from 'lucide-react';

interface TeamViewProps {
  currentOrg: Organization;
  members: TeamMember[];
  onInviteMember: (email: string, role: any) => void;
}

export const TeamView: React.FC<TeamViewProps> = ({ currentOrg, members, onInviteMember }) => {
  const [inviteEmail, setInviteEmail] = React.useState('');
  const [inviteRole, setInviteRole] = React.useState<'Admin' | 'Developer' | 'Billing'>('Developer');
  const [showInviteModal, setShowInviteModal] = React.useState(false);

  const handleInvite = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inviteEmail) return;
    onInviteMember(inviteEmail, inviteRole);
    setInviteEmail('');
    setShowInviteModal(false);
  };

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-zinc-800/80 gap-3">
        <div>
          <h1 className="text-xl font-semibold text-zinc-100 tracking-tight">
            Jamoa va Rollar
          </h1>
          <p className="text-xs text-zinc-500 mt-0.5">
            Tashkilot loyihalari, API kalitlari va moliyaviy hisobotlarga kirish huquqlarini boshqarish
          </p>
        </div>

        <button
          onClick={() => setShowInviteModal(true)}
          className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
        >
          <UserPlus className="w-3.5 h-3.5" />
          <span>A'zo Taklif Qilish</span>
        </button>
      </div>

      {/* Organization Meta Card */}
      <div className="p-4 rounded-xl bg-zinc-900/40 border border-zinc-800/80 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex items-center space-x-3">
          <div className="w-9 h-9 rounded-lg bg-zinc-900 border border-zinc-800 flex items-center justify-center text-zinc-300">
            <Building className="w-4 h-4" />
          </div>
          <div>
            <div className="flex items-center space-x-2">
              <h2 className="text-sm font-semibold text-zinc-100">{currentOrg.name}</h2>
              <span className="px-1.5 py-0.2 rounded text-[10px] font-mono bg-zinc-800 text-zinc-400">
                {currentOrg.tier}
              </span>
            </div>
            <p className="text-[11px] text-zinc-500 font-mono">slug: {currentOrg.slug}</p>
          </div>
        </div>

        <div className="flex items-center space-x-5 text-xs text-zinc-400 font-mono">
          <div>
            <span className="block text-zinc-500 text-[10px] uppercase">A'zolar</span>
            <span className="text-xs font-medium text-zinc-200">{members.length} / 15</span>
          </div>
          <div>
            <span className="block text-zinc-500 text-[10px] uppercase">RPM Limit</span>
            <span className="text-xs font-medium text-zinc-200">5,000 RPM</span>
          </div>
        </div>
      </div>

      {/* Members Minimal Table */}
      <div className="rounded-xl bg-zinc-900/40 border border-zinc-800/80 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-zinc-800 bg-zinc-950/60 text-zinc-500 font-medium text-[11px]">
                <th className="py-2.5 px-3.5">A'zo</th>
                <th className="py-2.5 px-3.5">Rol</th>
                <th className="py-2.5 px-3.5">Status</th>
                <th className="py-2.5 px-3.5">Qo'shilgan</th>
                <th className="py-2.5 px-3.5 text-right">Amal</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/60 text-[11px]">
              {members.map((member) => (
                <tr key={member.id} className="hover:bg-zinc-900/50 transition">
                  <td className="py-3 px-3.5">
                    <div className="flex items-center space-x-2.5">
                      <div className="w-7 h-7 rounded-full bg-zinc-800 flex items-center justify-center text-[10px] font-medium text-zinc-200">
                        {member.name.split(' ').map(n => n[0]).join('')}
                      </div>
                      <div>
                        <div className="font-medium text-zinc-200">{member.name}</div>
                        <div className="text-[10px] text-zinc-500 font-mono">{member.email}</div>
                      </div>
                    </div>
                  </td>

                  <td className="py-3 px-3.5">
                    <span className="px-2 py-0.5 rounded bg-zinc-800 text-zinc-300 font-mono text-[10px]">
                      {member.role}
                    </span>
                  </td>

                  <td className="py-3 px-3.5">
                    <span className="inline-flex items-center space-x-1 px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 text-[10px] font-medium">
                      <CheckCircle2 className="w-3 h-3" />
                      <span>Faol</span>
                    </span>
                  </td>

                  <td className="py-3 px-3.5 text-zinc-500 font-mono">
                    {member.joinedAt}
                  </td>

                  <td className="py-3 px-3.5 text-right">
                    {member.role !== 'Owner' && (
                      <button className="text-zinc-500 hover:text-zinc-200 p-1 rounded transition cursor-pointer">
                        <MoreVertical className="w-3.5 h-3.5" />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Invite Modal */}
      {showInviteModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4">
          <div className="w-full max-w-sm rounded-xl bg-zinc-950 border border-zinc-800 shadow-2xl p-5 space-y-4">
            <div>
              <h3 className="text-sm font-semibold text-zinc-100">Yangi A'zo Taklif Qilish</h3>
              <p className="text-xs text-zinc-500 mt-0.5">
                Kolleganingizning email manzilini kiriting
              </p>
            </div>

            <form onSubmit={handleInvite} className="space-y-3.5">
              <div>
                <label className="text-[11px] font-medium text-zinc-400 block mb-1">Email manzil</label>
                <input
                  type="email"
                  required
                  placeholder="developer@kompaniya.uz"
                  value={inviteEmail}
                  onChange={(e) => setInviteEmail(e.target.value)}
                  className="w-full px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-700"
                />
              </div>

              <div>
                <label className="text-[11px] font-medium text-zinc-400 block mb-1">Roli</label>
                <select
                  value={inviteRole}
                  onChange={(e) => setInviteRole(e.target.value as any)}
                  className="w-full px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 focus:outline-none focus:border-zinc-700 cursor-pointer"
                >
                  <option value="Developer">Developer (Faqat API kalitlar va loglar)</option>
                  <option value="Billing">Billing (To'lovlar va hisobotlar)</option>
                  <option value="Admin">Admin (To'liq boshqaruv)</option>
                </select>
              </div>

              <div className="flex items-center justify-end space-x-2 pt-2">
                <button
                  type="button"
                  onClick={() => setShowInviteModal(false)}
                  className="px-3 py-1.5 rounded-lg bg-zinc-900 hover:bg-zinc-800 text-zinc-400 text-xs font-medium transition cursor-pointer"
                >
                  Bekor qilish
                </button>
                <button
                  type="submit"
                  className="px-3 py-1.5 rounded-lg bg-white hover:bg-zinc-200 text-zinc-950 text-xs font-medium transition cursor-pointer shadow-xs active:scale-[0.98]"
                >
                  Taklif Yuborish
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
