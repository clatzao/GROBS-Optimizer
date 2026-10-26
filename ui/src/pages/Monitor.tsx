import { useEffect, useMemo, useRef, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Bar, Card, CardTitle, Gauge, PageHead, Spark, Spinner, Stagger, Tabs } from '../components/ui';
import { bits, bytes, level, mhz, pct, rate, temp, uptime } from '../format';
import SensorsTab from './SensorsTab';
import { appStore, historyStore, run, statsStore, toast, useStore } from '../store';

function Overview() {
  const s = useStore(statsStore);
  const h = useStore(historyStore);
  const unit = useStore(appStore, a => a?.settings.tempUnit ?? 'C');
  if (!s) return <div className="empty"><Spinner size={24} /></div>;
  const big = (label: string, value: number, colors: [string, string], sub: string) => (
    <Card spotlight style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
      <Gauge value={value < 0 ? 0 : value} size={92} stroke={9} colors={colors}><b style={{ fontSize: 19 }}>{pct(value)}</b></Gauge>
      <div style={{ minWidth: 0 }}><div className="muted" style={{ fontWeight: 550 }}>{label}</div><div className="small dim" style={{ marginTop: 4 }}>{sub}</div></div>
    </Card>
  );
  return (
    <>
      <Stagger className="grid g4">
        {big('CPU', s.cpu, ['#8b5cf6', '#6366f1'], mhz(s.cpuMhz))}
        {big('Memória', s.mem, ['#22d3ee', '#0ea5e9'], `${bytes(s.memUsed)} / ${bytes(s.memTotal)}`)}
        {big('GPU', s.gpu, ['#f472b6', '#e879f9'], s.gpuMem > 0 ? `VRAM ${bytes(s.gpuMem)}` : 'motor 3D')}
        {big('Disco', s.disk, ['#fbbf24', '#f97316'], `tempo ativo`)}
      </Stagger>

      <div className="grid g2" style={{ marginTop: 16 }}>
        <Card>
          <CardTitle icon="cpu" color="#8b5cf6" right={<span className="small dim">últimos 60 s</span>}>Uso do processador</CardTitle>
          <Spark data={h.cpu} max={100} height={110} color="#8b5cf6" />
          <div className="small dim" style={{ margin: '14px 0 8px' }}>Por núcleo lógico</div>
          <div style={{ display: 'grid', gridTemplateColumns: `repeat(${Math.min(8, Math.max(1, s.cores.length))}, 1fr)`, gap: 6 }}>
            {s.cores.map((c, i) => (
              <div key={i} title={`Núcleo ${i}: ${pct(c)}`} style={{ height: 46, borderRadius: 8, background: 'rgba(255,255,255,.04)', position: 'relative', overflow: 'hidden' }}>
                <i style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: '100%', transformOrigin: 'bottom', transform: `scaleY(${c / 100})`, background: 'linear-gradient(0deg,#7c3aed,#22d3ee)', opacity: .85, transition: 'transform .6s var(--ease)' }} />
                <span className="small" style={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center', fontSize: 11 }}>{Math.round(c)}</span>
              </div>
            ))}
          </div>
        </Card>
        <Card>
          <CardTitle icon="memory" color="#22d3ee">Memória</CardTitle>
          <Spark data={h.mem} max={100} height={110} color="#22d3ee" />
          <dl className="kv" style={{ marginTop: 14 }}>
            <dt>Em uso</dt><dd>{bytes(s.memUsed)} ({pct(s.mem)})</dd>
            <dt>Disponível</dt><dd>{bytes(s.memTotal - s.memUsed)}</dd>
            <dt>Em cache</dt><dd>{bytes(s.cached ?? 0)}</dd>
            <dt>Confirmada</dt><dd>{bytes(s.commit ?? 0)} / {bytes(s.commitLimit ?? 0)}</dd>
          </dl>
        </Card>
        <Card>
          <CardTitle icon="wifi" color="#34d399">Rede</CardTitle>
          <div className="row" style={{ gap: 24, marginBottom: 10 }}>
            <div><div className="small dim"><Icon name="arrowDown" size={12} /> Download</div><div className="stat-big" style={{ fontSize: 22 }}>{bits(s.netDown)}</div></div>
            <div><div className="small dim"><Icon name="arrowUp" size={12} /> Upload</div><div className="stat-big" style={{ fontSize: 22 }}>{bits(s.netUp)}</div></div>
          </div>
          <Spark data={h.netDown} height={70} color="#34d399" />
          <div style={{ marginTop: -70, pointerEvents: 'none' }}><Spark data={h.netUp} height={70} color="#60a5fa" fill={false} max={Math.max(1, ...h.netDown, ...h.netUp) * 1.15} /></div>
        </Card>
        <Card>
          <CardTitle icon="thermo" color="#fb7185">Temperaturas e energia</CardTitle>
          <div className="grid g2">
            <div><div className="small dim">CPU {s.cpuTempSrc && `(${s.cpuTempSrc})`}</div><div className={`stat-big ${s.cpuTemp > 0 ? level(s.cpuTemp, 75, 90) + '-t' : ''}`} style={{ fontSize: 26 }}>{temp(s.cpuTemp, unit)}</div></div>
            <div><div className="small dim">GPU {s.gpuTempSrc && `(${s.gpuTempSrc})`}</div><div className={`stat-big ${s.gpuTemp > 0 ? level(s.gpuTemp, 80, 92) + '-t' : ''}`} style={{ fontSize: 26 }}>{temp(s.gpuTemp, unit)}</div></div>
          </div>
          {s.cpuTemp > 0 && <div style={{ marginTop: 10 }}><Spark data={h.cpuTemp} max={100} height={50} color="#fb7185" /></div>}
          {s.cpuTemp < 0 && <p className="small dim" style={{ marginBottom: 0 }}>Este computador não expõe a temperatura da CPU ao Windows sem driver. Para leitura precisa, abra o LibreHardwareMonitor — o GROBS lê os sensores dele automaticamente.</p>}
          <dl className="kv" style={{ marginTop: 12 }}>
            {s.gpuFan !== undefined && <><dt>Ventoinha GPU</dt><dd>{s.gpuFan}%</dd></>}
            {s.gpuPower !== undefined && <><dt>Consumo GPU</dt><dd>{s.gpuPower} W</dd></>}
            {s.battery !== undefined && <><dt>Bateria</dt><dd>{s.battery}%{s.charging ? ' · carregando' : ''}</dd></>}
            <dt>Ligado há</dt><dd>{uptime(s.uptime)}</dd>
          </dl>
        </Card>
      </div>
    </>
  );
}

function Health() {
  const [d, setD] = useState<any>(null);
  const [bat, setBat] = useState<any>(null);
  const admin = useStore(appStore, a => a?.admin);
  useEffect(() => { call('sys.disks').then(setD).catch(() => {}); call('sys.battery').then(setBat).catch(() => {}); }, []);
  if (!d) return <div className="empty"><Spinner size={24} /></div>;
  const label: Record<string, string> = { ok: 'Saudável', warn: 'Alerta', bad: 'Falha', unknown: 'Desconhecido' };
  return (
    <>
      {d.smartFailure === true && <Card style={{ borderColor: 'rgba(248,113,113,.4)', marginBottom: 16 }}><div className="row bad-t"><Icon name="alert" />O S.M.A.R.T. prevê falha em um disco. Faça backup agora.</div></Card>}
      <Stagger className="grid g2">
        {d.physical.map((p: any) => (
          <Card key={p.name} spotlight>
            <CardTitle icon="disk" color={p.health === 'ok' ? '#34d399' : p.health === 'warn' ? '#fbbf24' : p.health === 'bad' ? '#f87171' : '#a3a9bb'}
              right={<span className={`chip ${p.health === 'ok' ? 'ok' : p.health === 'warn' ? 'warn' : p.health === 'bad' ? 'bad' : ''}`}>{label[p.health]}</span>}>{p.name}</CardTitle>
            <dl className="kv">
              <dt>Tipo</dt><dd>{p.type}{p.bus ? ` · ${p.bus}` : ''}</dd>
              <dt>Capacidade</dt><dd>{bytes(p.size)}</dd>
              {p.temp > 0 && <><dt>Temperatura</dt><dd>{p.temp}°C{p.tempMax ? <span className="dim"> (máx. {p.tempMax}°C)</span> : null}</dd></>}
              {p.wear !== undefined && p.wear !== null && <><dt>Desgaste</dt><dd>{p.wear}%</dd></>}
              {p.hours > 0 && <><dt>Horas ligado</dt><dd>{p.hours.toLocaleString('pt-BR')} h</dd></>}
              {p.readErrors !== undefined && p.readErrors !== null && <><dt>Erros de leitura</dt><dd>{p.readErrors}</dd></>}
            </dl>
            {p.wear !== undefined && p.wear !== null && <div style={{ marginTop: 10 }}><Bar value={100 - p.wear} kind={p.wear > 80 ? 'bad' : p.wear > 50 ? 'warn' : 'ok'} /><div className="small dim" style={{ marginTop: 4 }}>Vida útil restante estimada: {100 - p.wear}%</div></div>}
          </Card>
        ))}
        {bat?.present && (
          <Card spotlight>
            <CardTitle icon="battery" color="#34d399">Bateria</CardTitle>
            <div className="row" style={{ gap: 18 }}>
              <Gauge value={bat.health} size={90} stroke={9} colors={['#34d399', '#22d3ee']}><b>{Math.round(bat.health)}%</b></Gauge>
              <dl className="kv" style={{ flex: 1 }}>
                <dt>Saúde</dt><dd>{bat.health}%</dd>
                <dt>Capacidade de projeto</dt><dd>{(bat.design / 1000).toFixed(1)} Wh</dd>
                <dt>Carga total atual</dt><dd>{(bat.full / 1000).toFixed(1)} Wh</dd>
                {bat.cycles > 0 && <><dt>Ciclos</dt><dd>{bat.cycles}</dd></>}
              </dl>
            </div>
          </Card>
        )}
      </Stagger>
      {!d.smartAvailable && !admin && <p className="small dim" style={{ marginTop: 14 }}><Icon name="lock" size={12} /> Temperatura, desgaste e horas de uso dos discos ficam disponíveis ao executar como administrador.</p>}
      <h2 className="sec"><Icon name="folder" size={16} />Volumes</h2>
      <Card className="pad-0">
        <div className="list">
          {d.volumes.map((v: any) => {
            const used = (1 - v.free / v.total) * 100;
            return (
              <div key={v.name} className="li">
                <b style={{ width: 34 }}>{v.name}</b>
                <div className="grow"><div className="row small" style={{ marginBottom: 6 }}><span>{v.label || 'Disco local'}</span><span className="dim">{v.fs}</span><span className="spacer" /><span className="muted">{bytes(v.total - v.free)} usados · {bytes(v.free)} livres</span></div><Bar value={used} kind={used > 90 ? 'bad' : used > 80 ? 'warn' : undefined} /></div>
              </div>
            );
          })}
        </div>
      </Card>
    </>
  );
}

type Proc = { pid: number; name: string; cpu: number; mem: number; threads: number; prio: number; path: string };

function ProcessList() {
  const [list, setList] = useState<Proc[] | null>(null);
  const [sort, setSort] = useState<{ key: keyof Proc; dir: 1 | -1 }>({ key: 'cpu', dir: -1 });
  const [q, setQ] = useState('');
  const [sel, setSel] = useState<Proc | null>(null);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => {
    let alive = true;
    const load = () => call<Proc[]>('proc.list').then(l => { if (alive) setList(l); }).catch(() => {});
    load();
    timer.current = window.setInterval(load, 2000);
    return () => { alive = false; clearInterval(timer.current); };
  }, []);

  const rows = useMemo(() => {
    if (!list) return [];
    const f = q.trim().toLowerCase();
    return list.filter(p => !f || p.name.toLowerCase().includes(f) || String(p.pid) === f)
      .sort((a, b) => { const x = a[sort.key], y = b[sort.key]; return (typeof x === 'string' ? x.localeCompare(y as string) : (x as number) - (y as number)) * sort.dir; })
      .slice(0, 250);
  }, [list, sort, q]);

  const th = (key: keyof Proc, label: string, num = false) => (
    <th className={num ? 'num' : ''} onClick={() => setSort(s => ({ key, dir: s.key === key ? (s.dir === 1 ? -1 : 1) : (num ? -1 : 1) }))}>
      {label}{sort.key === key && <Icon name={sort.dir === 1 ? 'arrowUp' : 'arrowDown'} size={11} style={{ marginLeft: 4, verticalAlign: -1 }} />}
    </th>
  );

  async function kill(p: Proc, tree: boolean) {
    const r = await run(call<any>('proc.kill', { pid: p.pid, tree }));
    if (r) toast(`${p.name} finalizado à força${r.killed > 1 ? ` (${r.killed} processos)` : ''}`);
    setSel(null); loadHung();
  }
  const [hung, setHung] = useState<{ pid: number; title: string; name: string }[]>([]);
  const loadHung = () => call<any[]>('proc.hung').then(setHung).catch(() => {});
  useEffect(() => { loadHung(); const t = window.setInterval(loadHung, 4000); return () => clearInterval(t); }, []);
  async function prio(p: Proc, level: string) { await run(call('proc.priority', { pid: p.pid, level }), 'Prioridade alterada'); }

  return (
    <>
      <div className="row" style={{ marginBottom: 12 }}>
        <div style={{ position: 'relative', flex: '0 1 320px' }}>
          <Icon name="search" size={15} style={{ position: 'absolute', left: 11, top: 10, color: 'var(--text-3)' }} />
          <input type="search" placeholder="Buscar processo ou PID" value={q} onChange={e => setQ(e.target.value)} style={{ width: '100%', paddingLeft: 34 }} />
        </div>
        <span className="small dim">{list ? `${list.length} processos` : ''}</span>
        <span className="spacer" />
        {sel && (
          <div className="row">
            <span className="small muted ellipsis" style={{ maxWidth: 220 }}>{sel.name} ({sel.pid})</span>
            <select defaultValue="" onChange={e => { if (e.target.value) prio(sel, e.target.value); e.target.value = ''; }}>
              <option value="" disabled>Prioridade…</option>
              <option value="high">Alta</option><option value="above">Acima do normal</option><option value="normal">Normal</option><option value="below">Abaixo do normal</option><option value="idle">Baixa</option>
            </select>
            {sel.path && <button className="btn sm" onClick={() => call('shell.reveal', { path: sel.path })}><Icon name="folder" size={14} />Local</button>}
            <button className="btn sm danger" onClick={() => kill(sel, false)} title="TerminateProcess: fecha na hora, mesmo travado"><Icon name="x" size={14} />Finalizar à força</button>
            <button className="btn sm danger" onClick={() => kill(sel, true)} title="Fecha o processo e todos os que ele abriu (taskkill /F /T)"><Icon name="trash" size={14} />Árvore inteira</button>
          </div>
        )}
      </div>
      {hung.length > 0 && (
        <Card style={{ marginBottom: 12, borderColor: 'rgba(248,113,113,.35)' }}>
          {hung.map(h => (
            <div key={h.pid} className="row" style={{ padding: '4px 0' }}>
              <Icon name="alert" size={16} className="bad-t" />
              <span style={{ flex: 1 }}><b>{h.name}</b> <span className="dim small">— “{h.title}” não está respondendo</span></span>
              <button className="btn sm danger" onClick={() => kill({ pid: h.pid, name: h.name } as Proc, true)}>Finalizar à força</button>
            </div>
          ))}
        </Card>
      )}
      <Card className="pad-0" style={{ overflow: 'hidden' }}>
        <div style={{ maxHeight: 'calc(100vh - 290px)', overflow: 'auto' }}>
          {!list ? <div className="empty"><Spinner /></div> : (
            <table className="table">
              <thead><tr>{th('name', 'Nome')}{th('pid', 'PID', true)}{th('cpu', 'CPU', true)}{th('mem', 'Memória', true)}{th('threads', 'Threads', true)}</tr></thead>
              <tbody>
                {rows.map(p => (
                  <tr key={p.pid} onClick={() => setSel(p)} style={{ cursor: 'pointer', background: sel?.pid === p.pid ? 'rgba(139,92,246,.12)' : undefined }}>
                    <td className="ellipsis" style={{ maxWidth: 340 }} title={p.path}>{p.name}</td>
                    <td className="num dim">{p.pid}</td>
                    <td className="num" style={{ color: p.cpu > 25 ? 'var(--warn)' : undefined }}>{p.cpu.toFixed(1).replace('.', ',')}%</td>
                    <td className="num">{bytes(p.mem)}</td>
                    <td className="num dim">{p.threads}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </Card>
    </>
  );
}

export default function MonitorPage() {
  const [tab, setTab] = useState<'overview' | 'proc' | 'health' | 'sensors'>('overview');
  return (
    <div className="page">
      <PageHead title="Monitor do sistema" sub="Uso, temperatura e saúde do hardware em tempo real."
        right={<Tabs layoutId="montab" value={tab} onChange={setTab} items={[{ id: 'overview', label: 'Visão geral' }, { id: 'proc', label: 'Processos' }, { id: 'sensors', label: 'Sensores' }, { id: 'health', label: 'Saúde do hardware' }]} />} />
      {tab === 'overview' && <Overview />}
      {tab === 'proc' && <ProcessList />}
      {tab === 'health' && <Health />}
      {tab === 'sensors' && <SensorsTab />}
    </div>
  );
}
