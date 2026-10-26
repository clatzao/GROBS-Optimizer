import { motion } from 'motion/react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Beam, Card, CardTitle, PageHead, Spinner } from '../components/ui';
import { bytes } from '../format';
import { run } from '../store';

type Node = { name: string; size: number; files: number; dirs: number };
type View = { root: string; path: string; size: number; files: number; looseFiles: number; children: Node[]; bigFiles: { path: string; size: number }[] };
type Rect = { x: number; y: number; w: number; h: number; node: Node; i: number };

/** Treemap "squarified" (Bruls et al.) — retângulos o mais quadrados possível. */
function squarify(nodes: Node[], W: number, H: number): Rect[] {
  const total = nodes.reduce((a, n) => a + n.size, 0);
  if (!total || W <= 0 || H <= 0) return [];
  const scale = (W * H) / total;
  const items = nodes.map((n, i) => ({ n, i, a: n.size * scale })).filter(x => x.a > 0);
  const out: Rect[] = [];
  let x = 0, y = 0, w = W, h = H;
  let row: typeof items = [];
  const worst = (r: typeof items, side: number) => {
    const s = r.reduce((a, it) => a + it.a, 0);
    let mx = 0, mn = Infinity;
    r.forEach(it => { mx = Math.max(mx, it.a); mn = Math.min(mn, it.a); });
    return Math.max((side * side * mx) / (s * s), (s * s) / (side * side * mn));
  };
  const layout = (r: typeof items) => {
    const s = r.reduce((a, it) => a + it.a, 0);
    if (w >= h) {
      const cw = s / h; let cy = y;
      r.forEach(it => { const ch = it.a / cw; out.push({ x, y: cy, w: cw, h: ch, node: it.n, i: it.i }); cy += ch; });
      x += cw; w -= cw;
    } else {
      const ch = s / w; let cx = x;
      r.forEach(it => { const cw = it.a / ch; out.push({ x: cx, y, w: cw, h: ch, node: it.n, i: it.i }); cx += cw; });
      y += ch; h -= ch;
    }
  };
  for (const it of items) {
    const side = Math.min(w, h);
    if (row.length === 0 || worst([...row, it], side) <= worst(row, side)) row.push(it);
    else { layout(row); row = [it]; }
  }
  if (row.length) layout(row);
  return out;
}

const COLORS = ['#7c3aed', '#0891b2', '#db2777', '#059669', '#d97706', '#4f46e5', '#0d9488', '#be123c', '#65a30d', '#9333ea'];

function Treemap({ nodes, onOpen }: { nodes: Node[]; onOpen: (n: Node) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0 });
  useEffect(() => {
    const el = ref.current!;
    const ro = new ResizeObserver(() => setSize({ w: el.clientWidth, h: el.clientHeight }));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);
  const rects = useMemo(() => squarify(nodes.slice(0, 40), size.w, size.h), [nodes, size]);
  return (
    <div ref={ref} style={{ position: 'relative', height: 340, borderRadius: 12, overflow: 'hidden', background: 'var(--bg-2)' }}>
      {rects.map(r => (
        <motion.button key={r.node.name} title={`${r.node.name} — ${bytes(r.node.size)}`} onClick={() => r.node.dirs >= 0 && onOpen(r.node)}
          initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} transition={{ delay: Math.min(r.i * 0.02, 0.4), duration: 0.35 }}
          style={{ position: 'absolute', left: r.x, top: r.y, width: r.w, height: r.h, border: '1px solid var(--bg)', borderRadius: 6, padding: 0,
            background: `linear-gradient(135deg, ${COLORS[r.i % COLORS.length]}cc, ${COLORS[r.i % COLORS.length]}66)`, color: '#fff', textAlign: 'left', overflow: 'hidden' }}>
          {r.w > 70 && r.h > 34 && (
            <span style={{ position: 'absolute', left: 8, top: 6, right: 6, fontSize: 12, lineHeight: 1.25 }}>
              <b className="ellipsis" style={{ display: 'block' }}>{r.node.name}</b>
              <span style={{ opacity: .8 }}>{bytes(r.node.size)}</span>
            </span>
          )}
        </motion.button>
      ))}
    </div>
  );
}

export default function Disk() {
  const [vols, setVols] = useState<any[]>([]);
  const [view, setView] = useState<View | null>(null);
  const [running, setRunning] = useState(false);
  const [files, setFiles] = useState(0);
  const poll = useRef<number | undefined>(undefined);

  useEffect(() => {
    call('sys.disks').then((d: any) => setVols(d.volumes)).catch(() => {});
    call<View>('disk.children', { sub: '' }).then(v => v && setView(v)).catch(() => {});
    const off = on('diskDone', async () => {
      clearInterval(poll.current); setRunning(false);
      const v = await call<View>('disk.children', { sub: '' }).catch(() => null);
      if (v) setView(v);
    });
    return () => { off(); clearInterval(poll.current); };
  }, []);

  async function start(path: string) {
    setRunning(true); setFiles(0); setView(null);
    await run(call('disk.start', { path }));
    clearInterval(poll.current);
    poll.current = window.setInterval(() => call<any>('disk.status').then(s => setFiles(s.files)).catch(() => {}), 500);
  }
  async function pick() { const p = await call<string>('shell.pickFolder'); if (p) start(p); }
  async function open(sub: string) { const v = await call<View>('disk.children', { sub }); if (v) setView(v); }

  const crumbs = view ? ['', ...view.path.split('\\').filter(Boolean)] : [];
  const fullPath = (n?: string) => view ? view.root + (view.path ? view.path + '\\' : '') + (n ?? '') : '';

  return (
    <div className="page">
      <PageHead title="Espaço em disco" sub="Descubra o que está ocupando espaço — no estilo WizTree/TreeSize."
        right={<>
          {vols.map(v => <button key={v.name} className="btn" disabled={running} onClick={() => start(v.name + '\\')}><Icon name="disk" size={15} />{v.name} <span className="dim small">{bytes(v.total - v.free)}</span></button>)}
          <button className="btn" disabled={running} onClick={pick}><Icon name="folder" size={15} />Pasta…</button>
        </>} />

      {running && (
        <Card style={{ padding: 30, textAlign: 'center', overflow: 'hidden' }}>
          <Beam size={200} duration={2.5} />
          <div style={{ display: 'grid', placeItems: 'center', marginBottom: 16 }}><div className="tri-loader" /></div>
          <div className="stat-big" style={{ fontSize: 24 }}>{files.toLocaleString('pt-BR')} arquivos</div>
          <div className="muted" style={{ marginBottom: 14 }}>Medindo pastas… em SSD leva poucos segundos; em HD pode levar alguns minutos.</div>
          <button className="btn" onClick={() => { call('disk.cancel'); setRunning(false); clearInterval(poll.current); }}>Cancelar</button>
        </Card>
      )}

      {!running && !view && (
        <Card style={{ padding: 40, textAlign: 'center' }}>
          <Icon name="pie" size={42} stroke={1.3} className="dim" />
          <div className="stat-big" style={{ fontSize: 20, marginTop: 12 }}>Escolha uma unidade ou pasta</div>
          <div className="muted">A análise só lê o tamanho dos arquivos. Nada é alterado.</div>
        </Card>
      )}

      {!running && view && (
        <>
          <div className="row wrap" style={{ marginBottom: 12, gap: 4 }}>
            {crumbs.map((c, i) => (
              <span key={i} className="row" style={{ gap: 4 }}>
                {i > 0 && <Icon name="chevronRight" size={14} className="dim" />}
                <button className="btn ghost sm" onClick={() => open(crumbs.slice(1, i + 1).join('\\'))}>{i === 0 ? view.root : c}</button>
              </span>
            ))}
            <span className="spacer" />
            <span className="small muted">{bytes(view.size)} · {view.files.toLocaleString('pt-BR')} arquivos</span>
            <button className="btn sm" onClick={() => call('shell.reveal', { path: fullPath().replace(/\\$/, '') })}><Icon name="external" size={13} />Abrir</button>
          </div>
          <Card style={{ padding: 10 }}>
            <Treemap nodes={view.children} onOpen={n => open((view.path ? view.path + '\\' : '') + n.name)} />
          </Card>
          <div className="grid g2" style={{ marginTop: 16 }}>
            <Card className="pad-0">
              <div style={{ padding: '14px 16px 0' }}><CardTitle icon="folder" color="#a78bfa">Maiores pastas</CardTitle></div>
              <div className="list">
                {view.children.slice(0, 15).map((n, i) => (
                  <div key={n.name} className="li" style={{ cursor: 'pointer', padding: '9px 16px' }} onClick={() => open((view.path ? view.path + '\\' : '') + n.name)}>
                    <span className="dot" style={{ background: COLORS[i % COLORS.length] }} />
                    <div className="grow"><div className="ellipsis">{n.name}</div><div className="bar" style={{ marginTop: 5, height: 4 }}><i style={{ ["--v" as any]: n.size / (view.children[0]?.size || 1) }} /></div></div>
                    <b className="small" style={{ width: 74, textAlign: 'right' }}>{bytes(n.size)}</b>
                  </div>
                ))}
                {view.looseFiles > 0 && <div className="li small dim" style={{ padding: '9px 16px' }}><span style={{ flex: 1 }}>Arquivos soltos nesta pasta</span>{bytes(view.looseFiles)}</div>}
              </div>
            </Card>
            <Card className="pad-0">
              <div style={{ padding: '14px 16px 0' }}><CardTitle icon="list" color="#fbbf24">Maiores arquivos (&gt;100 MB)</CardTitle></div>
              <div className="list">
                {view.bigFiles.length === 0 && <div className="empty" style={{ padding: 20 }}>Nenhum arquivo acima de 100 MB.</div>}
                {view.bigFiles.map(f => (
                  <div key={f.path} className="li" style={{ padding: '9px 16px' }}>
                    <div className="grow ellipsis small" title={f.path}>{f.path}</div>
                    <b className="small">{bytes(f.size)}</b>
                    <button className="btn ghost sm iconbtn" title="Mostrar no Explorer" onClick={() => call('shell.reveal', { path: f.path })}><Icon name="external" size={13} /></button>
                  </div>
                ))}
              </div>
            </Card>
          </div>
        </>
      )}
    </div>
  );
}
