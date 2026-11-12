import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import GlareHover from '../components/rb/GlareHover';
import { AdminLock, Card, CardTitle, Modal, PageHead, Spinner, Toggle } from '../components/ui';
import { appStore, run, setSetting, toast, useStore } from '../store';

type Ping = { host: string; avg: number; min: number; max: number; lost: number };
const HOSTS = [{ host: '1.1.1.1', name: 'Cloudflare' }, { host: '8.8.8.8', name: 'Google DNS' }, { host: '208.67.222.222', name: 'OpenDNS' }];

export default function Tools() {
  const admin = useStore(appStore, a => a?.admin);
  const [busy, setBusy] = useState<string | null>(null);
  const [pings, setPings] = useState<Ping[]>([]);

  async function act(id: string, m: string, ok: string, p?: any) {
    setBusy(id);
    await run(call(m, p), ok);
    setBusy(null);
  }
  async function pingAll() {
    setBusy('ping'); setPings([]);
    for (const h of HOSTS) {
      const r = await call<Ping>('tools.ping', { host: h.host }).catch(() => null);
      if (r) setPings(p => [...p, r]);
    }
    setBusy(null);
  }

  const tools: { id: string; icon: string; color: string; title: string; desc: string; admin?: boolean; run: () => void }[] = [
    { id: 'dns', icon: 'globe', color: '#22d3ee', title: 'Limpar cache DNS', desc: 'Resolve sites que não abrem após mudança de endereço.', run: () => act('dns', 'tools.flushDns', 'Cache DNS limpo') },
    { id: 'explorer', icon: 'refresh', color: '#a78bfa', title: 'Reiniciar o Explorer', desc: 'Corrige barra de tarefas ou área de trabalho travadas.', run: () => act('explorer', 'tools.restartExplorer', 'Explorer reiniciado') },
    { id: 'battery', icon: 'battery', color: '#34d399', title: 'Relatório da bateria', desc: 'Capacidade atual vs. original, ciclos e histórico de uso (notebooks). Abre no navegador.', run: () => act('battery', 'battery.report', 'Relatório gerado') },
    { id: 'winutil', icon: 'wrench', color: '#fbbf24', title: 'WinUtil (Chris Titus)', desc: 'Abre a ferramenta gratuita do Chris Titus: instala programas, ajustes, consertos e Windows Update. Baixa a versão estável do site oficial e abre numa janela própria.', run: () => act('winutil', 'tools.winutil', 'Abrindo o WinUtil… a janela aparece em alguns segundos') },
  ];

  return (
    <div className="page">
      <PageHead title="Utilitários" sub="Ferramentas úteis, a um clique." />
      <div className="grid g3">
        {tools.map(t => (
          <GlareHover key={t.id} background="var(--surface)" borderColor="rgba(255,255,255,0.07)" borderRadius="16px" width="100%" height="100%" glareOpacity={0.14} glareSize={260}>
            <button className="tool-btn" onClick={t.run} disabled={!!busy || (t.admin && !admin)}>
              <span className="qa-ico" style={{ color: t.color, background: `${t.color}1f` }}>{busy === t.id ? <Spinner size={18} /> : <Icon name={t.icon} size={20} />}</span>
              <b>{t.title} {t.admin && !admin && <AdminLock />}</b>
              <span className="small dim">{t.desc}</span>
            </button>
          </GlareHover>
        ))}
      </div>

      <ForceDeleteCard />

      <RestorePointsCard />

      <h2 className="sec"><Icon name="network" size={16} />Latência da internet</h2>
      <Card>
        <CardTitle icon="wifi" color="#34d399" right={<button className="btn sm primary" onClick={pingAll} disabled={busy === 'ping'}>{busy === 'ping' ? <Spinner size={13} /> : <Icon name="play" size={13} />}Testar</button>}>Ping para servidores públicos</CardTitle>
        <div className="grid g3">
          {HOSTS.map(h => {
            const r = pings.find(p => p.host === h.host);
            const q = !r ? '' : r.avg < 0 ? 'bad' : r.avg < 30 ? 'ok' : r.avg < 80 ? 'warn' : 'bad';
            return (
              <div key={h.host} style={{ padding: 14, borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)' }}>
                <div className="small dim">{h.name} · {h.host}</div>
                <div className={`stat-big ${q}-t`} style={{ fontSize: 26, margin: '6px 0 2px' }}>{!r ? (busy === 'ping' ? <Spinner /> : '—') : r.avg < 0 ? 'sem resposta' : `${Math.round(r.avg)} ms`}</div>
                {r && r.avg >= 0 && <div className="small dim">mín {r.min} · máx {r.max} · {r.lost} perdido(s)</div>}
              </div>
            );
          })}
        </div>
      </Card>
    </div>
  );
}

type RP = { seq: number; desc: string; date: string };

function RestorePointsCard() {
  const app = useStore(appStore);
  const [list, setList] = useState<RP[] | null>(null);
  const [confirm, setConfirm] = useState<RP | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const load = () => call<{ points: RP[] }>('rp.status').then(s => setList(s.points)).catch(() => setList([]));
  useEffect(() => { load(); }, []);
  async function create() {
    setBusy('create');
    const r = await run(call<{ points: RP[] }>('rp.create', { desc: 'GROBS Optimizer — manual' }), 'Ponto de restauração criado');
    if (r) setList(r.points);
    setBusy(null);
  }
  async function enable() { setBusy('enable'); await run(call('rp.enable'), 'Proteção do Sistema ligada no disco do Windows'); setBusy(null); }
  async function restore(p: RP) {
    setConfirm(null); setBusy('restore');
    const ok = await run(call('rp.restore', { seq: p.seq }));
    setBusy(null);
    if (ok) toast('Restauração agendada — o PC vai reiniciar em 15 segundos', 'warn');
  }
  const auto = (app?.settings as any)?.autoRestorePoint !== false;
  return (
    <>
      <h2 className="sec"><Icon name="restore" size={16} />Pontos de restauração</h2>
      <Card className="pad-0">
        <div className="row wrap" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
          <div style={{ flex: 1, minWidth: 260 }}><b>Desfazer mudanças no sistema</b><div className="small dim">Volta o registro, drivers e arquivos do Windows ao estado do ponto. Seus documentos não são afetados.</div></div>
          <label className="row small muted" style={{ gap: 8 }}>Criar antes de ajustes de risco <Toggle on={auto} onChange={v => setSetting('autoRestorePoint' as any, v as any)} /></label>
          {!app?.admin && <AdminLock />}
          <button className="btn sm ghost" onClick={enable} disabled={!app?.admin || !!busy}>{busy === 'enable' ? <Spinner size={12} /> : <Icon name="shield" size={13} />}Ligar proteção</button>
          <button className="btn sm primary" onClick={create} disabled={!app?.admin || !!busy}>{busy === 'create' ? <Spinner size={12} /> : <Icon name="restore" size={13} />}Criar agora</button>
        </div>
        <div className="list" style={{ maxHeight: 260, overflow: 'auto' }}>
          {!list ? <div className="empty"><Spinner /></div> : list.length === 0 ? <div className="empty small">Nenhum ponto de restauração. Se "Criar agora" falhar, clique em "Ligar proteção" primeiro.</div> : list.map(p => (
            <div key={p.seq} className="li" style={{ padding: '9px 18px' }}>
              <Icon name="clock" size={15} className="muted" />
              <div className="grow"><div className="t" style={{ fontSize: 13 }}>{p.desc}</div><div className="s">{p.date}</div></div>
              <button className="btn sm" disabled={!app?.admin || !!busy} onClick={() => setConfirm(p)}>Restaurar…</button>
            </div>
          ))}
        </div>
      </Card>
      <Modal open={!!confirm} onClose={() => setConfirm(null)}>
        <h3>Restaurar o sistema?</h3>
        <p className="muted" style={{ marginTop: 0 }}>O Windows volta ao ponto <b>{confirm?.desc}</b> ({confirm?.date}). Programas e drivers instalados depois dessa data podem ser removidos. <b>O PC reinicia em 15 segundos</b> — salve o que estiver aberto.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(null)}>Cancelar</button><button className="btn danger" onClick={() => confirm && restore(confirm)}><Icon name="restore" size={14} />Restaurar e reiniciar</button></div>
      </Modal>
    </>
  );
}

type FdInfo = { path: string; isDir: boolean; files: number; dirs: number; size: number; owner: string; blocked: string | null; admin: boolean; lockers: { pid: number; name: string }[] };
type FdResult = { deleted: number; pending: number; failed: number; closed: number; errors: string[]; exists: boolean };

/** Apagar à força: arquivos com "acesso negado", "você precisa de permissão" ou "em uso por outro programa". */
function ForceDeleteCard() {
  const admin = useStore(appStore, a => a?.admin);
  const [path, setPath] = useState('');
  const [info, setInfo] = useState<FdInfo | null>(null);
  const [closeApps, setCloseApps] = useState(true);
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<FdResult | null>(null);

  async function inspect(p: string) {
    setPath(p); setResult(null); setInfo(null);
    if (!p.trim()) return;
    setBusy(true);
    const r = await run(call<FdInfo>('force.inspect', { path: p }));
    setBusy(false);
    if (r) setInfo(r);
  }
  async function pick(m: string) { const p = await call<string | null>(m); if (p) inspect(p); }
  async function del() {
    if (!info) return;
    setConfirm(false); setBusy(true);
    const r = await run(call<FdResult>('force.run', { path: info.path, closeApps }));
    setBusy(false);
    if (!r) return;
    setResult(r); setInfo(null); setPath('');
    if (r.failed > 0) toast(`${r.failed} item(ns) não puderam ser apagados`, 'warn');
    else if (r.pending > 0) toast('Parte dos itens será apagada ao reiniciar o PC', 'info');
    else toast('Apagado');
  }
  const human = (n: number) => (n >= 1073741824 ? `${(n / 1073741824).toFixed(1)} GB` : n >= 1048576 ? `${(n / 1048576).toFixed(1)} MB` : `${Math.ceil(n / 1024)} KB`);
  return (
    <>
      <h2 className="sec"><Icon name="trash" size={16} />Apagar à força</h2>
      <Card>
        <CardTitle icon="lock" color="#f87171" right={!admin && <AdminLock />}>Arquivos que o Windows não deixa apagar</CardTitle>
        <div className="small muted" style={{ marginBottom: 12 }}>Para "acesso negado", "você precisa de permissão" e "arquivo em uso": o GROBS assume a posse, dá permissão a si mesmo, fecha quem está travando e apaga. O que continuar preso é apagado ao reiniciar. A pasta do Windows e as pastas essenciais são sempre recusadas.</div>
        <div className="row wrap">
          <input type="text" placeholder="Cole o caminho aqui ou escolha ao lado" value={path} onChange={e => setPath(e.target.value)} onBlur={() => path !== (info?.path ?? '') && inspect(path)} onKeyDown={e => e.key === 'Enter' && inspect(path)} style={{ flex: 1, minWidth: 260 }} />
          <button className="btn sm" onClick={() => pick('shell.pickFile')} disabled={busy}><Icon name="file" size={13} />Arquivo…</button>
          <button className="btn sm" onClick={() => pick('shell.pickFolder')} disabled={busy}><Icon name="folder" size={13} />Pasta…</button>
        </div>
        {busy && <div className="small muted" style={{ marginTop: 12 }}><Spinner size={12} /> Trabalhando…</div>}
        {info && (
          <div style={{ marginTop: 14, padding: 14, borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)' }}>
            <div className="mono small selectable" style={{ wordBreak: 'break-all' }}>{info.path}</div>
            <div className="small dim" style={{ marginTop: 4 }}>{info.isDir ? `${info.files} arquivo(s) e ${info.dirs} subpasta(s)` : 'Arquivo'} · {human(info.size)}{info.owner ? ` · dono: ${info.owner}` : ''}</div>
            {info.blocked ? <div className="small bad-t" style={{ marginTop: 10 }}><Icon name="shield" size={12} /> {info.blocked}</div> : (
              <>
                {info.lockers.length > 0 && (
                  <div style={{ marginTop: 10 }}>
                    <div className="small warn-t"><Icon name="alert" size={12} /> Em uso por: {info.lockers.map(l => `${l.name} (${l.pid})`).join(', ')}</div>
                    <label className="row small" style={{ gap: 8, marginTop: 8 }}><Toggle on={closeApps} onChange={setCloseApps} />Fechar esses programas (o que não estiver salvo neles é perdido)</label>
                  </div>
                )}
                <button className="btn danger" style={{ marginTop: 12 }} disabled={!admin || busy} onClick={() => setConfirm(true)}><Icon name="trash" size={15} />Apagar à força</button>
              </>
            )}
          </div>
        )}
        {result && (
          <div className="small" style={{ marginTop: 12 }}>
            <span className={result.failed ? 'warn-t' : 'ok-t'}>{result.deleted} apagado(s){result.closed ? ` · ${result.closed} programa(s) fechado(s)` : ''}{result.pending ? ' · o restante sai ao reiniciar o PC' : ''}{result.failed ? ` · ${result.failed} com falha` : ''}</span>
            {result.errors.length > 0 && <pre className="mono selectable dim" style={{ margin: '6px 0 0', whiteSpace: 'pre-wrap', maxHeight: 120, overflow: 'auto' }}>{result.errors.join('\n')}</pre>}
          </div>
        )}
      </Card>
      <Modal open={confirm && !!info} onClose={() => setConfirm(false)}>
        <h3>Apagar definitivamente?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Isto <b>não vai para a Lixeira e não dá para desfazer</b>. Se o arquivo pertence a um programa ou ao sistema, esse programa pode parar de funcionar.</p>
        <div className="mono small" style={{ wordBreak: 'break-all', marginBottom: 14 }}>{info?.path}</div>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn danger" onClick={del}><Icon name="trash" size={14} />Apagar</button></div>
      </Modal>
    </>
  );
}
