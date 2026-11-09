import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import TweakList from '../components/TweakList';
import { AdminLock, Card, CardTitle, Check, Modal, PageHead, Spinner, Tabs, Toggle } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, setSetting, toast, useStore } from '../store';

const RISK: Record<string, [string, string]> = {
  low: ['low', 'tranquilo'], safe: ['low', 'tranquilo'], medium: ['medium', 'moderado'], review: ['medium', 'moderado'], high: ['high', 'perigoso'], danger: ['high', 'perigoso'],
};
function Risk({ r }: { r: string }) { const [c, t] = RISK[r] ?? RISK.medium; return <span className={`risk ${c}`}>{t}</span>; }

// alternativas leves (winget) para apps do Windows removidos
const ALT: Record<string, { id: string; name: string }[]> = {
  'Microsoft.ZuneMusic': [{ id: 'VideoLAN.VLC', name: 'VLC' }], 'Microsoft.ZuneVideo': [{ id: 'VideoLAN.VLC', name: 'VLC' }],
  'Microsoft.MSPaint': [{ id: 'dotPDN.PaintDotNet', name: 'Paint.NET' }], 'Microsoft.WindowsSoundRecorder': [{ id: 'Audacity.Audacity', name: 'Audacity' }],
  'Microsoft.WindowsCommunicationsApps': [{ id: 'Mozilla.Thunderbird', name: 'Thunderbird' }], 'Microsoft.OutlookForWindows': [{ id: 'Mozilla.Thunderbird', name: 'Thunderbird' }],
  'Clipchamp.Clipchamp': [{ id: 'Meltytech.Shotcut', name: 'Shotcut' }],
  edge: [{ id: 'Mozilla.Firefox', name: 'Firefox' }, { id: 'Google.Chrome', name: 'Chrome' }, { id: 'Brave.Brave', name: 'Brave' }],
};

function Alternatives({ removed }: { removed: string[] }) {
  const [st, setSt] = useState<Record<string, string>>({});
  useEffect(() => on('winget', (s: any) => setSt(s?.state ?? {})), []);
  const list = useMemo(() => { const m = new Map<string, string>(); for (const r of removed) for (const a of ALT[r] ?? []) m.set(a.id, a.name); return [...m]; }, [removed]);
  if (list.length === 0) return null;
  return (
    <Card>
      <CardTitle icon="download" color="#34d399">Quer uma alternativa mais leve?</CardTitle>
      <div className="small muted" style={{ marginBottom: 10 }}>Programas gratuitos e leves que fazem o mesmo que os apps removidos, instalados pelo winget (fonte oficial de cada um).</div>
      <div className="row wrap">
        {list.map(([id, name]) => (
          <button key={id} className="btn sm" disabled={!!st[id] && st[id] !== 'failed'} onClick={() => run(call('winget.install', { ids: [id] }))}>
            {st[id] === 'running' ? <Spinner size={12} /> : st[id] === 'done' ? <Icon name="check" size={13} /> : <Icon name="download" size={13} />}
            {st[id] === 'done' ? `${name} instalado` : st[id] === 'failed' ? `${name}: tentar de novo` : `Instalar ${name}`}
          </button>
        ))}
      </div>
    </Card>
  );
}

// ------------------------------------------------------------------ perfis de 1 clique
type PItem = { key: string; kind: string; id: string; title: string; group: string; risk: string; skip: string | null };
type PPlan = { pc: Record<string, boolean>; items: PItem[] };
type PSt = { running: boolean; phase: string; error: string | null; message: string | null; done: number; total: number; last: boolean; admin: boolean };

const LEVELS = [
  { id: 'safe', title: 'Seguro', color: '#34d399', desc: 'Só o que é tranquilo: anúncios, sugestões, telemetria, IA, apps de fábrica inúteis e serviços sem uso. Nada que você vá sentir falta.' },
  { id: 'recommended', title: 'Recomendado', color: '#fbbf24', desc: 'O Seguro mais os itens moderados: mais serviços, menu do clique direito limpo, Armazenamento Reservado e apps que algumas pessoas usam.' },
  { id: 'extreme', title: 'Extremo', color: '#f87171', desc: 'Tudo, inclusive o perigoso: Microsoft Store, Xbox, Edge, OneDrive, indexação da busca e Windows Update só manual. Para quem sabe o que está fazendo.' },
];
const PC_LABEL: Record<string, string> = { laptop: 'Notebook', printer: 'Impressora', bluetooth: 'Bluetooth', biometric: 'Biometria', scanner: 'Scanner/câmera', smartcard: 'Token/cartão', xbox: 'App Xbox' };

function Profiles() {
  const app = useStore(appStore);
  const admin = !!app?.admin;
  const [level, setLevel] = useState('recommended');
  const [plan, setPlan] = useState<PPlan | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(false);
  const [st, setSt] = useState<PSt | null>(null);
  const [confirm, setConfirm] = useState(false);
  const [removed, setRemoved] = useState<string[]>([]);
  useEffect(() => {
    call<PSt>('dprof.status').then(setSt).catch(() => {});
    return on('debloatProfile', (s: PSt) => setSt(prev => { if (prev?.running && !s.running) { if (s.error) toast(s.error, 'err'); else if (s.message) toast(s.message); } return s; }));
  }, []);
  async function load(l: string) {
    setLevel(l); setPlan(null); setLoading(true);
    const p = await run(call<PPlan>('dprof.plan', { level: l }));
    setLoading(false);
    if (p) { setPlan(p); setSel(new Set(p.items.filter(i => !i.skip).map(i => i.key))); }
  }
  async function apply() {
    setConfirm(false);
    setRemoved(plan!.items.filter(i => sel.has(i.key) && (i.kind === 'app' || i.kind === 'edge')).map(i => i.id));
    const r = await run(call<PSt>('dprof.apply', { keys: [...sel] }));
    if (r) setSt(r);
  }
  const groups = useMemo(() => [...new Set((plan?.items ?? []).map(i => i.group))], [plan]);
  const toggle = (k: string) => setSel(s => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n; });
  const counts = useMemo(() => { const c = { low: 0, medium: 0, high: 0 }; for (const i of plan?.items ?? []) if (sel.has(i.key)) c[(RISK[i.risk]?.[0] ?? 'medium') as 'low']++; return c; }, [plan, sel]);
  if (!admin) return <Card><AdminLock text="Abra o GROBS como administrador para usar os perfis" /></Card>;
  const busy = !!st?.running;
  return (
    <div className="grid" style={{ gap: 16 }}>
      <div className="grid g3">
        {LEVELS.map(l => (
          <Card key={l.id} style={{ borderColor: level === l.id && plan ? l.color : undefined, cursor: 'pointer' }}>
            <div onClick={() => !busy && load(l.id)}>
              <CardTitle icon={l.id === 'safe' ? 'shieldCheck' : l.id === 'recommended' ? 'zap' : 'flame'} color={l.color}>{l.title}</CardTitle>
              <div className="small muted" style={{ minHeight: 64 }}>{l.desc}</div>
              <button className="btn sm" style={{ marginTop: 10 }} disabled={busy}>{loading && level === l.id ? <Spinner size={12} /> : <Icon name="list" size={13} />}Ver o que será feito</button>
            </div>
          </Card>
        ))}
      </div>
      <Card>
        <div className="row wrap">
          <Toggle on={(app?.settings as any)?.debloatAllUsers !== false} onChange={v => setSetting('debloatAllUsers' as any, v as any)} />
          <div className="grow"><b>Aplicar também nas outras contas e nas contas novas</b><div className="small muted">Os ajustes da sua conta vão também para as outras contas do PC e para o perfil padrão, então contas criadas depois já nascem limpas.</div></div>
          {st?.last && <button className="btn sm" disabled={busy} onClick={async () => { const r = await run(call<PSt>('dprof.undo')); if (r) setSt(r); }}><Icon name="restore" size={13} />Desfazer o último perfil</button>}
        </div>
        {busy && <div className="row small muted" style={{ marginTop: 10 }}><Spinner size={14} />{st!.phase} ({st!.done} de {st!.total})</div>}
        {!busy && st?.message && <div className="small ok-t" style={{ marginTop: 10 }}>{st.message}</div>}
      </Card>
      {plan && (
        <Card className="pad-0">
          <div style={{ padding: '16px 18px 8px' }}>
            <CardTitle icon="list" color="#a78bfa" right={<button className="btn primary" disabled={busy || sel.size === 0} onClick={() => setConfirm(true)}><Icon name="zap" size={14} />Aplicar {sel.size}</button>}>
              Perfil {LEVELS.find(l => l.id === level)?.title}: {plan.items.length} item(ns)
            </CardTitle>
            <div className="row wrap small" style={{ gap: 6 }}>
              <span className="muted">Seu PC:</span>
              {Object.entries(plan.pc).filter(([, v]) => v).map(([k]) => <span key={k} className="chip">{PC_LABEL[k] ?? k}</span>)}
              {Object.values(plan.pc).every(v => !v) && <span className="chip">Desktop simples</span>}
              <span className="spacer" />
              <span className="risk low">{counts.low} tranquilos</span><span className="risk medium">{counts.medium} moderados</span><span className="risk high">{counts.high} perigosos</span>
            </div>
          </div>
          {plan.items.length === 0 && <div className="empty">Nada a fazer: este nível já está aplicado.</div>}
          {groups.map(g => (
            <div key={g}>
              <div className="small dim" style={{ padding: '10px 18px 4px', textTransform: 'uppercase', letterSpacing: 1 }}>{g}</div>
              {plan.items.filter(i => i.group === g).map(i => (
                <div key={i.key} className="li" style={{ cursor: i.skip ? 'default' : 'pointer', opacity: i.skip ? 0.55 : 1 }} onClick={() => !i.skip && toggle(i.key)}>
                  <Check on={sel.has(i.key)} disabled={!!i.skip} onChange={() => {}} />
                  <div className="grow"><div className="t ellipsis">{i.title}</div>{i.skip && <div className="s">{i.skip}</div>}</div>
                  <Risk r={i.risk} />
                </div>
              ))}
            </div>
          ))}
        </Card>
      )}
      <Alternatives removed={removed} />
      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Aplicar {sel.size} item(ns)?</h3>
        <p className="muted" style={{ marginTop: 0 }}>O GROBS cria um ponto de restauração antes. Ajustes, serviços e o Windows Update podem ser desfeitos por "Desfazer o último perfil". Apps, Edge e OneDrive removidos voltam só reinstalando (Microsoft Store e sites oficiais).</p>
        {counts.high > 0 && <p className="bad-t small">Inclui {counts.high} item(ns) perigoso(s). Confira a lista antes.</p>}
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn primary" onClick={apply}>Aplicar</button></div>
      </Modal>
    </div>
  );
}

// ------------------------------------------------------------------ apps pré-instalados
type App = { name: string; title: string; risk: 'safe' | 'review' | 'danger'; version: string; installed: boolean; provisioned: boolean };

function Bloatware() {
  const admin = useStore(appStore, a => !!a?.admin);
  const [list, setList] = useState<App[] | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [prov, setProv] = useState(true);
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [removed, setRemoved] = useState<string[]>([]);
  const load = () => call<App[]>('deep.apps').then(l => { setList(l); setSel(new Set(l.filter(x => x.risk === 'safe').map(x => x.name))); }).catch(() => setList([]));
  useEffect(() => { load(); }, []);

  async function remove() {
    setConfirm(false); setBusy(true);
    const r = await run(call<{ done: string[]; failed: string[] }>('deep.removeApps', { names: [...sel], provisioned: prov && admin }));
    setBusy(false);
    if (r) {
      toast(r.failed.length ? `${r.done.length} removido(s); o Windows recusou ${r.failed.length}` : `${r.done.length} app(s) removido(s)`, r.failed.length ? 'warn' : 'ok');
      setRemoved(r.done); setList(null); load();
    }
  }
  const toggle = (n: string) => setSel(s => { const c = new Set(s); if (c.has(n)) c.delete(n); else c.add(n); return c; });
  const dangerSel = (list ?? []).some(a => a.risk === 'danger' && sel.has(a.name));

  return (
    <div className="grid" style={{ gap: 16 }}>
      <Card className="pad-0">
        <div style={{ padding: '16px 18px 6px' }}>
          <CardTitle icon="trash" color="#f472b6" right={list && list.length > 0 && (
            <button className="btn sm danger" disabled={busy || sel.size === 0} onClick={() => setConfirm(true)}>{busy ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Remover {sel.size}</button>)}>
            Apps pré-instalados
          </CardTitle>
          <p className="small muted" style={{ margin: '-4px 0 8px' }}>Apps da Microsoft e de parceiros que vêm com o Windows, inclusive Widgets, Copilot, Xbox e a Microsoft Store. Só os tranquilos começam marcados. Todos voltam pela Microsoft Store (a própria Store volta pelo winget).</p>
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
                    <Risk r={a.risk} />
                  </div>
                ))}
              </div>
            )}
        <Modal open={confirm} onClose={() => setConfirm(false)}>
          <h3>Remover {sel.size} app(s)?</h3>
          <p className="muted" style={{ marginTop: 0 }}>{admin ? 'Eles saem de todas as contas deste PC' : 'Eles saem da sua conta'}{prov && admin ? ' e não serão instalados em contas novas' : ''}. Para ter algum de volta, instale pela Microsoft Store.</p>
          {dangerSel && <p className="bad-t small">Você marcou itens perigosos: sem a Microsoft Store e o login do Xbox, jogos e apps da loja (incluindo Game Pass) deixam de instalar e abrir.</p>}
          <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn danger" onClick={remove}>Remover</button></div>
        </Modal>
      </Card>
      <Alternatives removed={removed} />
    </div>
  );
}

function EdgeOneDrive() {
  const [st, setSt] = useState<any>(null);
  const [ask, setAsk] = useState<'edge' | 'onedrive' | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [removed, setRemoved] = useState<string[]>([]);
  useEffect(() => { call('deep.status').then(setSt).catch(() => setSt({})); }, []);
  async function go(what: 'edge' | 'onedrive' | 'restoreEdge') {
    setAsk(null); setBusy(what);
    const m = what === 'edge' ? 'deep.removeEdge' : what === 'onedrive' ? 'deep.removeOneDrive' : 'deep.restoreEdge';
    const r = await run(call(m), what === 'edge' ? 'Microsoft Edge removido' : what === 'onedrive' ? 'OneDrive removido' : 'Reinstalação do Edge liberada');
    setBusy(null);
    if (r && what === 'edge') setRemoved(['edge']);
    call('deep.status').then(setSt).catch(() => {});
  }
  if (!st) return <Card><Spinner /></Card>;
  return (
    <div className="grid" style={{ gap: 16 }}>
      <div className="grid g2">
        <Card>
          <CardTitle icon="globe" color="#60a5fa">Microsoft Edge</CardTitle>
          <div className="small muted" style={{ minHeight: 80 }}>
            {st.edge ? <>Instalado (versão {st.edgeVersion}). </> : <>Não está instalado. </>}
            O <b>WebView2</b> {st.webview ? 'continua instalado' : 'não foi encontrado'}: é o motor que o GROBS, o Teams, os Widgets e outros programas usam para mostrar telas, e ele não é removido.
            {' '}Depois da remoção, o Windows fica impedido de reinstalar o Edge sozinho.
          </div>
          <div className="row" style={{ marginTop: 12 }}>
            <Risk r="high" /><span className="spacer" />
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
            <Risk r="medium" /><span className="spacer" />
            <button className="btn danger" disabled={!!busy || !st.onedrive} onClick={() => setAsk('onedrive')}>{busy === 'onedrive' ? <Spinner size={14} /> : <Icon name="trash" size={14} />}Remover o OneDrive</button>
          </div>
        </Card>
      </div>
      <Alternatives removed={removed} />
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
              {!f.removed && <Risk r={f.risk} />}
              <button className={`btn sm ${f.removed ? '' : 'danger'}`} disabled={!!busy} onClick={() => act(f)}>
                {busy === f.id ? <Spinner size={12} /> : <Icon name={f.removed ? 'download' : 'trash'} size={13} />}{f.removed ? 'Reinstalar' : 'Remover'}
              </button>
            </div>
          ))}
    </Card>
  );
}

function Space() {
  const admin = useStore(appStore, a => !!a?.admin);
  const [st, setSt] = useState<PSt | null>(null);
  const [langs, setLangs] = useState<{ locale: string; name: string; caps: string[] }[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => {
    call<PSt>('dprof.status').then(setSt).catch(() => {});
    if (admin) call<any[]>('dprof.languages').then(setLangs).catch(() => setLangs([]));
    return on('debloatProfile', (s: PSt) => setSt(prev => { if (prev?.running && !s.running) { if (s.error) toast(s.error, 'err'); else if (s.message) toast(s.message); } return s; }));
  }, [admin]);
  if (!admin) return <Card><AdminLock text="Abra o GROBS como administrador para liberar espaço do sistema" /></Card>;
  return (
    <div className="grid" style={{ gap: 16 }}>
      <TweakList groups={['Liberar espaço do sistema']} icons={{ 'Liberar espaço do sistema': ['disk', '#22d3ee'] }} />
      <Card>
        <CardTitle icon="layers" color="#a78bfa">Componentes antigos do Windows (WinSxS)</CardTitle>
        <div className="small muted">Apaga versões antigas de arquivos do sistema que ficaram depois das atualizações. Costuma liberar de 1 a 5 GB. Depois disso não dá para desinstalar as atualizações já instaladas. Leva de 10 a 30 minutos.</div>
        {st?.running && <div className="row small muted" style={{ marginTop: 10 }}><Spinner size={14} />{st.phase}</div>}
        {!st?.running && st?.message && <div className="small ok-t" style={{ marginTop: 10 }}>{st.message}</div>}
        <div className="row" style={{ marginTop: 10 }}><Risk r="medium" /><span className="spacer" /><button className="btn primary" disabled={!!st?.running} onClick={async () => { const r = await run(call<PSt>('dprof.cleanup')); if (r) setSt(r); }}><Icon name="broom" size={14} />Limpar componentes</button></div>
      </Card>
      <Card className="pad-0">
        <div style={{ padding: '16px 18px 6px' }}><CardTitle icon="globe" color="#60a5fa">Idiomas extras</CardTitle>
          <div className="small muted" style={{ marginTop: -6, marginBottom: 6 }}>Pacotes de fala, escrita à mão, OCR e texto de idiomas que não são o do Windows nem o inglês.</div></div>
        {!langs ? <div className="empty"><Spinner /></div> : langs.length === 0 ? <div className="empty">Nenhum idioma extra instalado.</div> : langs.map(l => (
          <div key={l.locale} className="li">
            <div className="grow"><div className="t">{l.name} <span className="dim small mono">{l.locale}</span></div><div className="s">{l.caps.length} pacote(s)</div></div>
            <Risk r="low" />
            <button className="btn sm danger" disabled={!!busy} onClick={async () => { setBusy(l.locale); const r = await run(call<any[]>('dprof.removeLanguage', { locale: l.locale }), `${l.name} removido`); setBusy(null); if (r) setLangs(r); }}>{busy === l.locale ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Remover</button>
          </div>
        ))}
      </Card>
    </div>
  );
}

export default function Debloat() {
  const [tab, setTab] = useState<'profiles' | 'features' | 'apps' | 'remove' | 'optional' | 'space'>('profiles');
  return (
    <div className="page">
      <PageHead title="Recursos do Windows" sub="Debloat profundo com perfis de 1 clique, ou item por item: Copilot, Widgets, anúncios, telemetria, serviços, clique direito, apps de fábrica, Edge, OneDrive, recursos opcionais e espaço do sistema. Cada item diz se é tranquilo, moderado ou perigoso."
        right={<Tabs layoutId="dbtab" value={tab} onChange={setTab} items={[{ id: 'profiles', label: 'Perfis' }, { id: 'features', label: 'Recursos' }, { id: 'apps', label: 'Apps' }, { id: 'remove', label: 'Edge e OneDrive' }, { id: 'optional', label: 'Opcionais' }, { id: 'space', label: 'Espaço' }]} />} />
      {tab === 'profiles' ? <Profiles />
        : tab === 'features'
          ? <TweakList groups={['IA e assistentes', 'Barra de tarefas', 'Explorador e menu', 'Anúncios e sugestões', 'Sistema', 'Telemetria', 'Apps em segundo plano', 'Navegadores']}
            icons={{ 'IA e assistentes': ['sparkles', '#a78bfa'], 'Barra de tarefas': ['list', '#22d3ee'], 'Explorador e menu': ['folder', '#fbbf24'], 'Anúncios e sugestões': ['eye', '#f472b6'], 'Sistema': ['settings', '#fbbf24'], 'Telemetria': ['eye', '#fb7185'], 'Apps em segundo plano': ['layers', '#34d399'], 'Navegadores': ['globe', '#60a5fa'] }} />
          : tab === 'apps' ? <Bloatware /> : tab === 'remove' ? <EdgeOneDrive /> : tab === 'optional' ? <Optional /> : <Space />}
    </div>
  );
}
