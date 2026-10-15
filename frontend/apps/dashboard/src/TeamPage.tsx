import { formatDateTime } from './LocaleFormatting';
import { t } from './I18n';
import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { ApiError, management, type Organization, type Project, type TeamInvitation, type TeamMember, type TeamRole } from '@uzllm/api-client';
import { ActionFeedback, LifecycleDialog, LoadError, useLifecycleAction } from './LifecycleControls';
import './Lifecycle.css';
import './TeamPage.css';

const roles: TeamRole[] = ['Owner', 'Admin', 'Developer', 'BillingViewer', 'ReadOnly'];
const roleLabel = (role: string) => role === 'BillingViewer' ? 'Billing viewer' : role === 'ReadOnly' ? 'Read-only' : role;
const projectScoped = (role: string) => role === 'Developer' || role === 'ReadOnly';
const manager = (role?: string) => role === 'Owner' || role === 'Admin';

async function teamCommand<T>(command: () => Promise<T>): Promise<T> {
  try { return await command(); }
  catch (cause) {
    if (cause instanceof ApiError && cause.status === 409)
      throw new Error('Conflict: an active member/invitation may already exist, the last owner cannot be removed, or this role/grant transition is no longer valid. Refresh before retrying; no automatic retry was made.');
    throw cause;
  }
}

function invitationStatus(invitation: TeamInvitation): string {
  if (invitation.acceptedAt) return 'Accepted';
  if (invitation.revokedAt) return 'Revoked';
  return Date.parse(invitation.expiresAt) <= Date.now() ? 'Expired' : 'Pending';
}

export function TeamPage({ organization, accountId, projects }: { organization: Organization; accountId: string; projects: Project[] }) {
  const client = useQueryClient();
  const membersKey = ['team-members', accountId, organization.id];
  const invitationsKey = ['team-invitations', accountId, organization.id];
  const members = useQuery({ queryKey: membersKey, queryFn: ({ signal }) => management.teamMembers(organization.id, signal), staleTime: 0 });
  const invitations = useQuery({ queryKey: invitationsKey, queryFn: ({ signal }) => management.teamInvitations(organization.id, signal), staleTime: 0, refetchInterval: 60_000 });
  const actor = members.data?.find(member => member.accountId === accountId && member.status === 'Active');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [action, setAction] = useState<MemberAction | null>(null);
  const owners = members.data?.filter(member => member.status === 'Active' && member.role === 'Owner').length ?? 0;
  const current = members.data?.find(member => member.accountId === selected);
  function refresh() {
    void client.invalidateQueries({ queryKey: membersKey });
    void client.invalidateQueries({ queryKey: invitationsKey });
    void client.invalidateQueries({ queryKey: ['projects', organization.id] });
    void client.invalidateQueries({ queryKey: ['organizations'] });
  }
  const waiting = members.isPending || (!members.isFetchedAfterMount && members.isFetching);
  const denied = members.isError || !manager(actor?.role);
  const visible = members.data?.filter(member => `${member.email} ${member.role} ${member.status} ${t(roleLabel(member.role))} ${t(member.status)}`.toLowerCase().includes(search.toLowerCase())) ?? [];
  return <div className="lifecycle-page team-page"><div className="page-heading"><span className="eyebrow">{t("ORGANIZATION ACCESS · ")}{organization.name}</span>
    <h1>{t("Team")}</h1><p>{t("Invite people, assign the right role and grant only the projects they need.")}</p></div>
    <div className="team-toolbar"><Link className="button secondary" to="/accept-invitation">{t("Accept an invitation")}</Link>
      <button className="button secondary" onClick={refresh}>{t("Refresh team")}</button></div>
    {waiting ? <p role="status">{t("Loading team access…")}</p> : denied ? <section className="panel stack"><h2>{t("Owner or Admin access required")}</h2>
      {members.isError ? <LoadError error={members.error} retry={refresh} /> : <p role="alert" className="error">{t("Your current membership cannot manage this team. Refresh or ask an organization owner.")}</p>}
      <p className="muted">{t("Team emails, invitations and mutation controls are not available to Developer, Billing viewer or Read-only roles.")}</p></section> : <>
      <p className="lifecycle-callout">{t("You are ")}{t(roleLabel(actor!.role))}{t(" in ")}{organization.name}{t(". Admins cannot invite, change or revoke Admins/Owners. The last active Owner is protected. Every operation is re-authorized by the API.")}</p>
      <div className="lifecycle-overview"><section className="panel"><div className="section-title"><h2>{t("Members")}</h2><span className="tag">{members.data?.length ?? 0}{t(" total")}</span></div>
        <label className="stack">{t("Search members")}<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder={t("Email, role or status")} /></label>
        {visible.length ? <ul className="row-list team-members">{visible.map(member => {
          const protectedRole = actor!.role === 'Admin' && (member.role === 'Owner' || member.role === 'Admin');
          const lastOwner = member.status === 'Active' && member.role === 'Owner' && owners <= 1;
          const editable = member.status === 'Active' && !protectedRole;
          return <li key={member.accountId}><div><strong>{member.email}{member.accountId === accountId ? t(" (you)") : ''}</strong>
            <small>{t(roleLabel(member.role))}{t(" · Joined ")}{formatDateTime(new Date(member.createdAt), true)}</small>
            <small>{projectScoped(member.role) ? t("{p0} explicit project grants", { p0: member.projectIds.length }) : t("Organization-wide role permissions")}</small>
            {lastOwner && <small>{t("Last active Owner · cannot demote or revoke")}</small>}
            {protectedRole && <small>{t("Only an Owner may change this member")}</small>}</div>
            <div className="row-actions"><span className={`tag ${member.status === 'Active' ? 'status-active' : ''}`}>{t(member.status)}</span>
              <button className="button secondary" disabled={!editable || lastOwner} onClick={() => setAction({ kind: 'role', member })}>{t("Change role for ")}{member.email}</button>
              {projectScoped(member.role) && <button className="button secondary" disabled={!editable} aria-expanded={selected === member.accountId}
                onClick={() => setSelected(selected === member.accountId ? null : member.accountId)}>{t("Project access for ")}{member.email}</button>}
              <button className="text-button danger-text" disabled={!editable || lastOwner} onClick={() => setAction({ kind: 'revoke', member })}>{t("Revoke ")}{member.email}</button>
            </div></li>;
        })}</ul> : <p className="muted">{t("No matching members.")}</p>}</section>
        <InviteMemberForm key={actor!.role} organizationId={organization.id} role={actor!.role} refresh={refresh} /></div>
      {current?.status === 'Active' && projectScoped(current.role) && <ProjectAccess key={current.accountId} member={current} projects={projects} select={setAction} />}
      <section className="panel"><div className="section-title"><h2>{t("Invitations")}</h2><span className="tag">{t("One use · 7 days")}</span></div>
        {invitations.dataUpdatedAt > 0 && <small className="muted">{t("Last checked ")}{formatDateTime(new Date(invitations.dataUpdatedAt))}{t(". Status uses browser time; acceptance is decided by the API.")}</small>}
        <p className="muted">{t("The token is sent only by email, never returned here. Pending means issued, not proof of delivery. Expired invitations can be replaced by a new invite; pending duplicates return a conflict. Cancel/resend is not supported by the current API.")}</p>
        {invitations.isPending ? <p role="status">{t("Loading invitations…")}</p> : invitations.isError ? <LoadError error={invitations.error} retry={() => void invitations.refetch()} />
          : invitations.data.length ? <ul className="row-list">{invitations.data.map(invitation => <li key={invitation.id}>
            <div><strong>{invitation.email}</strong><small>{t(roleLabel(invitation.role))}{t(" · Expires ")}{new Date(invitation.expiresAt).toISOString()}{t(" (UTC)")}</small></div>
            <span className="tag">{t(invitationStatus(invitation))}</span></li>)}</ul> : <p className="muted">{t("No invitations yet.")}</p>}</section>
      {action && <MemberActionDialog key={`${action.member.accountId}:${action.kind}:${'project' in action ? action.project.id : ''}`} action={action} organization={organization}
        actorId={accountId} actorRole={actor!.role} close={() => setAction(null)} refresh={refresh} />}
    </>}
  </div>;
}

function InviteMemberForm({ organizationId, role, refresh }: { organizationId: string; role: string; refresh: () => void }) {
  const [email, setEmail] = useState(''); const [inviteRole, setRole] = useState<Exclude<TeamRole, 'Owner'>>('Developer');
  const action = useLifecycleAction();
  async function submit(event: FormEvent) {
    event.preventDefault();
    await action.run(() => teamCommand(() => management.inviteMember(organizationId, email.trim(), inviteRole)),
      () => { setEmail(''); }, 'Invitation issued. Ask the recipient to check their email; delivery may still be pending.', refresh);
  }
  return <form className="panel stack" aria-label={t("Invite member")} onSubmit={submit}><span className="eyebrow">{t("GROW YOUR TEAM")}</span><h2>{t("Invite a member")}</h2>
    <label>{t("Member email")}<input type="email" value={email} maxLength={320} required autoComplete="off" onChange={event => setEmail(event.target.value)} /></label>
    <label>{t("Invitation role")}<select value={inviteRole} onChange={event => setRole(event.target.value as Exclude<TeamRole, 'Owner'>)}>
      {roles.filter(value => value !== 'Owner' && (role === 'Owner' || value !== 'Admin')).map(value => <option key={value} value={value}>{t(roleLabel(value))}</option>)}</select></label>
    <p className="muted">{t("Developer and Read-only start with no project grants. Billing viewer reads billing/usage but cannot spend or change budgets. Owner cannot be granted by invitation.")}</p>
    <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>{t("Send invitation")}</button></form>;
}

type MemberAction = { member: TeamMember } & ({ kind: 'role' } | { kind: 'revoke' } | { kind: 'grant' | 'ungrant'; project: Project });
function MemberActionDialog({ action: request, organization, actorId, actorRole, close, refresh }: {
  action: MemberAction; organization: Organization; actorId: string; actorRole: string; close: () => void; refresh: () => void;
}) {
  const action = useLifecycleAction(); const [role, setRole] = useState<TeamRole>(roles.includes(request.member.role as TeamRole) ? request.member.role as TeamRole : 'ReadOnly');
  const member = request.member;
  const title = request.kind === 'role' ? 'Change member role' : request.kind === 'revoke' ? 'Revoke member access' : request.kind === 'grant' ? 'Grant project access' : 'Remove project access';
  async function confirm() {
    await action.run(() => teamCommand(() => request.kind === 'role' ? management.changeMemberRole(organization.id, member.accountId, role)
      : request.kind === 'revoke' ? management.revokeMember(organization.id, member.accountId)
        : request.kind === 'grant' ? management.grantMemberProject(organization.id, member.accountId, request.project.id)
          : management.revokeMemberProject(organization.id, member.accountId, request.project.id)), close, 'Team access updated.', refresh);
  }
  return <LifecycleDialog title={t(title)} close={close}><p><strong>{member.email}</strong>{t(" in ")}<strong>{organization.name}</strong></p>
    {request.kind === 'role' ? <><label className="stack">{t("New role")}<select value={role} onChange={event => setRole(event.target.value as TeamRole)}>
      {roles.filter(value => actorRole === 'Owner' || (value !== 'Owner' && value !== 'Admin')).map(value => <option key={value} value={value}>{t(roleLabel(value))}</option>)}</select></label>
      <p>{t("Role changes apply on the next authorized request. Switching to Owner/Admin/Billing viewer clears explicit project grants; switching back requires granting projects again.")}</p></>
      : request.kind === 'revoke' ? <p>{t("This revokes organization membership and removes every project grant. History remains. Rejoining requires a fresh invitation.")}</p>
        : <p>{request.kind === 'grant' ? t("Allow") : t("Remove")}{t(" explicit access to ")}<strong>{request.project.name}</strong>{t(". Other grants are unchanged.")}</p>}
    {member.accountId === actorId && <p className="error">{t("You are changing your own access and may lose this workspace immediately.")}</p>}
    <ActionFeedback {...action} /><div className="row-actions"><button className="button secondary" autoFocus onClick={close} data-dialog-dismiss="">{t("Cancel")}</button>
      <button className="button danger" disabled={action.busy || (request.kind === 'role' && role === member.role)} onClick={() => void confirm()}>{t("Confirm change")}</button></div>
  </LifecycleDialog>;
}

function ProjectAccess({ member, projects, select }: { member: TeamMember; projects: Project[]; select: (action: MemberAction) => void }) {
  const available = projects.filter(project => project.status === 0 && !member.projectIds.includes(project.id));
  const [projectId, setProjectId] = useState('');
  const chosen = available.find(project => project.id === projectId);
  return <section className="panel stack"><h2>{t("Project access · ")}{member.email}</h2><p className="muted">{t("Only explicit grants count for Developer and Read-only. Each grant/removal is a separate audited API operation, not a bulk replacement.")}</p>
    {member.projectIds.length ? <ul className="row-list">{member.projectIds.map(id => {
      const project = projects.find(value => value.id === id);
      return <li key={id}><strong>{project?.name ?? id}{project?.status === 1 ? t(" (Archived)") : ''}</strong>
        <button className="button secondary" disabled={!project} onClick={() => project && select({ kind: 'ungrant', member, project })}>{t("Remove access to ")}{project?.name ?? id}</button></li>;
    })}</ul> : <p className="notice">{t("No project access. This member cannot access project-scoped resources yet.")}</p>}
    <div className="control-pair"><label>{t("Project to grant")}<select value={chosen?.id ?? ''} onChange={event => setProjectId(event.target.value)}>
      <option value="">{t("Choose an active project")}</option>{available.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <button className="button secondary" disabled={!chosen} onClick={() => chosen && select({ kind: 'grant', member, project: chosen })}>{t("Grant project access")}</button></div>
  </section>;
}
