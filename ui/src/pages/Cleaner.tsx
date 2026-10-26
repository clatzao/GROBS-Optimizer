import { motion, AnimatePresence } from 'motion/react';
import { useEffect, useMemo, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import CountUp from '../components/rb/CountUp';
import DecryptedText from '../components/rb/DecryptedText';
import { AdminLock, Beam, Card, Check, Modal, PageHead, Spinner, Stagger } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, useStore } from '../store';

type Item = { id: string; name: string; group: string; desc: string; size: number; count: number; admin?: boolean; locked?: boolean; default: boolean; running?: boolean };

let cache: { items: Item[]; sel: string[] } | null = null;

const GROUP_ICON: Record<string, string> = { Windows: 'dashboard', Navegadores: 'globe', Aplicativos: 'package' };

export default function Cleaner() {
  const [items, setItems] = useState<Item[] | null>(cache?.items ?? null);
  const [sel, setSel] = useState<Set<string>>(new Set(cache?.sel ?? []));
  const [phase, setPhase] = useState<'idle' | 'scan' | 'clean' | 'done'>(cache ? 'idle' : 'idle');
  const [result, setResult] = useState<{ freed: number; files: number; skipped: number } | null>(null);
  const [confirm, setConfirm] = useState(false);
  const admin = useStore(appStore, a => a?.admin);

  useEffect(() => { if (items) cache = { items, sel: [...sel] }; }, [items, sel]);

  async function scan() {
    setPhase('scan'); setResult(null);
    const r = await run(call<Item[]>('clean.scan'));
    if (r) {
      setItems(r);
      setSel(new Set(r.filter(i => i.default && !i.locked && i.size > 0).map(i => i.id)));
    }
    setPhase('idle');
  }

  async function clean() {
    setConfirm(false); setPhase('clean');
    const r = await run(call<any>('clean.run', { ids: [...sel] }));
    if (r) { setResult(r); setPhase('done'); cache = null; }
    else setPhase('idle');
    const fresh = await call<Item[]>('clean.scan').catch(() => null);
    if (fresh) { setItems(fresh); setSel(new Set()); }
  }

  const groups = useMemo(() => {
    const g = new Map<string, Item[]>();
    (items ?? []).forEach(i => { if (!g.has(i.group)) g.set(i.group, []); g.get(i.group)!.push(i); });
    return [...g.entries()];
  }, [items]);

  const total = (items ?? []).filter(i => sel.has(i.id)).reduce((a, i) => a + i.size, 0);
  const totalAll = (items ?? []).filter(i => !i.locked).reduce((a, i) => a + i.size, 0);
  const runningSel = (items ?? []).filter(i => sel.has(i.id) && i.running);
  const toggle = (id: string, v: boolean) => setSel(s => { const n = new Set(s); v ? n.add(id) : n.delete(id); return n; });

  return (
    <div className="page">
      <PageHead title="Limpeza" sub="Remove só o que é seguro: temporários, caches e restos de atualizações. Nada de limpeza de registro."
        right={<>
          {items && <button className="btn" onClick={scan} disabled={phase !== 'idle' && phase !== 'done'}><Icon name="refresh" size={15} />Analisar de novo</button>}
          {items && <button className="btn primary" onClick={() => setConfirm(true)} disabled={sel.size === 0 || total === 0 || phase === 'scan' || phase === 'clean'}><Icon name="broom" size={15} />Limpar {bytes(total)}</button>}
        </>} />

      <Card style={{ padding: 24, marginBottom: 18, overflow: 'hidden' }}>
        {(phase === 'scan' || phase === 'clean') && <Beam size={180} duration={2.6} />}
        <div className="row" style={{ gap: 24 }}>
          <div style={{ width: 72, height: 72, borderRadius: 20, display: 'grid', placeItems: 'center', background: 'linear-gradient(135deg, rgba(139,92,246,.25), rgba(34,211,238,.15))', border: '1px solid rgba(139,92,246,.3)' }}>
            {phase === 'scan' || phase === 'clean' ? <div className="tri-loader" style={{ transform: 'scale(.7)' }} /> : phase === 'done' ? <Icon name="check" size={32} style={{ color: 'var(--ok)' }} /> : <Icon name="broom" size={30} style={{ color: '#c4b5fd' }} />}
          </div>
          <div style={{ flex: 1 }}>
            <AnimatePresence mode="wait">
              <motion.div key={phase} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -6 }} transition={{ duration: 0.25 }}>
                {phase === 'scan' && <><div className="stat-big" style={{ fontSize: 24 }}><DecryptedText text="Analisando arquivos…" animateOn="view" speed={40} sequential /></div><div className="muted">Verificando pastas temporárias, caches de navegadores e aplicativos.</div></>}
                {phase === 'clean' && <><div className="stat-big" style={{ fontSize: 24 }}>Limpando…</div><div className="muted">Arquivos em uso são ignorados automaticamente.</div></>}
                {phase === 'done' && result && <><div className="stat-big" style={{ fontSize: 30 }}><CountUp to={result.freed / 1e6} duration={1.4} separator="." trigger="load" /> MB liberados</div><div className="muted">{result.files.toLocaleString('pt-BR')} arquivos removidos{result.skipped ? ` · ${result.skipped.toLocaleString('pt-BR')} em uso foram mantidos` : ''}.</div></>}
                {phase === 'idle' && !items && <><div className="stat-big" style={{ fontSize: 24 }}>Pronto para analisar</div><div className="muted">A análise só lê os arquivos — nada é apagado até você confirmar.</div></>}
                {phase === 'idle' && items && <><div className="stat-big" style={{ fontSize: 30 }}>{bytes(totalAll)}</div><div className="muted">encontrados · {bytes(total)} selecionados para limpeza</div></>}
              </motion.div>
            </AnimatePresence>
          </div>
          {!items && phase === 'idle' && <button className="btn primary lg" onClick={scan}><Icon name="search" size={16} />Analisar agora</button>}
        </div>
      </Card>

      {runningSel.length > 0 && (
        <Card style={{ marginBottom: 16, borderColor: 'rgba(251,191,36,.3)', background: 'rgba(251,191,36,.05)' }}>
          <div className="row warn-t"><Icon name="alert" />Feche {runningSel.map(i => i.name).join(', ')} para limpar o cache por completo (arquivos em uso são pulados).</div>
        </Card>
      )}

      {items && (
        <Stagger className="grid g3" delay={0.06}>
          {groups.map(([g, list]) => {
            const gsel = list.filter(i => sel.has(i.id) && !i.locked);
            const all = list.filter(i => !i.locked && i.size > 0);
            return (
              <Card key={g} className="pad-0">
                <div className="row" style={{ padding: '14px 16px', borderBottom: '1px solid var(--line)' }}>
                  <Check on={all.length > 0 && gsel.length === all.length} onChange={v => setSel(s => { const n = new Set(s); all.forEach(i => (v ? n.add(i.id) : n.delete(i.id))); return n; })} disabled={all.length === 0} />
                  <Icon name={GROUP_ICON[g] ?? 'folder'} size={16} className="muted" />
                  <b>{g}</b><span className="spacer" />
                  <span className="muted small">{bytes(list.reduce((a, i) => a + i.size, 0))}</span>
                </div>
                <div className="list">
                  {list.map(i => (
                    <label key={i.id} className="li" style={{ padding: '10px 16px', cursor: i.locked ? 'default' : 'pointer', opacity: i.locked ? 0.6 : 1 }}>
                      <Check on={sel.has(i.id)} onChange={v => toggle(i.id, v)} disabled={i.locked || i.size === 0} />
                      <div className="grow">
                        <div className="t ellipsis" style={{ fontSize: 13 }}>{i.name}</div>
                        <div className="s ellipsis" title={i.desc}>{i.desc}</div>
                      </div>
                      {i.locked ? <AdminLock /> : <span className="small" style={{ fontWeight: 600, color: i.size > 0 ? 'var(--text)' : 'var(--text-3)' }}>{i.size > 0 ? bytes(i.size) : 'limpo'}</span>}
                    </label>
                  ))}
                </div>
              </Card>
            );
          })}
        </Stagger>
      )}
      {items && !admin && items.some(i => i.locked) && (
        <p className="small dim" style={{ marginTop: 14 }}><Icon name="lock" size={12} /> Alguns itens do Windows exigem administrador. Use “Executar como administrador” em Configurações para incluí-los.</p>
      )}

      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Confirmar limpeza</h3>
        <p className="muted" style={{ marginTop: 0 }}>{sel.size} categoria(s) · {bytes(total)}. Arquivos apagados não vão para a Lixeira{sel.has('recycle') ? ' e a Lixeira será esvaziada' : ''}.</p>
        <div className="row" style={{ marginTop: 18 }}><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn primary" onClick={clean}><Icon name="broom" size={15} />Limpar agora</button></div>
      </Modal>
    </div>
  );
}
