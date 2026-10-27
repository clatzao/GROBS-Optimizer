import { ReactNode, useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Card, Check, Modal, PageHead, Spinner, Tabs } from '../components/ui';
import { bytes } from '../format';
import { run, toast } from '../store';

type AppItem = { id: string; name: string; publisher: string; version: string; size: number; date: string; scope: string; location?: string };
type Leftover = { path: string; size: number; files: number };

function Installed({ tabs }: { tabs: ReactNode }) {
  const [list, setList] = useState<AppItem[] | null>(null);
  const [q, setQ] = useState('');
  const [sort, setSort] = useState<'name' | 'size' | 'date'>('name');
  const [busy, setBusy] = useState<string | null>(null);
  const [left, setLeft] = useState<{ app: AppItem; items: Leftover[]; sel: Set<string> } | null>(null);

  const [icons, setIcons] = useState<Record<string, string>>({});
  const load = () => call<AppItem[]>('apps.list').then(l => { setList(l); call<Record<string, string>>('apps.icons').then(setIcons).catch(() => {}); }).catch(() => setList([]));
  useEffect(() => { load(); }, []);

  const rows = useMemo(() => {
    const f = q.trim().toLowerCase();
    const r = (list ?? []).filter(a => !f || a.name.toLowerCase().includes(f) || a.publisher.toLowerCase().includes(f));
    const d = (s: string) => (s.length === 10 ? s.slice(6) + s.slice(3, 5) + s.slice(0, 2) : '');
    return r.sort((a, b) => sort === 'size' ? b.size - a.size : sort === 'date' ? d(b.date).localeCompare(d(a.date)) : a.name.localeCompare(b.name));
  }, [list, q, sort]);

  async function uninstall(a: AppItem) {
    setBusy(a.id);
    const r = await run(call<any>('apps.uninstall', { id: a.id }));
    setBusy(null);
    if (!r) return;
    if (r.removed) {
      toast(`${a.name} desinstalado`);
      const items = await call<Leftover[]>('apps.leftovers', { name: a.name, location: a.location ?? '' }).catch(() => []);
      if (items.length) setLeft({ app: a, items, sel: new Set() });
    } else toast('A desinstalação foi cancelada ou ainda não terminou.', 'info');
    load();
  }

  async function removeLeftovers() {
    if (!left) return;
    let n = 0;
    for (const p of left.sel) if (await run(call('apps.deleteLeftover', { path: p }))) n++;
    toast(`${n} pasta(s) de resíduos enviadas para a Lixeira`);
    setLeft(null);
  }

  const total = (list ?? []).reduce((a, x) => a + x.size, 0);
  return (
    <>
      <PageHead title="Programas instalados" sub={list ? `${list.length} programas · ${bytes(total)} informados pelos instaladores` : 'Carregando…'}
        right={<>
          {tabs}
          <div style={{ position: 'relative', width: 280 }}>
            <Icon name="search" size={15} style={{ position: 'absolute', left: 11, top: 10, color: 'var(--text-3)' }} />
            <input type="search" placeholder="Buscar programa ou editor" value={q} onChange={e => setQ(e.target.value)} style={{ width: '100%', paddingLeft: 34 }} />
          </div>
          <select value={sort} onChange={e => setSort(e.target.value as any)}>
            <option value="name">Nome</option><option value="size">Tamanho</option><option value="date">Instalação recente</option>
          </select>
        </>} />
      <Card className="pad-0">
        {!list ? <div className="empty"><Spinner size={24} /></div> : (
          <div className="list">
            {rows.map(a => (
              <div key={a.id} className="li">
                {icons[a.id]
                  ? <div style={{ width: 38, height: 38, borderRadius: 11, display: 'grid', placeItems: 'center', flex: 'none', background: 'var(--surface-3)' }}><img src={icons[a.id]} width={26} height={26} alt="" style={{ imageRendering: 'auto' }} /></div>
                  : <div style={{ width: 38, height: 38, borderRadius: 11, display: 'grid', placeItems: 'center', flex: 'none', fontWeight: 700, fontSize: 15, color: '#fff', background: `hsl(${[...a.name].reduce((h, c) => h + c.charCodeAt(0), 0) % 360} 55% 38%)` }}>{a.name.trim()[0]?.toUpperCase()}</div>}
                <div className="grow">
                  <div className="t ellipsis">{a.name}</div>
                  <div className="s ellipsis">{[a.publisher, a.version && `v${a.version}`, a.date, a.scope].filter(Boolean).join(' · ')}</div>
                </div>
                <span className="small muted" style={{ width: 80, textAlign: 'right' }}>{a.size > 0 ? bytes(a.size) : ''}</span>
                <button className="btn sm danger" disabled={!!busy} onClick={() => uninstall(a)}>{busy === a.id ? <Spinner size={13} /> : <Icon name="trash" size={14} />}Desinstalar</button>
              </div>
            ))}
            {rows.length === 0 && <div className="empty">Nenhum programa encontrado.</div>}
          </div>
        )}
      </Card>

      <Modal open={!!left} onClose={() => setLeft(null)}>
        {left && <>
          <h3>Resíduos de {left.app.name}</h3>
          <p className="muted" style={{ marginTop: 0 }}>Estas pastas ficaram para trás. Elas vão para a Lixeira, então dá para recuperar se precisar.</p>
          <div className="list" style={{ border: '1px solid var(--line)', borderRadius: 12, margin: '10px 0 16px', maxHeight: 240, overflow: 'auto' }}>
            {left.items.map(i => (
              <label key={i.path} className="li" style={{ padding: '9px 12px', cursor: 'pointer' }}>
                <Check on={left.sel.has(i.path)} onChange={v => setLeft(l => { if (!l) return l; const s = new Set(l.sel); v ? s.add(i.path) : s.delete(i.path); return { ...l, sel: s }; })} />
                <div className="grow ellipsis small" title={i.path}>{i.path}</div>
                <b className="small">{bytes(i.size)}</b>
              </label>
            ))}
          </div>
          <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setLeft(null)}>Manter</button><button className="btn primary" disabled={left.sel.size === 0} onClick={removeLeftovers}><Icon name="trash" size={14} />Remover selecionados</button></div>
        </>}
      </Modal>
    </>
  );
}

type Upg = { name: string; id: string; version: string; available: string; source: string };
type WgSt = { running: boolean; current: string | null; state: Record<string, 'queued' | 'running' | 'done' | 'failed'> };

function UpdatesTab() {
  const [data, setData] = useState<{ available: boolean; items?: Upg[] } | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [st, setSt] = useState<WgSt>({ running: false, current: null, state: {} });
  const load = () => { setData(null); call<{ available: boolean; items?: Upg[] }>('winget.list').then(d => { setData(d); setSel(new Set((d.items ?? []).map(i => i.id))); }).catch(e => { toast(e.message, 'err'); setData({ available: true, items: [] }); }); };
  useEffect(() => {
    load();
    call<WgSt>('winget.status').then(setSt).catch(() => {});
    return on('winget', (s: WgSt) => { setSt(s); if (!s.running) { const ok = Object.values(s.state).filter(x => x === 'done').length; const bad = Object.values(s.state).filter(x => x === 'failed').length; if (ok + bad > 0) toast(`${ok} atualizado(s)${bad ? ` · ${bad} com falha` : ''}`, bad ? 'warn' : 'ok'); } });
  }, []);
  async function upgrade(ids: string[]) { const r = await run(call<WgSt>('winget.upgrade', { ids })); if (r) setSt(r); }
  if (!data) return <Card><div className="empty"><Spinner size={24} /><div className="small dim" style={{ marginTop: 8 }}>Perguntando ao winget o que tem versão nova…</div></div></Card>;
  if (!data.available) return <Card><div className="empty"><Icon name="package" size={28} /><div style={{ marginTop: 10 }}>O winget (Gerenciador de Pacotes do Windows) não está instalado. Instale o "Instalador de Aplicativo" pela Microsoft Store — no Windows 11 ele já vem.</div></div></Card>;
  const items = data.items ?? [];
  const label = { queued: 'na fila', running: 'atualizando…', done: 'atualizado', failed: 'falhou' } as const;
  return (
    <Card className="pad-0">
      <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
        <b>{items.length} programa(s) com versão nova</b><span className="spacer" />
        <button className="btn sm ghost" onClick={load} disabled={st.running}><Icon name="refresh" size={13} />Verificar de novo</button>
        <button className="btn sm" disabled={st.running || sel.size === 0} onClick={() => upgrade([...sel])}><Icon name="download" size={13} />Atualizar selecionados ({sel.size})</button>
        <button className="btn sm primary" disabled={st.running || items.length === 0} onClick={() => upgrade(items.map(i => i.id))}>{st.running ? <Spinner size={13} /> : <Icon name="zap" size={13} />}Atualizar tudo</button>
      </div>
      {items.length === 0 ? <div className="empty">Tudo atualizado.</div> : (
        <div className="list">
          {items.map(i => {
            const s = st.state[i.id];
            return (
              <label key={i.id} className="li" style={{ cursor: 'pointer' }}>
                <Check on={sel.has(i.id)} disabled={st.running} onChange={v => setSel(p => { const n = new Set(p); v ? n.add(i.id) : n.delete(i.id); return n; })} />
                <div className="grow"><div className="t ellipsis">{i.name}</div><div className="s ellipsis mono">{i.id}{i.source ? ` · ${i.source}` : ''}</div></div>
                <span className="small muted mono">{i.version || '?'} <Icon name="chevronRight" size={11} /> <b style={{ color: 'var(--ok)' }}>{i.available}</b></span>
                {s && <span className={`chip ${s === 'done' ? 'ok' : s === 'failed' ? 'bad' : 'accent'}`} style={{ minWidth: 92, justifyContent: 'center' }}>{s === 'running' && <Spinner size={10} />}{label[s]}</span>}
              </label>
            );
          })}
        </div>
      )}
      <div className="small dim" style={{ padding: '10px 18px 14px', borderTop: '1px solid var(--line)' }}>Atualiza em silêncio pelo winget, com os instaladores oficiais. Alguns programas pedem para fechar antes, ou abrem o próprio instalador.</div>
    </Card>
  );
}

export default function Apps() {
  const [tab, setTab] = useState<'installed' | 'updates'>('installed');
  return (
    <div className="page">
      {tab === 'installed' ? <Installed tabs={<Tabs layoutId="apptab" value={tab} onChange={setTab} items={[{ id: 'installed', label: 'Instalados' }, { id: 'updates', label: 'Atualizações' }]} />} />
        : <><PageHead title="Atualizar programas" sub="Versões novas dos seus programas pelo winget, com um clique." right={<Tabs layoutId="apptab" value={tab} onChange={setTab} items={[{ id: 'installed', label: 'Instalados' }, { id: 'updates', label: 'Atualizações' }]} />} /><UpdatesTab /></>}
    </div>
  );
}
