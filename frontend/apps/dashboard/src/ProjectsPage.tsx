import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { management, type Organization, type Project, type ProjectSettings } from '@uzllm/api-client';
import { ActionFeedback, AllowlistField, allowlistValue, codeListDraft, LifecycleDialog, LoadError, useLifecycleAction } from './LifecycleControls';
import { BudgetsEditor, LimitsEditor } from './LimitsEditor';
import './Lifecycle.css';

export function ProjectsPage({ organization, projects, selected }: { organization: Organization; projects: Project[]; selected?: Project }) {
  const [name, setName] = useState(''); const [archive, setArchive] = useState<Project | null>(null);
  const action = useLifecycleAction(); const client = useQueryClient();
  async function create(event: FormEvent) {
    event.preventDefault();
    await action.run(() => management.createProject(organization.id, name.trim()), () => {
      setName('');
    }, 'Project created. Open it to configure restrictions and budgets.', () => { void client.invalidateQueries({ queryKey: ['projects', organization.id] }); });
  }
  return <div className="lifecycle-page"><div className="page-heading"><span className="eyebrow">WORKSPACE CONTROLS · {organization.name}</span>
    <h1>Projects</h1><p>Separate workloads. Set safe defaults. Keep every key and dollar in the right scope.</p></div>
    <div className="lifecycle-overview"><section className="panel"><div className="section-title"><h2>Your projects</h2><span className="tag">{projects.length} total</span></div>
      {projects.length ? <ul className="row-list project-list">{projects.map(project => <li key={project.id}>
        <div><strong>{project.name}</strong><small className="muted">{project.id}</small></div>
        <div className="row-actions"><span className={`tag ${project.status === 0 ? 'status-active' : ''}`}>{project.status === 0 ? 'Active' : 'Archived'}</span>
          {project.status === 0 && <><Link className="button secondary" to={`/organizations/${organization.id}/projects?project=${project.id}`}>Open {project.name}</Link>
            <button className="text-button danger-text" onClick={() => setArchive(project)}>Archive {project.name}</button></>}
        </div></li>)}</ul> : <p className="muted">No projects yet. Create one to isolate keys, usage and budgets.</p>}</section>
      <form onSubmit={create} className="panel stack"><span className="eyebrow">NEW WORKLOAD</span><h2>Create a project</h2>
        <label>Project name<input value={name} onChange={event => setName(event.target.value)} required minLength={2} maxLength={100} /></label>
        <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>Create project</button>
        <small className="muted">New projects inherit managed execution and operator limits. Configure budgets separately before issuing production keys.</small></form></div>
    {selected?.status === 0 && <ProjectControls key={`${organization.id}:${selected.id}`} organizationId={organization.id} project={selected} />}
    {selected?.status === 1 && <p className="notice">This project is archived. New inference and settings edits are unavailable; financial history is retained.</p>}
    {archive && <ArchiveProjectDialog key={archive.id} organizationId={organization.id} project={archive} close={() => setArchive(null)} />}
  </div>;
}

function ArchiveProjectDialog({ organizationId, project, close }: { organizationId: string; project: Project; close: () => void }) {
  const action = useLifecycleAction(); const client = useQueryClient();
  return <LifecycleDialog title={`Archive ${project.name}?`} close={close}>
    <p>This is permanent: the project cannot be restored. Its keys stop admitting new requests. Existing financial and usage history is retained.</p>
    <ActionFeedback {...action} /><div className="row-actions"><button autoFocus className="button secondary" onClick={close}>Cancel</button>
      <button className="button danger" disabled={action.busy} onClick={() => void action.run(() => management.archiveProject(organizationId, project.id), () => {
        close();
      }, 'Project archived.', () => { void client.invalidateQueries({ queryKey: ['projects', organizationId] }); })}>Confirm archive</button></div>
  </LifecycleDialog>;
}

function ProjectControls({ organizationId, project }: { organizationId: string; project: Project }) {
  const [tab, setTab] = useState<'settings' | 'limits'>('settings');
  return <section className="project-controls"><div className="section-title"><div><span className="eyebrow">SELECTED PROJECT</span><h2>{project.name}</h2></div>
    <Link className="button secondary" to={`/organizations/${organizationId}/keys?project=${project.id}`}>Manage API keys</Link></div>
    <div className="lifecycle-tabs" aria-label="Project controls"><button className="button secondary" aria-pressed={tab === 'settings'} onClick={() => setTab('settings')}>Settings & restrictions</button>
      <button className="button secondary" aria-pressed={tab === 'limits'} onClick={() => setTab('limits')}>Limits & budgets</button></div>
    {tab === 'settings' ? <ProjectSettingsEditor organizationId={organizationId} projectId={project.id} /> : <div className="stack">
      <LimitsEditor organizationId={organizationId} projectId={project.id} /><BudgetsEditor organizationId={organizationId} projectId={project.id} /></div>}
  </section>;
}

export function ProjectSettingsEditor({ organizationId, projectId }: { organizationId: string; projectId: string }) {
  const settings = useQuery({ queryKey: ['project-settings', organizationId, projectId], queryFn: () => management.projectSettings(organizationId, projectId), refetchOnWindowFocus: false });
  return <section className="panel lifecycle-section"><h3>Routing defaults & hard restrictions</h3>
    {settings.isPending || (!settings.isFetchedAfterMount && settings.isFetching) ? <p role="status">Loading project settings…</p> : settings.isError ? <LoadError error={settings.error} retry={() => void settings.refetch()} />
      : <ProjectSettingsForm organizationId={organizationId} projectId={projectId} value={settings.data} />}</section>;
}

function ProjectSettingsForm({ organizationId, projectId, value }: { organizationId: string; projectId: string; value: ProjectSettings }) {
  const [routing, setRouting] = useState<NonNullable<ProjectSettings['routing']> | ''>(value.routing ?? ''); const [environment, setEnvironment] = useState(value.environment ?? '');
  const [models, setModels] = useState(codeListDraft(value.allowedModels)); const [providers, setProviders] = useState(codeListDraft(value.allowedProviders));
  const action = useLifecycleAction(); const client = useQueryClient();
  async function save(event: FormEvent) {
    event.preventDefault();
    await action.run(() => management.setProjectSettings(organizationId, projectId, {
      routing: routing || null, environment: environment.trim() || null, allowedModels: allowlistValue(models), allowedProviders: allowlistValue(providers)
    }), result => {
      setRouting(result.routing ?? ''); setEnvironment(result.environment ?? ''); setModels(codeListDraft(result.allowedModels)); setProviders(codeListDraft(result.allowedProviders));
      client.setQueryData(['project-settings', organizationId, projectId], result);
    }, 'Project settings saved. Future admissions use these restrictions.', () => {
      void client.invalidateQueries({ queryKey: ['project-settings', organizationId, projectId] });
      void client.invalidateQueries({ queryKey: ['projects', organizationId] });
    });
  }
  return <form className="stack" onSubmit={save} aria-label="Project settings">
    <p className="muted">Request routing may override strategy only. Project restrictions, key permissions, budgets, wallet and operator limits always apply. Managed is the default; BYOK/Hybrid still require explicit request consent.</p>
    <div className="control-pair"><label>Default routing<select value={routing} onChange={event => setRouting(event.target.value as NonNullable<ProjectSettings['routing']> | '')}>
      <option value="">Inherit deterministic priority</option><option value="priority">Priority</option><option value="price">Lowest price</option>
      <option value="latency">Lowest latency</option><option value="throughput">Highest throughput</option><option value="auto">Auto</option></select></label>
      <label>Environment tag<input value={environment} maxLength={32} pattern="[A-Za-z0-9_-]{1,32}" placeholder="e.g. Production" onChange={event => setEnvironment(event.target.value)} />
        <small>Metadata only. Does not change billing, permissions or execution mode.</small></label></div>
    <div className="control-pair"><AllowlistField label="Allowed models" value={models} change={setModels} /><AllowlistField label="Allowed providers" value={providers} change={setProviders} /></div>
    <small className="muted">Canonical model codes only. Saving replaces this entire group; concurrent edits are last-commit-wins. Budgets are managed separately.</small>
    <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>Save project settings</button>
  </form>;
}
