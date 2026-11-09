import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import TweakList from '../components/TweakList';
import { AdminLock, Card, CardTitle, Check, Modal, PageHead, Spinner, Tabs } from '../components/ui';
import { appStore, run, toast, useStore } from '../store';

type App = { name: string; title: string; risk: 'safe' | 'review'; version: string; installed: boolean; provisioned: boolean };

function Bloatware() {
  const admin = useStore(appStore, a => !!a?.admin);
  const [list, setList] = useState<App[] | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [prov, setProv] = useState(true);
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const load = () => call<App[]>('deep.apps').then(l => { setList(l); setSel(new Set(l.filter(x => x.risk === 'safe').map(x => x.name))); }).catch(() => setList([]));
  useEffect(() => { load(); }, []);

  async function remove() {
    setConfirm(false); setBusy(true);
    const r = await run(call<{ done: string[]; failed: string[] }>('deep.removeApps', { names: [...sel], provisioned: prov && admin }));
    setBusy(false);
    if (r) {
      toast(r.failed.length ? `${r.done.length} removido(s); o Windows recusou ${r.failed.length}` : `${r.done.length} app(s) removido(s)`, r.failed.length ? 'warn' : 'ok');
      setList(null); load();
    }
  }
  const toggle = (n: string) => setSel(s => { const c = new Set(s); if (c.has(n)) c.delete(n); else c.add(n); return c; });

  return (
    <Card className="pad-0">
      <div style={{ padding: '16px 18px 6px' }}>
        <CardTitle icon="trash" color="#f472b6" right={list && list.length > 0 && (
          <button className="btn sm danger" disabled={busy || sel.size === 0} onClick={() => setConfirm(true)}>{busy ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Remover {sel.size}</button>)}>
          Apps pré-instalados
        </CardTitle>
        <p className="small muted" style={{ margin: '-4px 0 8px' }}>Apps da Microsoft e de parceiros que vêm com o Windows, inclusive Widgets e Copilot. Os marcados como "conferir" são úteis para algumas pessoas e começam desmarcados. Todos voltam pela Microsoft Store.</p>
        <div className="row small" style={{ marginBottom: 8 }}>
          <Check on={prov && admin} disabled={!admin} onChange={setProv} />
          <span>Remover também para contas novas e impedir que voltem após grandes atualizações</span>
          {!admin && <AdminLock />}
        </div>
      </div>
      {!list ? <div className="empty"><Spinner /> <div className="small dim" style={{ marginTop: 8 }}>Lendo apps instalados…</div></div>
        : list.length === 0 ? <div className="empty">Nenhum app dispensável encontrado: seu Windows já está limpo.</div>
          : (
            <div className="grid g2" style={{ gap: 0 }}>
              {list.map(a => (
                <div key={a.name} className="li" style={{ cursor: 'pointer' }} onClick={() => toggle(a.name)}>
                  <Check on={sel.has(a.name)} onChange={() => {}} />
                  <div className="grow"><div className="t ellipsis">{a.title}</div><div className="s ellipsis mono">{a.name}{!a.installed ? ' · só em contas novas' : a.provisioned ? ' · também em contas novas' : ''}</div></div>
                  {a.risk === 'review' && <span className="risk medium">conferir</span>}
                </div>
              ))}
            </div>
          )}
      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Remover {sel.size} app(s)?</h3>
        <p className="muted" style={{ marginTop: 0 }}>{admin ? 'Eles saem de todas as contas deste PC' : 'Eles saem da sua conta'}{prov && admin ? ' e não serão instalados em contas novas' : ''}. Para ter algum de volta, instale pela Microsoft Store.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn danger" onClick={remove}>Remover</button></div>
      </Modal>
    </Card>
  );
}

function EdgeOneDrive() {
  const [st, setSt] = useState<any>(null);
  const [ask, setAsk] = useState<'edge' | 'onedrive' | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => { call('deep.status').then(setSt).catch(() => setSt({})); }, []);
  async function go(what: 'edge' | 'onedrive' | 'restoreEdge') {
    setAsk(null); setBusy(what);
    const m = what === 'edge' ? 'deep.removeEdge' : what === 'onedrive' ? 'deep.removeOneDrive' : 'deep.restoreEdge';
    await run(call(m), what === 'edge' ? 'Microsoft Edge removido' : what === 'onedrive' ? 'OneDrive removido' : 'Reinstalação do Edge liberada');
    setBusy(null);
    call('deep.status').then(setSt).catch(() => {});
  }
  if (!st) return <Card><Spinner /></Card>;
  return (
    <div className="grid g2">
      <Card>
        <CardTitle icon="globe" color="#60a5fa">Microsoft Edge</CardTitle>
        <div className="small muted" style={{ minHeight: 80 }}>
          {st.edge ? <>Instalado (versão {st.edgeVersion}). </> : <>Não está instalado. </>}
          O <b>WebView2</b> {st.webview ? 'continua instalado' : 'não foi encontrado'}: é o motor que o GROBS, o Teams, os Widgets e outros programas usam para mostrar telas, e ele não é removido.
          {' '}Depois da remoção, o Windows fica impedido de reinstalar o Edge sozinho.
        </div>
        <div className="row" style={{ marginTop: 12 }}>
          {st.edge
            ? <button className="btn danger" disabled={!!busy || !st.admin} onClick={() => setAsk('edge')}>{busy === 'edge' ? <Spinner size={14} /> : <Icon name="trash" size={14} />}Remover o Edge</button>
            : <button className="btn" disabled={!!busy || !st.admin} onClick={() => go('restoreEdge')}>{busy === 'restoreEdge' ? <Spinner size={14} /> : <Icon name="download" size={14} />}Reinstalar o Edge</button>}
          {!st.admin && <AdminLock />}
        </div>
      </Card>
      <Card>
        <CardTitle icon="folder" color="#22d3ee">OneDrive</CardTitle>
        <div className="small muted" style={{ minHeight: 80 }}>
          {st.onedrive ? 'Instalado.' : 'Não está instalado.'} Os seus arquivos{st.onedriveFolder ? <> em <span className="mono selectable">{st.onedriveFolder}</span></> : ''} continuam no PC e na nuvem: só sai o programa de sincronização, junto com o atalho do Explorador.
          {' '}Contas novas também deixam de receber o OneDrive.
        </div>
        <div className="row" style={{ marginTop: 12 }}>
          <button className="btn danger" disabled={!!busy || !st.onedrive} onClick={() => setAsk('onedrive')}>{busy === 'onedrive' ? <Spinner size={14} /> : <Icon name="trash" size={14} />}Remover o OneDrive</button>
        </div>
      </Card>
      <Modal open={!!ask} onClose={() => setAsk(null)}>
        <h3>{ask === 'edge' ? 'Remover o Microsoft Edge?' : 'Remover o OneDrive?'}</h3>
        <p className="muted" style={{ marginTop: 0 }}>{ask === 'edge'
          ? 'Links e PDFs passam a abrir no navegador que você escolher como padrão. Favoritos e senhas do Edge ficam guardados no seu perfil, caso você instale de novo.'
          : 'A sincronização para. O que já está na pasta do OneDrive continua no PC, e o que está só na nuvem continua no site onedrive.com.'}</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setAsk(null)}>Cancelar</button><button className="btn danger" onClick={() => ask && go(ask)}>Remover</button></div>
      </Modal>
    </div>
  );
}

type Feat = { id: string; title: string; desc: string; risk: 'safe' | 'review'; removed?: boolean };

function Optional() {
  const admin = useStore(appStore, a => !!a?.admin);
  const [list, setList] = useState<Feat[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [err, setErr] = useState('');
  const load = () => call<Feat[]>('deep.features').then(setList).catch((e: Error) => { setErr(e.message); setList([]); });
  useEffect(() => { if (admin) load(); }, [admin]);
  async function act(f: Feat) {
    setBusy(f.id);
    const r = await run(call<{ restart: boolean }>(f.removed ? 'deep.restoreFeature' : 'deep.removeFeature', { id: f.id, title: f.title }));
    setBusy(null);
    if (r) { toast(`${f.title} ${f.removed ? 'reinstalado' : 'removido'}${r.restart ? '. Reinicie o PC para concluir.' : ''}`); load(); }
  }
  if (!admin) return <Card><AdminLock text="Abra o GROBS como administrador para remover recursos do Windows" /></Card>;
  return (
    <Card className="pad-0">
      <div style={{ padding: '16px 18px 6px' }}>
        <CardTitle icon="layers" color="#fbbf24">Recursos opcionais do Windows</CardTitle>
        <p className="small muted" style={{ margin: '-4px 0 8px' }}>Componentes que vêm instalados e quase ninguém usa. Remover libera espaço e tira serviços de segundo plano. Para reinstalar é preciso internet, porque o Windows baixa de novo.</p>
      </div>
      {!list ? <div className="empty"><Spinner /> <div className="small dim" style={{ marginTop: 8 }}>Consultando o Windows (pode levar até um minuto)…</div></div>
        : list.length === 0 ? <div className="empty">{err || 'Nenhum recurso dispensável instalado.'}</div>
          : list.map(f => (
            <div key={f.id} className="li">
              <div className="grow"><div className="t">{f.title}{f.removed && <span className="dim small"> · removido</span>}</div>{f.desc && <div className="s">{f.desc}</div>}</div>
              {f.risk === 'review' && !f.removed && <span className="risk medium">conferir</span>}
              <button className={`btn sm ${f.removed ? '' : 'danger'}`} disabled={!!busy} onClick={() => act(f)}>
                {busy === f.id ? <Spinner size={12} /> : <Icon name={f.removed ? 'download' : 'trash'} size={13} />}{f.removed ? 'Reinstalar' : 'Remover'}
              </button>
            </div>
          ))}
    </Card>
  );
}

export default function Debloat() {
  const [tab, setTab] = useState<'features' | 'apps' | 'remove' | 'optional'>('features');
  return (
    <div className="page">
      <PageHead title="Recursos do Windows" sub="Desligue Copilot, Cortana, Recall, Widgets, anúncios, telemetria do Windows e de programas e apps em segundo plano, e remova de vez apps de fábrica, Edge, OneDrive e recursos opcionais."
        right={<Tabs layoutId="dbtab" value={tab} onChange={setTab} items={[{ id: 'features', label: 'Recursos' }, { id: 'apps', label: 'Apps pré-instalados' }, { id: 'remove', label: 'Edge e OneDrive' }, { id: 'optional', label: 'Recursos opcionais' }]} />} />
      {tab === 'features'
        ? <TweakList groups={['IA e assistentes', 'Barra de tarefas', 'Anúncios e sugestões', 'Sistema', 'Telemetria', 'Apps em segundo plano', 'Navegadores']}
            icons={{ 'IA e assistentes': ['sparkles', '#a78bfa'], 'Barra de tarefas': ['list', '#22d3ee'], 'Anúncios e sugestões': ['eye', '#f472b6'], 'Sistema': ['settings', '#fbbf24'], 'Telemetria': ['eye', '#fb7185'], 'Apps em segundo plano': ['layers', '#34d399'], 'Navegadores': ['globe', '#60a5fa'] }} />
        : tab === 'apps' ? <Bloatware /> : tab === 'remove' ? <EdgeOneDrive /> : <Optional />}
    </div>
  );
}
