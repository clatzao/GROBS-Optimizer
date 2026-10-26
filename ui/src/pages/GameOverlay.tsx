import { useEffect, useRef, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Card, CardTitle, Check, PageHead, Spinner, Tabs, Toggle } from '../components/ui';
import { appStore, run, useStore } from '../store';

type Cfg = { enabled: boolean; position: string; scale: number; opacity: number; metrics: string[] };
type App = { pid: number; name: string; fps: number; low1: number; frametime: number };
type St = { on: boolean; running: boolean; error: string | null; apps: App[]; config: Cfg; admin: boolean };

const METRICS: { id: string; label: string; demo: string }[] = [
  { id: 'fps', label: 'FPS', demo: '144' },
  { id: 'low1', label: '1% low', demo: '98' },
  { id: 'frametime', label: 'Frametime', demo: '6.9 ms' },
  { id: 'cpu', label: 'Uso da CPU', demo: '34%' },
  { id: 'cpuTemp', label: 'Temperatura da CPU', demo: '61°C' },
  { id: 'gpu', label: 'Uso da GPU', demo: '97%' },
  { id: 'gpuTemp', label: 'Temperatura da GPU', demo: '70°C' },
  { id: 'vram', label: 'VRAM usada', demo: '5.2 GB' },
  { id: 'ram', label: 'RAM usada', demo: '9.8 GB' },
  { id: 'clock', label: 'Relógio', demo: '21:34' },
];
const SHORT: Record<string, string> = { fps: 'FPS', low1: '1% LOW', frametime: 'FRAME', cpu: 'CPU', cpuTemp: 'CPU °', gpu: 'GPU', gpuTemp: 'GPU °', vram: 'VRAM', ram: 'RAM', clock: 'HORA' };

export default function GameOverlay() {
  const [st, setSt] = useState<St | null>(null);
  const [cfg, setCfg] = useState<Cfg | null>(null);
  const admin = useStore(appStore, a => a?.admin);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => {
    const load = () => call<St>('overlay.status').then(s => { setSt(s); setCfg(c => c ?? s.config); }).catch(() => {});
    load();
    timer.current = window.setInterval(load, 1000);
    return () => clearInterval(timer.current);
  }, []);

  async function save(next: Cfg) {
    setCfg(next);
    await run(call('overlay.config', { config: next }));
  }

  if (!st || !cfg) return <div className="page"><div className="empty"><Spinner size={24} /></div></div>;
  const has = (m: string) => cfg.metrics.includes(m);
  const toggleMetric = (m: string, v: boolean) => save({ ...cfg, metrics: v ? METRICS.map(x => x.id).filter(id => id === m || cfg.metrics.includes(id)) : cfg.metrics.filter(x => x !== m) });

  return (
    <div className="page">
      <PageHead title="Overlay de jogo" sub="FPS, frametime e sensores por cima de jogos e programas — como o MSI Afterburner/RTSS, só que leve."
        right={<div className="row"><span className="muted small">Overlay</span><Toggle on={cfg.enabled} onChange={v => save({ ...cfg, enabled: v })} /></div>} />

      {!admin && (
        <Card style={{ marginBottom: 16, borderColor: 'rgba(251,191,36,.3)', background: 'rgba(251,191,36,.05)' }}>
          <div className="row warn-t"><Icon name="lock" />O contador de FPS lê eventos do Windows (ETW) e precisa do modo administrador. Os sensores funcionam sem ele.</div>
        </Card>
      )}
      {st.error && admin && <Card style={{ marginBottom: 16 }}><div className="row bad-t"><Icon name="alert" />{st.error}</div></Card>}

      <div className="grid" style={{ gridTemplateColumns: '1fr 1.1fr', gap: 16 }}>
        <Card>
          <CardTitle icon="eye" color="#a78bfa">Prévia</CardTitle>
          <div style={{ position: 'relative', height: 260, borderRadius: 12, overflow: 'hidden', background: 'linear-gradient(135deg,#1e293b,#0f172a 40%,#312e81)', border: '1px solid var(--line)' }}>
            <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(circle at 70% 60%, rgba(34,211,238,.25), transparent 50%)' }} />
            <div style={{
              position: 'absolute', [cfg.position.startsWith('b') ? 'bottom' : 'top']: 12, [cfg.position.endsWith('r') ? 'right' : 'left']: 12,
              background: `rgba(10,11,16,${cfg.opacity})`, border: '1px solid rgba(139,92,246,.8)', borderRadius: 8, padding: '7px 9px', transform: `scale(${cfg.scale})`,
              transformOrigin: `${cfg.position.startsWith('b') ? 'bottom' : 'top'} ${cfg.position.endsWith('r') ? 'right' : 'left'}`,
            }}>
              {METRICS.filter(m => has(m.id)).map(m => (
                <div key={m.id} className="row" style={{ gap: 10, lineHeight: '17px' }}>
                  <span style={{ width: 50, fontSize: 10.5, fontWeight: 700, color: '#a3a9bb' }}>{SHORT[m.id]}</span>
                  <span className="mono" style={{ fontSize: 13, fontWeight: 700, color: m.id === 'fps' ? '#34d399' : '#fff' }}>{m.demo}</span>
                </div>
              ))}
            </div>
          </div>
          <div className="grid g2" style={{ marginTop: 14, gap: 10 }}>
            <div>
              <div className="small dim" style={{ marginBottom: 6 }}>Posição</div>
              <Tabs layoutId="ovpos" value={cfg.position} onChange={v => save({ ...cfg, position: v })} items={[{ id: 'tl', label: '↖' }, { id: 'tr', label: '↗' }, { id: 'bl', label: '↙' }, { id: 'br', label: '↘' }]} />
            </div>
            <div>
              <div className="small dim" style={{ marginBottom: 6 }}>Tamanho</div>
              <Tabs layoutId="ovsize" value={String(cfg.scale)} onChange={v => save({ ...cfg, scale: Number(v) })} items={[{ id: '0.85', label: 'P' }, { id: '1', label: 'M' }, { id: '1.25', label: 'G' }, { id: '1.5', label: 'GG' }]} />
            </div>
          </div>
          <div style={{ marginTop: 12 }}>
            <div className="small dim" style={{ marginBottom: 6 }}>Opacidade do fundo: {Math.round(cfg.opacity * 100)}%</div>
            <input type="range" min={20} max={100} value={Math.round(cfg.opacity * 100)} onChange={e => setCfg({ ...cfg, opacity: +e.target.value / 100 })} onMouseUp={() => save(cfg)} style={{ width: '100%', accentColor: '#8b5cf6' }} />
          </div>
          <p className="small dim" style={{ marginBottom: 0 }}><Icon name="info" size={12} /> Atalho <b>Ctrl+Shift+F10</b> mostra/oculta durante o jogo. Funciona em modo janela e tela cheia sem borda (o padrão da maioria dos jogos atuais).</p>
        </Card>

        <div className="grid" style={{ gap: 16, alignContent: 'start' }}>
          <Card className="pad-0">
            <div style={{ padding: '14px 16px 4px' }}><CardTitle icon="list" color="#22d3ee">O que mostrar</CardTitle></div>
            <div className="grid g2" style={{ gap: 0 }}>
              {METRICS.map(m => (
                <label key={m.id} className="li" style={{ padding: '10px 16px', cursor: 'pointer' }}>
                  <Check on={has(m.id)} onChange={v => toggleMetric(m.id, v)} />
                  <span className="grow">{m.label}</span>
                  {['fps', 'low1', 'frametime'].includes(m.id) && !admin && <AdminLock />}
                </label>
              ))}
            </div>
          </Card>
          <Card>
            <CardTitle icon="gamepad" color="#34d399" right={st.running ? <span className="chip ok"><span className="dot ok" style={{ width: 6, height: 6 }} />contando</span> : <span className="chip">parado</span>}>Jogos e apps detectados</CardTitle>
            {!st.running ? <div className="small dim">Ative o overlay {admin ? '' : '(em modo administrador) '}para começar a medir o FPS.</div>
              : st.apps.length === 0 ? <div className="small dim">Abra um jogo — ele aparece aqui assim que desenhar quadros (DirectX 9–12; Vulkan/OpenGL em melhor esforço).</div>
                : (
                  <div className="list">
                    {st.apps.sort((a, b) => b.fps - a.fps).map(a => (
                      <div key={a.pid} className="row" style={{ padding: '7px 0', borderBottom: '1px solid var(--line)' }}>
                        <b className="ellipsis" style={{ flex: 1 }}>{a.name}</b>
                        <span className="mono ok-t" style={{ width: 70, textAlign: 'right' }}>{a.fps} FPS</span>
                        <span className="mono dim small" style={{ width: 80, textAlign: 'right' }}>1% {a.low1}</span>
                        <span className="mono dim small" style={{ width: 70, textAlign: 'right' }}>{a.frametime} ms</span>
                      </div>
                    ))}
                  </div>
                )}
          </Card>
        </div>
      </div>
    </div>
  );
}
