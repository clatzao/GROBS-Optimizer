import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Bar, Card, CardTitle, Check, Modal, PageHead, Spinner, Tabs } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, toast, useStore } from '../store';

type Drive = { root: string; label: string; total: number; free: number; system: boolean };
type Item = { id: string; cat: string; title: string; why: string; risk: 'tranquilo' | 'moderado' | 'perigoso' | 'protegido'; kind: string; path: string; size: number; modified: string | null; deletable: boolean; needsAdmin: boolean; inUse: string | null; note: string | null };
type FileRow = { name: string; path: string; size: number; modified: string };
type NodeRow = { name: string; path: string; size: number; files: number; modified: string | null; dirs: number };
type Report = any;
type DelRes = { results: { id: string; title: string; ok: boolean; before: number; after: number; msg: string | null }[]; drives: { root: string; before: number; after: number }[]; date: string };
type St = { running: boolean; deleting: boolean; phase: string; progress: number; error: string | null; report: Report | null; items: Item[] | null; lastDelete: DelRes | null; admin: boolean };

const CATS: [string, string, string][] = [
  ['lixeira', 'Lixeira', 'trash'], ['temp', 'Arquivos temporários', 'clock'], ['browser', 'Caches de navegadores', 'globe'], ['caches', 'Caches do Windows e de programas', 'broom'],
  ['dev', 'Caches de programação', 'wrench'], ['proj', 'Projetos (node_modules e Rust)', 'package'], ['update', 'Windows Update e Windows antigo', 'refresh'],
  ['downloads', 'Instaladores em Downloads', 'arrowDown'], ['big', 'Arquivos grandes (mais de 500 MB)', 'disk'], ['ai', 'IA, emuladores e máquinas virtuais', 'cpu'],
  ['system', 'Sistema (não apague à mão)', 'shield'], ['personal', 'Suas pastas (só informação)', 'eye'],
];
const RISK: Record<string, string> = { tranquilo: 'low', moderado: 'medium', perigoso: 'high' };
function Risk({ r }: { r: string }) { return r === 'protegido' ? <span className="chip">protegido</span> : <span className={`risk ${RISK[r]}`}>{r}</span>; }

export default function FullScan() {
  const admin = useStore(appStore, a => a?.admin);
  const [drives, setDrives] = useState<Drive[]>([]);
  const [pick, setPick] = useState<Set<string>>(new Set());
  const [st, setSt] = useState<St | null>(null);
  const [tab, setTab] = useState<'sum' | 'store' | 'map' | 'folders' | 'proc' | 'start' | 'svc' | 'disk'>('sum');
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [confirm, setConfirm] = useState(false);

  useEffect(() => {
    call<Drive[]>('fullscan.drives').then(d => { setDrives(d); setPick(new Set(d.filter(x => x.system).map(x => x.root))); }).catch(() => {});
    call<St>('fullscan.status').then(setSt).catch(() => {});
    return on('fullScan', (s: St) => { setSt(s); if (s.error && !s.running) toast(s.error, 'err'); });
  }, []);

  async function start() { setSel(new Set()); const r = await run(call<St>('fullscan.start', { roots: [...pick] })); if (r) setSt(r); }
  const items = st?.items ?? [];
  const chosen = items.filter(i => sel.has(i.id));
  const total = chosen.reduce((a, i) => a + i.size, 0);
  const toggle = (id: string, v: boolean) => setSel(s => { const n = new Set(s); v ? n.add(id) : n.delete(id); return n; });
  const r = st?.report;
  const busy = !!(st?.running || st?.deleting);

  return (
    <div className="page">
      <PageHead title="Leitura completa" sub="Lê o PC inteiro e diz o que deixa ele lento e o que ocupa cada disco. Nada é apagado nem alterado sem você marcar e aprovar item por item."
        right={st?.running ? <button className="btn" onClick={() => call('fullscan.cancel')}><Icon name="x" size={15} />Cancelar</button>
          : <button className="btn primary lg" onClick={start} disabled={busy || pick.size === 0}><Icon name="search" size={16} />{r ? 'Ler de novo' : 'Fazer a leitura'}</button>} />

      <Card style={{ marginBottom: 16 }}>
        <CardTitle icon="disk" color="#fbbf24" right={<>
          <button className="btn sm ghost" disabled={busy} onClick={() => setPick(new Set(drives.filter(d => d.system).map(d => d.root)))}>Só o do Windows</button>
          <button className="btn sm ghost" disabled={busy} onClick={() => setPick(new Set(drives.map(d => d.root)))}>Todos</button>
        </>}>Quais discos ler</CardTitle>
        <div className="grid g3">
          {drives.map(d => {
            const used = (1 - d.free / d.total) * 100;
            return <label key={d.root} className="li" style={{ cursor: 'pointer', border: '1px solid var(--line)', alignItems: 'flex-start' }}>
              <Check on={pick.has(d.root)} disabled={busy} onChange={v => setPick(s => { const n = new Set(s); v ? n.add(d.root) : n.delete(d.root); return n; })} />
              <div className="grow">
                <b>{d.root.replace('\\', '')} {d.label && <span className="muted">{d.label}</span>}</b>{d.system && <span className="chip" style={{ marginLeft: 6 }}>Windows</span>}
                <div style={{ margin: '6px 0 4px' }}><Bar value={used} kind={d.free / d.total < 0.15 ? 'bad' : used > 80 ? 'warn' : undefined} /></div>
                <div className="small dim">{bytes(d.free)} livres de {bytes(d.total)}</div>
              </div>
            </label>;
          })}
        </div>
      </Card>

      {busy && <Card style={{ marginBottom: 16 }}>
        <div className="row"><Spinner color="var(--accent)" /><b className="grow ellipsis" title={st?.phase}>{st?.phase || 'Preparando…'}</b><span className="small dim">{Math.round(st?.progress ?? 0)}%</span></div>
        <div style={{ marginTop: 10 }}><Bar value={st?.progress ?? 0} /></div>
        {st?.running && <div className="small dim" style={{ marginTop: 8 }}>A leitura do disco pode levar alguns minutos. O uso de CPU, memória e disco foi medido antes de começar, para não sair distorcido.</div>}
      </Card>}

      {!r && !busy && <Card><div className="empty"><Icon name="search" size={30} /><div style={{ marginTop: 10 }}>Escolha os discos e clique em <b>Fazer a leitura</b>. A leitura só lê: mede o tamanho real de cada pasta (inclusive discos virtuais de emuladores, que mostram mais do que ocupam), lista arquivos acima de 500 MB, programas que abrem com o Windows, serviços de terceiros e a saúde dos discos.</div></div></Card>}

      {r && <>
        <div style={{ marginBottom: 14, overflowX: 'auto' }}>
          <Tabs layoutId="fstab" value={tab} onChange={setTab} items={[{ id: 'sum', label: 'Resumo' }, { id: 'store', label: 'Armazenamento' }, { id: 'map', label: 'Mapa' }, { id: 'folders', label: 'Pastas' },
            { id: 'proc', label: 'Processos' }, { id: 'start', label: 'Inicialização' }, { id: 'svc', label: 'Serviços' }, { id: 'disk', label: 'Disco e saúde' }]} />
        </div>
        {tab === 'sum' && <Summary r={r} items={items} onPick={ids => { setSel(s => new Set([...s, ...ids])); setTab('store'); }} />}
        {tab === 'store' && <Storage items={items} sel={sel} toggle={toggle} clear={() => setSel(new Set())} total={total} count={chosen.length} onDelete={() => setConfirm(true)} busy={busy} last={st?.lastDelete ?? null} />}
        {tab === 'map' && <MapView roots={r.roots} />}
        {tab === 'folders' && <Folders r={r} />}
        {tab === 'proc' && <Processes p={r.processes} />}
        {tab === 'start' && <StartupList list={r.startup} admin={!!admin} />}
        {tab === 'svc' && <ServiceList list={r.services} admin={!!admin} />}
        {tab === 'disk' && <Health h={r.health} />}
      </>}

      <ConfirmDelete open={confirm} items={chosen} total={total} onClose={() => setConfirm(false)} onOk={async () => {
        setConfirm(false);
        const res = await run(call<St>('fullscan.delete', { ids: chosen.map(i => i.id) }));
        if (res) { setSt(res); setSel(new Set()); setTab('store'); }
      }} />
    </div>
  );
}

function Summary({ r, items, onPick }: { r: Report; items: Item[]; onPick: (ids: string[]) => void }) {
  const s = r.system ?? {};
  const color: Record<string, string> = { bad: '#f87171', warn: '#fbbf24', info: '#60a5fa' };
  const safe = items.filter(i => i.deletable && i.risk === 'tranquilo');
  return <>
    <Card style={{ marginBottom: 16 }}>
      <CardTitle icon="cpu" color="#8b5cf6" right={<span className="small dim">leitura de {r.date}</span>}>Hardware e uso agora</CardTitle>
      <dl className="kv">
        <dt>Processador</dt><dd>{s.cpu} · {s.cores} núcleos / {s.threads} threads · {s.cpuLoad}% em uso</dd>
        <dt>Memória</dt><dd>{bytes(s.ramTotal - s.ramFree)} de {bytes(s.ramTotal)} em uso ({s.ramUsedPct}%)</dd>
        <dt>Disco</dt><dd>{s.diskBusy ?? '—'}% ocupado (medido antes da varredura)</dd>
        <dt>Ligado desde</dt><dd>{s.boot} ({s.uptimeHours >= 48 ? `${Math.round(s.uptimeHours / 24)} dias` : `${s.uptimeHours} h`})</dd>
        <dt>Sistema</dt><dd>{s.os}{s.model ? ` · ${s.model}` : ''}</dd>
      </dl>
    </Card>
    <Card style={{ marginBottom: 16 }}>
      <CardTitle icon="alert" color="#f87171">Causas prováveis da lentidão</CardTitle>
      {r.causes.length === 0 ? <div className="small ok-t"><Icon name="check" size={12} /> Nada fora do normal.</div> :
        <div className="list">{r.causes.map((c: any, i: number) => <div key={i} className="li" style={{ alignItems: 'flex-start', borderLeft: `3px solid ${color[c.level]}` }}>
          <div className="grow"><b>{c.title}</b><div className="small dim" style={{ marginTop: 2 }}>{c.detail}</div></div></div>)}</div>}
    </Card>
    <Card style={{ marginBottom: 16 }}>
      <CardTitle icon="sparkles" color="#a78bfa" right={safe.length > 0 && <button className="btn sm primary" onClick={() => onPick(safe.map(i => i.id))}><Icon name="check" size={13} />Marcar os tranquilos ({bytes(safe.reduce((a, i) => a + i.size, 0))})</button>}>O que mais libera espaço</CardTitle>
      {r.advice.length === 0 ? <div className="small dim">Nada grande para liberar.</div> :
        <div className="list">{r.advice.map((a: any) => <div key={a.id} className="li" style={{ alignItems: 'flex-start' }}>
          <div className="grow"><div className="row wrap" style={{ gap: 8 }}><b>{a.title}</b><Risk r={a.risk} /></div><div className="small dim" style={{ marginTop: 2 }}>{a.why}</div></div>
          <b className="small" style={{ flex: 'none' }}>{bytes(a.size)}</b>
          <button className="btn sm ghost" style={{ flex: 'none' }} onClick={() => onPick([a.id])}>Marcar</button></div>)}</div>}
    </Card>
    {r.notChecked.length > 0 && <Card>
      <CardTitle icon="info" color="#60a5fa">O que não deu para verificar</CardTitle>
      {r.notChecked.map((t: string) => <div key={t} className="small dim" style={{ marginBottom: 4 }}>• {t}</div>)}
    </Card>}
  </>;
}

function Storage({ items, sel, toggle, clear, total, count, onDelete, busy, last }: { items: Item[]; sel: Set<string>; toggle: (id: string, v: boolean) => void; clear: () => void; total: number; count: number; onDelete: () => void; busy: boolean; last: DelRes | null }) {
  const [open, setOpen] = useState<Record<string, FileRow[] | 'loading'>>({});
  async function files(id: string) {
    if (open[id]) { setOpen(o => { const n = { ...o }; delete n[id]; return n; }); return; }
    setOpen(o => ({ ...o, [id]: 'loading' }));
    const f = await call<FileRow[]>('fullscan.files', { id }).catch(() => []);
    setOpen(o => ({ ...o, [id]: f }));
  }
  return <>
    {last && <Card style={{ marginBottom: 16, borderColor: 'rgba(52,211,153,.35)' }}>
      <CardTitle icon="check" color="#34d399" right={<span className="small dim">às {last.date}</span>}>Resultado da limpeza</CardTitle>
      {last.drives.map(d => <div key={d.root} className="small" style={{ marginBottom: 6 }}><b>{d.root}</b> livre: {bytes(d.before)} → <b className="ok-t">{bytes(d.after)}</b> (+{bytes(Math.max(0, d.after - d.before))})</div>)}
      <div className="list">{last.results.map(x => <div key={x.id} className="li" style={{ padding: '6px 0' }}>
        <Icon name={x.ok ? 'check' : 'alert'} size={13} style={{ color: x.ok ? 'var(--ok)' : 'var(--bad)' }} />
        <span className="small grow">{x.title}{x.msg && <span className="dim"> · {x.msg}</span>}</span>
        <span className="small dim">{bytes(x.before)} → {bytes(x.after)}</span></div>)}</div>
    </Card>}
    <div className="row" style={{ marginBottom: 10, position: 'sticky', top: 0, zIndex: 2, background: 'var(--bg)', padding: '6px 0' }}>
      <b>{count} marcado(s) · {bytes(total)}</b><span className="spacer" />
      <button className="btn sm ghost" onClick={clear} disabled={count === 0}>Desmarcar tudo</button>
      <button className="btn sm danger" onClick={onDelete} disabled={count === 0 || busy}>{busy ? <Spinner size={13} /> : <Icon name="trash" size={14} />}Apagar marcados</button>
    </div>
    {CATS.map(([cat, label, icon]) => {
      const list = items.filter(i => i.cat === cat).sort((a, b) => b.size - a.size);
      if (list.length === 0) return null;
      return <Card key={cat} className="pad-0" style={{ marginBottom: 14 }}>
        <div className="row" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}><Icon name={icon} size={15} /><b className="grow">{label}</b><span className="small dim">{list.length} · {bytes(list.reduce((a, i) => a + i.size, 0))}</span></div>
        <div className="list">{list.map(i => {
          const f = open[i.id];
          return <div key={i.id}>
            <div className="li" style={{ alignItems: 'flex-start' }}>
              {i.deletable ? <Check on={sel.has(i.id)} onChange={v => toggle(i.id, v)} /> : <span style={{ width: 20, flex: 'none' }} />}
              <div className="grow" style={{ minWidth: 0 }}>
                <div className="row wrap" style={{ gap: 8 }}>
                  <span className="t">{i.title}</span><Risk r={i.risk} />
                  {i.inUse && <span className="chip warn">aberto: {i.inUse}</span>}
                  {i.needsAdmin && !i.deletable && <span className="chip">precisa de administrador</span>}
                  {i.modified && <span className="small dim">{i.modified}</span>}
                </div>
                <div className="s selectable" style={{ marginTop: 3 }}>{i.why}</div>
                {i.note && <div className="small warn-t" style={{ marginTop: 2 }}>{i.note}</div>}
                {i.path && i.kind !== 'clean' && i.kind !== 'recycle' && <div className="small mono dim ellipsis selectable" style={{ marginTop: 2 }} title={i.path}>{i.path}</div>}
              </div>
              <b className="small" style={{ flex: 'none' }}>{bytes(i.size)}</b>
              {i.path && i.kind !== 'clean' && i.kind !== 'recycle' && <>
                <button className="btn sm ghost" style={{ flex: 'none' }} onClick={() => files(i.id)}>{f === 'loading' ? <Spinner size={11} /> : <Icon name="list" size={12} />}Arquivos</button>
                <button className="btn sm ghost iconbtn" style={{ flex: 'none' }} title="Mostrar na pasta" onClick={() => call('shell.reveal', { path: i.path })}><Icon name="external" size={12} /></button>
              </>}
            </div>
            {Array.isArray(f) && <div style={{ padding: '0 16px 10px 48px' }}>
              {f.length === 0 ? <div className="small dim">Sem arquivos para mostrar.</div> :
                <table className="small" style={{ width: '100%', borderCollapse: 'collapse' }}><tbody>{f.map(x => <tr key={x.path} title={x.path}>
                  <td className="ellipsis mono" style={{ maxWidth: 420, padding: '2px 8px 2px 0' }}>{x.path.replace(i.path, '…')}</td><td className="dim" style={{ whiteSpace: 'nowrap' }}>{x.modified}</td><td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}><b>{bytes(x.size)}</b></td></tr>)}</tbody></table>}
            </div>}
          </div>;
        })}</div>
      </Card>;
    })}
  </>;
}

function ConfirmDelete({ open, items, total, onClose, onOk }: { open: boolean; items: Item[]; total: number; onClose: () => void; onOk: () => void }) {
  const danger = items.filter(i => i.risk === 'perigoso');
  const [ack, setAck] = useState<Set<string>>(new Set());
  useEffect(() => { if (open) setAck(new Set()); }, [open]);
  const ready = danger.every(i => ack.has(i.id));
  return <Modal open={open} onClose={onClose}>
    <h3>Apagar {items.length} item(ns)?</h3>
    <p className="muted" style={{ marginTop: 0 }}>{bytes(total)} no total. <b>É definitivo: não vai para a Lixeira.</b> Antes de apagar, o GROBS confere de novo o tamanho e se algum programa está usando; depois confirma item por item e mede o espaço livre.</p>
    <div className="list" style={{ border: '1px solid var(--line)', borderRadius: 12, marginBottom: 14, maxHeight: 260, overflow: 'auto' }}>
      {items.map(i => <div key={i.id} className="li" style={{ padding: '7px 12px' }}>
        {i.risk === 'perigoso' ? <Check on={ack.has(i.id)} onChange={v => setAck(s => { const n = new Set(s); v ? n.add(i.id) : n.delete(i.id); return n; })} /> : <Icon name="check" size={12} />}
        <span className="small grow ellipsis" title={i.path}>{i.title}</span><Risk r={i.risk} />{i.inUse && <span className="chip warn">aberto</span>}<b className="small">{bytes(i.size)}</b></div>)}
    </div>
    {danger.length > 0 && <p className="small bad-t" style={{ marginTop: 0 }}>Há {danger.length} item(ns) perigoso(s). Marque cada um na lista para confirmar que sabe o que vai perder.</p>}
    {items.some(i => i.inUse) && <p className="small warn-t" style={{ marginTop: 0 }}>Itens com programa aberto serão pulados. Feche o programa e apague depois.</p>}
    <div className="row"><span className="spacer" /><button className="btn ghost" onClick={onClose}>Cancelar</button><button className="btn danger" disabled={!ready} onClick={onOk}><Icon name="trash" size={14} />Apagar definitivamente</button></div>
  </Modal>;
}

// ---------------------------------------------------------------- mapa visual (blocos proporcionais)
type Rect = { x: number; y: number; w: number; h: number; n: NodeRow | { name: string; path: string; size: number; own: true } };
function squarify(nodes: { size: number }[], x: number, y: number, w: number, h: number): { x: number; y: number; w: number; h: number }[] {
  const out: { x: number; y: number; w: number; h: number }[] = [];
  const total = nodes.reduce((a, n) => a + n.size, 0);
  if (total <= 0) return out;
  const scale = (w * h) / total;
  let rest = nodes.map(n => n.size * scale);
  let cx = x, cy = y, cw = w, ch = h;
  while (rest.length) {
    const side = Math.min(cw, ch);
    let row: number[] = [], best = Infinity;
    for (const a of rest) {
      const t = [...row, a], s = t.reduce((p, q) => p + q, 0);
      const worst = Math.max(...t.map(v => Math.max((side * side * v) / (s * s), (s * s) / (side * side * v))));
      if (worst > best) break;
      best = worst; row = t;
    }
    const s = row.reduce((p, q) => p + q, 0), thick = s / side;
    let off = 0;
    for (const a of row) {
      const len = a / thick;
      out.push(cw >= ch ? { x: cx, y: cy + off, w: thick, h: len } : { x: cx + off, y: cy, w: len, h: thick });
      off += len;
    }
    if (cw >= ch) { cx += thick; cw -= thick; } else { cy += thick; ch -= thick; }
    rest = rest.slice(row.length);
  }
  return out;
}

const PAL = ['#8b5cf6', '#22d3ee', '#f472b6', '#fbbf24', '#34d399', '#60a5fa', '#fb7185', '#a78bfa', '#2dd4bf', '#f97316'];

function MapView({ roots }: { roots: string[] }) {
  const [path, setPath] = useState(roots[0]);
  const [d, setD] = useState<any>(null);
  useEffect(() => { setD(null); call<any>('fullscan.children', { path }).then(setD).catch(() => {}); }, [path]);
  const W = 900, H = 420;
  const rects: Rect[] = useMemo(() => {
    if (!d?.dirs) return [];
    const nodes: Rect['n'][] = [...d.dirs.filter((x: NodeRow) => x.size > 0)];
    if (d.own > 0) nodes.push({ name: 'arquivos soltos nesta pasta', path: d.node.path, size: d.own, own: true });
    nodes.sort((a, b) => b.size - a.size);
    return squarify(nodes, 0, 0, W, H).map((r, i) => ({ ...r, n: nodes[i] }));
  }, [d]);
  const crumbs = path.split('\\').filter(Boolean);
  return <>
    <div className="row wrap" style={{ marginBottom: 10, gap: 6 }}>
      {roots.length > 1 && roots.map(rt => <button key={rt} className={`btn sm ${path.startsWith(rt) ? 'primary' : 'ghost'}`} onClick={() => setPath(rt)}>{rt.replace('\\', '')}</button>)}
      <span className="small mono">{crumbs.map((c, i) => <span key={i}>{i > 0 && ' › '}<a style={{ cursor: 'pointer' }} onClick={() => setPath(crumbs.slice(0, i + 1).join('\\') + (i === 0 ? '\\' : ''))}>{c}</a></span>)}</span>
      <span className="spacer" />{d?.node && <b className="small">{bytes(d.node.size)}</b>}
    </div>
    <Card className="pad-0" style={{ overflow: 'hidden', marginBottom: 14 }}>
      {!d ? <div className="empty"><Spinner /></div> : rects.length === 0 ? <div className="empty">Pasta vazia ou pequena demais para o mapa.</div> :
        <svg viewBox={`0 0 ${W} ${H}`} style={{ width: '100%', display: 'block' }}>
          {rects.map((r, i) => {
            const own = 'own' in r.n, c = own ? '#4b5563' : PAL[i % PAL.length];
            return <g key={r.n.path + i} style={{ cursor: own ? 'default' : 'pointer' }} onClick={() => !own && setPath(r.n.path)}>
              <title>{r.n.name} · {bytes(r.n.size)}</title>
              <rect x={r.x + 1} y={r.y + 1} width={Math.max(0, r.w - 2)} height={Math.max(0, r.h - 2)} fill={c} fillOpacity={0.28} stroke={c} strokeWidth={2} />
              {r.w > 70 && r.h > 28 && <text x={r.x + 8} y={r.y + 18} fontSize={12} fill="var(--text)" style={{ pointerEvents: 'none' }}>{r.n.name.length > r.w / 8 ? r.n.name.slice(0, Math.floor(r.w / 8) - 1) + '…' : r.n.name}</text>}
              {r.w > 70 && r.h > 44 && <text x={r.x + 8} y={r.y + 34} fontSize={11} fill="var(--text-2)" style={{ pointerEvents: 'none' }}>{bytes(r.n.size)}</text>}
            </g>;
          })}
        </svg>}
    </Card>
    {d?.parent && <button className="btn sm ghost" style={{ marginBottom: 10 }} onClick={() => setPath(d.parent)}><Icon name="arrowUp" size={12} />Voltar uma pasta</button>}
    {d?.files?.length > 0 && <Card>
      <CardTitle icon="list" color="#60a5fa">Maiores arquivos direto nesta pasta</CardTitle>
      <table className="small" style={{ width: '100%', borderCollapse: 'collapse' }}><tbody>{d.files.map((f: FileRow) => <tr key={f.path}>
        <td className="ellipsis mono" style={{ maxWidth: 520, padding: '2px 8px 2px 0' }} title={f.path}>{f.name}</td><td className="dim">{f.modified}</td><td style={{ textAlign: 'right' }}><b>{bytes(f.size)}</b></td></tr>)}</tbody></table>
    </Card>}
  </>;
}

function Folders({ r }: { r: Report }) {
  return <>
    <Card style={{ marginBottom: 14 }}>
      <CardTitle icon="pie" color="#fbbf24">Pastas específicas</CardTitle>
      <table className="small" style={{ width: '100%', borderCollapse: 'collapse' }}><tbody>{r.specific.map((s: any) => <tr key={s.path} title={s.path}>
        <td style={{ padding: '3px 8px 3px 0' }}>{s.title}</td><td className="mono dim ellipsis" style={{ maxWidth: 420 }}>{s.path}</td><td style={{ textAlign: 'right' }}><b>{bytes(s.size)}</b></td></tr>)}</tbody></table>
      <div className="small dim" style={{ marginTop: 8 }}>O WinSxS conta arquivos compartilhados com o Windows, então parece maior do que realmente ocupa.</div>
    </Card>
    <div className="grid g2">
      {r.folders.map((f: any) => <Card key={f.path}>
        <CardTitle icon="disk" color="#8b5cf6" right={<b className="small">{bytes(f.size)}</b>}>{f.title}</CardTitle>
        {f.top.map((n: NodeRow) => <div key={n.path} style={{ marginBottom: 6 }} title={n.path}>
          <div className="row small"><span className="grow ellipsis">{n.name}</span><b>{bytes(n.size)}</b></div>
          <Bar value={f.size > 0 ? (n.size / f.size) * 100 : 0} />
        </div>)}
      </Card>)}
    </div>
  </>;
}

function Processes({ p }: { p: any }) {
  const table = (rows: any[], key: 'cpuSec' | 'ramMB') => <table className="small" style={{ width: '100%', borderCollapse: 'collapse' }}><tbody>
    {rows.map(x => <tr key={x.name}><td style={{ padding: '3px 8px 3px 0' }}>{x.name}</td><td className="dim">{x.count} proc.</td>
      <td style={{ textAlign: 'right' }}><b>{key === 'ramMB' ? (x.ramMB >= 1024 ? `${(x.ramMB / 1024).toFixed(1)} GB` : `${x.ramMB} MB`) : x.cpuSec >= 3600 ? `${(x.cpuSec / 3600).toFixed(1)} h` : x.cpuSec >= 60 ? `${Math.round(x.cpuSec / 60)} min` : `${x.cpuSec} s`}</b></td></tr>)}
  </tbody></table>;
  return <div className="grid g2">
    {p.byCpuNow?.length > 0 && <Card style={{ gridColumn: '1 / -1' }}><CardTitle icon="activity" color="#f87171">Usando a CPU agora (medido antes da varredura)</CardTitle>
      {p.byCpuNow.map((x: any) => <div key={x.name} style={{ marginBottom: 6 }}><div className="row small"><span className="grow">{x.name}</span><b>{x.pct}%</b></div><Bar value={x.pct} kind={x.pct >= 50 ? 'bad' : x.pct >= 20 ? 'warn' : undefined} /></div>)}</Card>}
    <Card><CardTitle icon="cpu" color="#8b5cf6">Mais tempo de CPU desde que ligou</CardTitle>{table(p.byCpu, 'cpuSec')}</Card>
    <Card><CardTitle icon="memory" color="#22d3ee">Mais memória agora</CardTitle>{table(p.byRam, 'ramMB')}<div className="small dim" style={{ marginTop: 8 }}>Agrupado por nome: soma todos os processos do mesmo programa. {p.total} processos no total.</div></Card>
  </div>;
}

function StartupList({ list, admin }: { list: any[]; admin: boolean }) {
  const [state, setState] = useState<Record<string, boolean>>({});
  async function set(x: any, enabled: boolean) {
    const ok = await run(call('startup.set', { id: x.id, enabled }));
    if (ok !== undefined) { setState(s => ({ ...s, [x.id]: enabled })); toast(enabled ? 'Ativado' : 'Desativado: não abre mais com o Windows'); }
  }
  return <Card className="pad-0"><div className="list">{list.map((x, i) => {
    const en = state[x.id] ?? x.enabled;
    return <div key={x.id ?? i} className="li" style={{ alignItems: 'flex-start' }}>
      <div className="grow" style={{ minWidth: 0 }}>
        <div className="row wrap" style={{ gap: 8 }}><b>{x.name}</b><span className={`chip ${en ? 'ok' : ''}`}>{en ? 'ativo' : 'desativado'}</span>
          {x.unknown && <span className="chip bad"><Icon name="alert" size={10} />origem desconhecida</span>}{x.protected && <span className="chip">não mexer</span>}</div>
        <div className="small dim" style={{ marginTop: 2 }}>{x.company || x.signer || 'fabricante desconhecido'} · {x.location}</div>
        {x.flags?.length > 0 && <div className="small bad-t" style={{ marginTop: 2 }}>{x.flags.join(' · ')}</div>}
        <div className="small mono dim ellipsis selectable" title={x.command}>{x.command}</div>
      </div>
      {x.id && !x.protected && (x.admin && !admin ? <span className="small dim">precisa de administrador</span>
        : <button className={`btn sm ${en ? 'ghost' : 'primary'}`} style={{ flex: 'none' }} onClick={() => set(x, !en)}>{en ? 'Desativar' : 'Ativar'}</button>)}
    </div>;
  })}</div></Card>;
}

function ServiceList({ list, admin }: { list: any[]; admin: boolean }) {
  const [off, setOff] = useState<Record<string, boolean>>({});
  async function set(x: any, disable: boolean) {
    const ok = await run(call('fullscan.service', { name: x.name, disable }));
    if (ok !== undefined) { setOff(s => ({ ...s, [x.name]: disable })); toast(disable ? 'Serviço parado e desativado' : 'Serviço reativado'); }
  }
  return <Card className="pad-0">
    <div className="small dim" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}>Serviços em execução que não são do Windows. Desative só os de programas que você não usa. Segurança, antivírus e drivers ficam protegidos.</div>
    <div className="list">{list.map(x => <div key={x.name} className="li" style={{ alignItems: 'flex-start' }}>
      <div className="grow" style={{ minWidth: 0 }}>
        <div className="row wrap" style={{ gap: 8 }}><b>{x.display}</b><span className="chip">{x.startMode}</span>{x.protected && <span className="chip">não mexer</span>}{off[x.name] && <span className="chip warn">desativado</span>}</div>
        <div className="small dim">{x.company || 'fabricante desconhecido'} · {x.name}</div>
        <div className="small mono dim ellipsis selectable" title={x.path}>{x.path}</div>
      </div>
      {!x.protected && (admin ? <button className="btn sm ghost" style={{ flex: 'none' }} onClick={() => set(x, !off[x.name])}>{off[x.name] ? 'Reativar' : 'Desativar'}</button> : <span className="small dim">precisa de administrador</span>)}
    </div>)}</div>
  </Card>;
}

function Health({ h }: { h: any }) {
  const lab: Record<string, string> = { ok: 'Saudável', warn: 'Alerta', bad: 'Falha', unknown: 'Desconhecido' };
  return <>
    <div className="grid g2" style={{ marginBottom: 14 }}>
      {h.physical.map((p: any) => <Card key={p.name}>
        <CardTitle icon="disk" color={p.health === 'ok' ? '#34d399' : p.health === 'bad' ? '#f87171' : '#fbbf24'} right={<span className={`chip ${p.health === 'ok' ? 'ok' : p.health === 'bad' ? 'bad' : 'warn'}`}>{lab[p.health]}</span>}>{p.name}</CardTitle>
        <dl className="kv"><dt>Tipo</dt><dd>{p.type}{p.bus ? ` · ${p.bus}` : ''}</dd><dt>Capacidade</dt><dd>{bytes(p.size)}</dd>
          {p.wear != null && <><dt>Desgaste</dt><dd>{p.wear}%</dd></>}{p.temp > 0 && <><dt>Temperatura</dt><dd>{p.temp} °C</dd></>}
          {p.hours > 0 && <><dt>Horas ligado</dt><dd>{p.hours} h</dd></>}{p.readErrors != null && <><dt>Erros de leitura</dt><dd>{p.readErrors}</dd></>}</dl>
      </Card>)}
    </div>
    <Card style={{ marginBottom: 14 }}>
      <CardTitle icon="pie" color="#fbbf24">Espaço em cada disco</CardTitle>
      {h.volumes.map((v: any) => <div key={v.root} style={{ marginBottom: 10 }}>
        <div className="row small"><b className="grow">{v.root} {v.label}</b>{v.alert && <span className="chip bad">menos de 15% livre</span>}<span className="dim">{bytes(v.free)} livres de {bytes(v.total)} ({v.freePct}%)</span></div>
        <Bar value={100 - v.freePct} kind={v.freePct < 15 ? 'bad' : v.freePct < 25 ? 'warn' : undefined} />
      </div>)}
    </Card>
    <Card>
      <CardTitle icon="memory" color="#a78bfa">Arquivos do sistema e restauração</CardTitle>
      <dl className="kv">
        {h.sysFiles.map((f: any) => <span key={f.name} style={{ display: 'contents' }}><dt>{f.name}</dt><dd>{f.size > 0 ? bytes(f.size) : 'não existe'}</dd></span>)}
        <dt>Pontos de restauração</dt><dd>{h.restorePoints === undefined ? 'precisa de administrador' : h.restorePoints === 0 ? 'nenhum' : `${h.restorePoints} (último ${h.restoreLast})`}</dd>
      </dl>
    </Card>
  </>;
}
