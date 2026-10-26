import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Beam, Card, CardTitle, Check, PageHead, Spinner, Tabs } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, toast, useStore } from '../store';

type Upd = { id: string; title: string; maker: string; model: string; cls: string; date: string; size: number };
type St = { state: 'idle' | 'searching' | 'installing'; progress: string; error: string | null; items: Upd[] | null; when: string | null; lastInstall: { ok: number; total: number; reboot: boolean } | null };
type Gpu = { name: string; vendor: string; installed: string; date: string; latest?: string; url?: string; released?: string; outdated?: boolean; error?: string; page?: string; tool?: string; note?: string };
type Inst = { name: string; provider: string; version: string; date: string; cls: string; years: number };

function GpuCard() {
  const [gpus, setGpus] = useState<Gpu[] | null>(null);
  const [dl, setDl] = useState<any>(null);
  useEffect(() => {
    call<Gpu[]>('gpu.check').then(setGpus).catch(() => setGpus([]));
    return on('gpuDownload', setDl);
  }, []);
  useEffect(() => {
    if (dl?.state !== 'downloading') return;
    const t = window.setInterval(() => call('gpu.downloadStatus').then(setDl), 700);
    return () => clearInterval(t);
  }, [dl?.state]);

  async function download(url: string) {
    const r = await run(call('gpu.download', { url }));
    if (r) setDl(r);
  }
  const color: Record<string, string> = { nvidia: '#76b900', amd: '#ed1c24', intel: '#0071c5' };
  return (
    <Card>
      <CardTitle icon="gpu" color="#f472b6">Placa de vídeo — direto do fabricante</CardTitle>
      {!gpus ? <div className="row dim small"><Spinner size={14} />Consultando fabricantes…</div> : gpus.length === 0 ? <div className="small dim">Nenhuma GPU reconhecida.</div> : (
        <div className="list" style={{ gap: 10 }}>
          {gpus.map(g => (
            <div key={g.name} style={{ padding: 14, borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)' }}>
              <div className="row" style={{ marginBottom: 6 }}>
                <span className="chip" style={{ color: color[g.vendor], borderColor: `${color[g.vendor]}55` }}>{g.vendor.toUpperCase()}</span>
                <b className="ellipsis" style={{ flex: 1 }}>{g.name}</b>
                {g.outdated === true && <span className="chip warn">atualização disponível</span>}
                {g.outdated === false && <span className="chip ok">atualizado</span>}
              </div>
              <div className="small muted">Instalado: <b>{g.installed}</b>{g.date && ` (${g.date})`}{g.latest && <> · Mais recente: <b>{g.latest}</b>{g.released && ` (${g.released})`}</>}</div>
              {g.note && <div className="small dim" style={{ marginTop: 4 }}>{g.note}</div>}
              {g.error && <div className="small warn-t" style={{ marginTop: 4 }}>{g.error}</div>}
              <div className="row wrap" style={{ marginTop: 10 }}>
                {g.url && <button className={`btn sm ${g.outdated ? 'primary' : ''}`} onClick={() => download(g.url!)} disabled={dl?.state === 'downloading'}><Icon name="arrowDown" size={13} />Baixar e instalar {g.latest}</button>}
                {g.tool && <button className="btn sm primary" onClick={() => download(g.tool!)} disabled={dl?.state === 'downloading'}><Icon name="arrowDown" size={13} />Baixar assistente da Intel</button>}
                {g.page && <a className="btn sm" href={g.page} target="_blank" rel="noreferrer"><Icon name="external" size={13} />Site oficial</a>}
              </div>
            </div>
          ))}
        </div>
      )}
      {dl?.state === 'downloading' && (
        <div style={{ marginTop: 12 }}>
          <div className="small muted" style={{ marginBottom: 6 }}>Baixando… {bytes(dl.done)}{dl.total > 0 ? ` de ${bytes(dl.total)}` : ''}</div>
          <div className="bar"><i style={{ ['--v' as any]: dl.total > 0 ? dl.done / dl.total : 0.1 }} /></div>
        </div>
      )}
      {dl?.state === 'done' && <div className="small ok-t" style={{ marginTop: 10 }}><Icon name="check" size={12} /> Instalador aberto — siga as etapas na janela do fabricante.</div>}
      {dl?.state?.startsWith('error') && <div className="small bad-t" style={{ marginTop: 10 }}>{dl.state}</div>}
    </Card>
  );
}

type Missing = { name: string; code: number; hwid: string; search: string; cls: string; catalog: string; reason: string };

function MissingCard() {
  const [list, setList] = useState<Missing[] | null>(null);
  useEffect(() => { call<Missing[]>('drv.missing').then(setList).catch(() => setList([])); }, []);
  return (
    <Card style={{ borderColor: list && list.length > 0 ? 'rgba(251,191,36,.35)' : undefined }}>
      <CardTitle icon="alert" color="#fbbf24">Dispositivos que precisam de driver</CardTitle>
      {!list ? <div className="row dim small"><Spinner size={14} />Verificando o Gerenciador de Dispositivos…</div>
        : list.length === 0 ? <div className="row ok-t small"><Icon name="check" size={14} />Todos os dispositivos têm driver funcionando.</div>
          : (
            <>
              <div className="small muted" style={{ marginBottom: 8 }}>A busca abaixo (“Buscar drivers”) já procura no Windows Update os drivers destes dispositivos. Se não achar, use o Catálogo da Microsoft com o ID de hardware.</div>
              <div className="list">
                {list.map((d, i) => (
                  <div key={i} className="li" style={{ padding: '9px 0' }}>
                    <div className="grow">
                      <div className="t ellipsis">{d.name}</div>
                      <div className="s"><span className="warn-t">{d.reason}</span> · <span className="mono selectable">{d.search}</span>{d.cls && ` · ${d.cls}`}</div>
                    </div>
                    <a className="btn sm" href={d.catalog} target="_blank" rel="noreferrer"><Icon name="external" size={13} />Catálogo Microsoft</a>
                  </div>
                ))}
              </div>
            </>
          )}
    </Card>
  );
}

function CatalogTab() {
  const [st, setSt] = useState<St | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const admin = useStore(appStore, a => a?.admin);
  useEffect(() => {
    call<St>('drv.status').then(setSt);
    return on('drivers', (s: St) => { setSt(s); if (s.lastInstall && s.state === 'idle') toast(`${s.lastInstall.ok} de ${s.lastInstall.total} driver(s) instalados${s.lastInstall.reboot ? ' — reinicie o PC' : ''}`, s.lastInstall.ok === s.lastInstall.total ? 'ok' : 'warn'); });
  }, []);
  useEffect(() => {
    if (st?.state === 'idle') return;
    const t = window.setInterval(() => call<St>('drv.status').then(setSt), 1500);
    return () => clearInterval(t);
  }, [st?.state]);

  async function search() { const r = await run(call<St>('drv.search')); if (r) { setSt(r); setSel(new Set()); } }
  async function install() { const r = await run(call<St>('drv.install', { ids: [...sel] })); if (r) setSt(r); }

  if (!st) return <div className="empty"><Spinner /></div>;
  const busy = st.state !== 'idle';
  return (
    <>
      <Card style={{ padding: 22, marginBottom: 16, overflow: 'hidden' }}>
        {busy && <Beam size={180} duration={2.5} />}
        <div className="row" style={{ gap: 20 }}>
          <div style={{ width: 60, height: 60, borderRadius: 18, display: 'grid', placeItems: 'center', background: 'var(--surface-3)' }}>{busy ? <div className="tri-loader" style={{ transform: 'scale(.65)' }} /> : <Icon name="cpu" size={28} style={{ color: '#a78bfa' }} />}</div>
          <div style={{ flex: 1 }}>
            <div className="stat-big" style={{ fontSize: 22 }}>{busy ? (st.state === 'installing' ? 'Instalando drivers…' : 'Procurando drivers…') : st.items ? `${st.items.length} atualização(ões) de driver` : 'Catálogo oficial do Windows Update'}</div>
            <div className="small muted" style={{ marginTop: 4 }}>{busy ? st.progress : st.items ? `Última busca: ${st.when}` : 'Drivers testados e assinados pela Microsoft para o seu hardware exato — sem sites de terceiros.'}</div>
            {st.error && <div className="small bad-t" style={{ marginTop: 6 }}>{st.error}</div>}
          </div>
          <button className="btn primary" onClick={search} disabled={busy}><Icon name="search" size={15} />{st.items ? 'Buscar de novo' : 'Buscar drivers'}</button>
        </div>
      </Card>
      {st.items && st.items.length > 0 && (
        <Card className="pad-0">
          <div className="row" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}>
            <Check on={sel.size === st.items.length} onChange={v => setSel(v ? new Set(st.items!.map(i => i.id)) : new Set())} />
            <b>Selecionar todos</b><span className="spacer" />
            {!admin && <span className="chip warn"><Icon name="lock" size={11} />Instalar requer admin</span>}
            <button className="btn sm primary" disabled={!admin || sel.size === 0 || busy} onClick={install}><Icon name="arrowDown" size={13} />Instalar {sel.size || ''}</button>
          </div>
          <div className="list">
            {st.items.map(u => (
              <label key={u.id} className="li" style={{ cursor: 'pointer' }}>
                <Check on={sel.has(u.id)} onChange={v => setSel(s => { const n = new Set(s); v ? n.add(u.id) : n.delete(u.id); return n; })} />
                <div className="grow"><div className="t ellipsis">{u.model || u.title}</div><div className="s ellipsis">{[u.maker, u.cls, u.date && `versão de ${u.date}`].filter(Boolean).join(' · ')}</div></div>
                <span className="small muted">{u.size > 0 ? bytes(u.size) : ''}</span>
              </label>
            ))}
          </div>
        </Card>
      )}
      {st.items && st.items.length === 0 && <Card><div className="row ok-t"><Icon name="check" />Nenhuma atualização de driver pendente no catálogo da Microsoft.</div></Card>}
    </>
  );
}

function InstalledTab() {
  const [list, setList] = useState<Inst[] | null>(null);
  const [q, setQ] = useState('');
  useEffect(() => { call<Inst[]>('drv.installed').then(setList).catch(() => setList([])); }, []);
  const rows = useMemo(() => (list ?? []).filter(d => !q || (d.name + d.provider).toLowerCase().includes(q.toLowerCase())), [list, q]);
  return (
    <Card className="pad-0">
      <div className="row" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}>
        <b>Drivers de terceiros instalados</b><span className="small dim">{list ? `${list.length}` : ''} · mais antigos primeiro</span><span className="spacer" />
        <input type="search" placeholder="Filtrar" value={q} onChange={e => setQ(e.target.value)} style={{ width: 200 }} />
      </div>
      {!list ? <div className="empty"><Spinner /></div> : (
        <div style={{ maxHeight: 'calc(100vh - 300px)', overflow: 'auto' }}>
          <table className="table">
            <thead><tr><th>Dispositivo</th><th>Fabricante</th><th>Versão</th><th className="num">Data</th></tr></thead>
            <tbody>
              {rows.map((d, i) => (
                <tr key={i}><td className="ellipsis" style={{ maxWidth: 320 }}>{d.name}</td><td className="dim">{d.provider}</td><td className="mono small">{d.version}</td>
                  <td className={`num ${d.years > 5 ? 'warn-t' : ''}`}>{d.date}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

export default function Drivers() {
  const [tab, setTab] = useState<'update' | 'installed'>('update');
  return (
    <div className="page">
      <PageHead title="Drivers" sub="Busca e instala drivers oficiais: catálogo do Windows Update + versões mais novas direto da NVIDIA, Intel e AMD."
        right={<Tabs layoutId="drvtab" value={tab} onChange={setTab} items={[{ id: 'update', label: 'Atualizar' }, { id: 'installed', label: 'Instalados' }]} />} />
      {tab === 'update' ? <div className="grid" style={{ gap: 16 }}><MissingCard /><GpuCard /><CatalogTab /></div> : <InstalledTab />}
    </div>
  );
}
