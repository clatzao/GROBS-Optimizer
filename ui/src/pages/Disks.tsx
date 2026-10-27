import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Bar, Card, CardTitle, PageHead, Spinner, Tabs, Toggle } from '../components/ui';
import { bytes, temp } from '../format';
import { appStore, run, setSetting, toast, useStore } from '../store';

type Attr = { id: string; name: string; value?: number; worst?: number; threshold?: number; raw: string | number; rawDec?: number; status: 'ok' | 'warn' | 'bad' };
type SDisk = {
  index: number; model: string; serial: string; firmware: string; size: number; bus: string; type: string; rpm: number;
  status?: 'ok' | 'warn' | 'bad' | 'unknown'; error?: string; health?: number; temp?: number; hours?: number; cycles?: number; written?: number; read?: number; attributes?: Attr[]; kind?: 'ata' | 'nvme';
};

const ST = { ok: { label: 'Bom', color: '#34d399', bg: 'rgba(52,211,153,.12)' }, warn: { label: 'Cuidado', color: '#fbbf24', bg: 'rgba(251,191,36,.12)' }, bad: { label: 'Ruim', color: '#f87171', bg: 'rgba(248,113,113,.14)' }, unknown: { label: 'Desconhecido', color: '#6b7287', bg: 'rgba(255,255,255,.05)' } };

function tb(n?: number) { return n === undefined || n < 0 ? '—' : n >= 1e12 ? `${(n / 1e12).toFixed(2).replace('.', ',')} TB` : `${(n / 1e9).toFixed(0)} GB`; }

function HealthTab() {
  const app = useStore(appStore);
  const unit = app?.settings.tempUnit ?? 'C';
  const [disks, setDisks] = useState<SDisk[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [sel, setSel] = useState(0);
  const [hex, setHex] = useState(false);
  const load = () => { setDisks(null); setErr(null); call<SDisk[]>('smart.read').then(setDisks).catch(e => { setErr(e.message); setDisks([]); }); };
  useEffect(() => { if (app?.admin) load(); }, [app?.admin]);

  if (!app?.admin) return (
    <Card><div className="empty"><Icon name="lock" size={28} /><div style={{ margin: '10px 0 14px' }}>A leitura completa do S.M.A.R.T. fala direto com o disco e precisa do modo administrador.</div>
      <button className="btn primary" onClick={() => call('app.elevate')}><Icon name="shield" size={14} />Executar como admin</button></div></Card>
  );
  if (!disks) return <div className="empty"><Spinner size={24} /><div className="small dim" style={{ marginTop: 8 }}>Lendo os discos…</div></div>;
  if (disks.length === 0) return <Card><div className="empty">{err ?? 'Nenhum disco encontrado.'}</div></Card>;
  const d = disks[Math.min(sel, disks.length - 1)];
  const st = ST[d.status ?? 'unknown'];
  const tlimit = d.type === 'HDD' ? 50 : 65;
  return (
    <>
      <div className="row wrap" style={{ marginBottom: 14 }}>
        {disks.map((x, i) => {
          const s = ST[x.status ?? 'unknown'];
          return (
            <button key={x.index} className={`btn ${i === sel ? '' : 'ghost'}`} onClick={() => setSel(i)} style={{ height: 'auto', padding: '8px 12px', flexDirection: 'column', alignItems: 'flex-start', gap: 2, borderColor: i === sel ? s.color : undefined }}>
              <span className="row" style={{ gap: 6 }}><span className="dot" style={{ background: s.color, boxShadow: `0 0 8px ${s.color}` }} /><b style={{ fontSize: 12.5 }}>{s.label}</b>{x.temp !== undefined && x.temp >= 0 && <span className="small dim">{temp(x.temp, unit)}</span>}</span>
              <span className="small muted ellipsis" style={{ maxWidth: 180 }}>{x.model}</span>
            </button>
          );
        })}
        <span className="spacer" />
        <label className="row small muted" style={{ gap: 8 }}>Alertas na bandeja <Toggle on={(app.settings as any).diskAlerts !== false} onChange={v => setSetting('diskAlerts' as any, v as any)} /></label>
        <button className="btn sm" onClick={load}><Icon name="refresh" size={13} />Atualizar</button>
      </div>
      <div className="grid" style={{ gridTemplateColumns: '260px 1fr' }}>
        <Card style={{ textAlign: 'center' }}>
          <div className="small dim">Estado de saúde</div>
          <div style={{ margin: '12px auto', padding: '18px 10px', borderRadius: 14, background: st.bg, border: `1px solid ${st.color}55` }}>
            <div style={{ font: '700 26px var(--font-display)', color: st.color }}>{st.label}</div>
            {d.health !== undefined && <div className="stat-big" style={{ color: st.color, fontSize: 34, marginTop: 4 }}>{d.health}%</div>}
          </div>
          <div className="small dim">Temperatura</div>
          <div style={{ margin: '8px auto 0', padding: '12px 10px', borderRadius: 14, background: 'var(--surface-2)', border: '1px solid var(--line)' }}>
            <div className={`stat-big ${d.temp !== undefined && d.temp >= tlimit ? 'warn-t' : ''}`} style={{ fontSize: 28 }}>{d.temp !== undefined && d.temp >= 0 ? temp(d.temp, unit) : '—'}</div>
          </div>
          {d.error && <p className="small warn-t" style={{ marginBottom: 0 }}>{d.error}</p>}
          {d.status === 'bad' && <p className="small bad-t" style={{ marginBottom: 0 }}>Faça backup dos seus arquivos agora e planeje a troca do disco.</p>}
          {d.status === 'warn' && <p className="small warn-t" style={{ marginBottom: 0 }}>O disco apresenta sinais de desgaste ou setores com problema. Mantenha backup em dia.</p>}
        </Card>
        <Card>
          <div style={{ font: '650 18px var(--font-display)', marginBottom: 10 }}>{d.model} <span className="muted" style={{ fontWeight: 400 }}>{bytes(d.size, 0)}</span></div>
          <div className="grid g2" style={{ gap: '6px 24px' }}>
            <dl className="kv" style={{ margin: 0 }}>
              <dt>Firmware</dt><dd className="mono">{d.firmware || '—'}</dd>
              <dt>Número de série</dt><dd className="mono selectable">{d.serial || '—'}</dd>
              <dt>Interface</dt><dd>{d.bus || '—'} · {d.type}{d.rpm > 0 && d.rpm < 100000 ? ` · ${d.rpm} RPM` : ''}</dd>
            </dl>
            <dl className="kv" style={{ margin: 0 }}>
              <dt>Horas ligado</dt><dd>{d.hours !== undefined && d.hours >= 0 ? `${Math.round(d.hours).toLocaleString('pt-BR')} h` : '—'}</dd>
              <dt>Ligações</dt><dd>{d.cycles !== undefined && d.cycles >= 0 ? Math.round(d.cycles).toLocaleString('pt-BR') : '—'}</dd>
              <dt>Total gravado</dt><dd>{tb(d.written)}{d.read !== undefined ? ` · lido ${tb(d.read)}` : ''}</dd>
            </dl>
          </div>
          {d.attributes && (
            <div style={{ marginTop: 14, border: '1px solid var(--line)', borderRadius: 12, maxHeight: 380, overflow: 'auto' }}>
              <table className="table">
                <thead><tr><th style={{ width: 24 }} /><th>ID</th><th>Atributo</th>{d.kind === 'ata' && <><th className="num">Atual</th><th className="num">Pior</th><th className="num">Limite</th></>}
                  <th className="num" style={{ cursor: d.kind === 'ata' ? 'pointer' : undefined }} onClick={() => setHex(h => !h)} title={d.kind === 'ata' ? 'Clique para alternar hexadecimal/decimal' : undefined}>{d.kind === 'ata' ? `Valor bruto (${hex ? 'hex' : 'dec'})` : 'Valor'}</th></tr></thead>
                <tbody>
                  {d.attributes.map(a => (
                    <tr key={a.id}>
                      <td><span className="dot" style={{ background: ST[a.status].color, boxShadow: 'none' }} /></td>
                      <td className="mono">{a.id}</td>
                      <td>{a.name}</td>
                      {d.kind === 'ata' && <><td className="num">{a.value}</td><td className="num">{a.worst}</td><td className="num">{a.threshold}</td></>}
                      <td className="num mono">{d.kind === 'ata' ? (hex ? a.raw : (a.rawDec ?? 0).toLocaleString('pt-BR')) : a.raw}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </div>
    </>
  );
}

type BTest = { id: string; read: number; write: number; readIops?: number; writeIops?: number; readLat?: number; writeLat?: number };
type BRes = { drive: string; size: number; passes: number; tests: BTest[]; date: string; cancelled?: boolean };
type BStat = { running: boolean; phase: string; result: BRes | null; error: string | null };

function SpeedTab() {
  const [vols, setVols] = useState<{ name: string; label: string; free: number; total: number }[]>([]);
  const [drive, setDrive] = useState('C:');
  const [size, setSize] = useState(1024);
  const [passes, setPasses] = useState(3);
  const [st, setSt] = useState<BStat>({ running: false, phase: '', result: null, error: null });
  const [unit, setUnit] = useState<'mb' | 'iops'>('mb');
  useEffect(() => {
    call<any>('sys.disks').then(d => setVols(d.volumes)).catch(() => {});
    call<BStat>('dbench.status').then(setSt).catch(() => {});
    return on('diskBench', (s: BStat) => { setSt(s); if (!s.running && s.error) toast(s.error, 'err'); });
  }, []);
  const res = st.result;
  async function start() {
    const r = await run(call<BStat>('dbench.start', { drive, size, passes, quick: false }));
    if (r) setSt(r);
  }
  const rows = ['SEQ1M Q8T1', 'SEQ1M Q1T1', 'RND4K Q32T1', 'RND4K Q1T1'];
  const cell = (t: BTest | undefined, w: boolean) => {
    const v = t ? (w ? t.write : t.read) : -1;
    const iops = t ? (w ? t.writeIops : t.readIops) : undefined;
    const lat = t ? (w ? t.writeLat : t.readLat) : undefined;
    const isRnd = !!t && t.id.startsWith('RND');
    return (
      <div style={{ padding: '14px 16px', borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)', textAlign: 'right', minHeight: 72 }}>
        <div className="stat-big" style={{ fontSize: 30, color: v < 0 ? 'var(--text-3)' : w ? '#f9a8d4' : '#67e8f9' }}>
          {v < 0 ? '0,00' : unit === 'iops' && isRnd && iops ? Math.round(iops).toLocaleString('pt-BR') : v.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
        </div>
        {v >= 0 && isRnd && <div className="small dim">{unit === 'iops' ? 'IOPS' : `${Math.round(iops ?? 0).toLocaleString('pt-BR')} IOPS`} · {lat?.toLocaleString('pt-BR')} µs</div>}
      </div>
    );
  };
  return (
    <Card>
      <div className="row wrap" style={{ marginBottom: 16 }}>
        <button className="btn primary lg" onClick={start} disabled={st.running}>{st.running ? <Spinner size={16} /> : <Icon name="play" size={16} />}Testar tudo</button>
        {st.running && <button className="btn" onClick={() => call('dbench.cancel')}><Icon name="stop" size={14} />Parar</button>}
        <select value={passes} onChange={e => setPasses(Number(e.target.value))} disabled={st.running} title="Repetições (fica a melhor)">{[1, 2, 3, 5, 9].map(n => <option key={n} value={n}>{n}×</option>)}</select>
        <select value={size} onChange={e => setSize(Number(e.target.value))} disabled={st.running} title="Tamanho do arquivo de teste">{[256, 512, 1024, 4096, 8192].map(n => <option key={n} value={n}>{n >= 1024 ? `${n / 1024} GiB` : `${n} MiB`}</option>)}</select>
        <select value={drive} onChange={e => setDrive(e.target.value)} disabled={st.running}>{vols.map(v => <option key={v.name} value={v.name}>{v.name} {v.label} — {Math.round((v.total - v.free) / v.total * 100)}% ({bytes(v.total, 0)})</option>)}</select>
        <span className="spacer" />
        <div className="tabs">{(['mb', 'iops'] as const).map(u => <button key={u} className={`tab ${unit === u ? 'active' : ''}`} onClick={() => setUnit(u)}>{unit === u && <i className="tabbg" />}<span>{u === 'mb' ? 'MB/s' : 'IOPS'}</span></button>)}</div>
      </div>
      <div className="grid" style={{ gridTemplateColumns: '150px 1fr 1fr', gap: 10, alignItems: 'stretch' }}>
        <div /><div className="small dim" style={{ textAlign: 'right', paddingRight: 16 }}>Leitura ({unit === 'mb' ? 'MB/s' : 'IOPS'})</div><div className="small dim" style={{ textAlign: 'right', paddingRight: 16 }}>Gravação ({unit === 'mb' ? 'MB/s' : 'IOPS'})</div>
        {rows.map(id => {
          const t = res?.tests.find(x => x.id === id);
          return [
            <div key={id + 'l'} style={{ display: 'grid', placeItems: 'center', borderRadius: 12, background: 'linear-gradient(135deg, rgba(139,92,246,.25), rgba(34,211,238,.12))', border: '1px solid rgba(139,92,246,.3)', fontWeight: 650, textAlign: 'center', lineHeight: 1.2 }}>
              {id.split(' ')[0]}<br /><span className="small muted">{id.split(' ')[1]}</span></div>,
            <div key={id + 'r'}>{cell(t, false)}</div>, <div key={id + 'w'}>{cell(t, true)}</div>,
          ];
        })}
      </div>
      <div className="small muted" style={{ marginTop: 14, minHeight: 18 }}>
        {st.running ? <><Spinner size={12} /> {st.phase}</> : res ? `${res.drive} · ${res.size >= 1024 ? res.size / 1024 + ' GiB' : res.size + ' MiB'} · ${res.passes}× · ${res.date}${res.cancelled ? ' · interrompido' : ''}` : 'Mesmo método do CrystalDiskMark: sem cache do Windows, com fila real de requisições. Um arquivo temporário é criado e apagado no fim.'}
      </div>
    </Card>
  );
}

type Vol = { name: string; label: string; fs: string; media: string; total: number; free: number; aligned: boolean | null };
type DOpt = { volumes: Vol[]; trim: boolean; running: string | null; log: string; admin: boolean; hibernation: boolean; indexing: boolean };

function OptimizeTab() {
  const [s, setS] = useState<DOpt | null>(null);
  const [log, setLog] = useState<string[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const load = () => call<DOpt>('drives.status').then(x => { setS(x); if (x.log) setLog(x.log.split('\n').filter(Boolean)); }).catch(() => {});
  useEffect(() => {
    load();
    return on('driveOpt', (e: any) => { if (e.line) setLog(l => [...l.slice(-200), e.line]); if (e.done) { toast(`Otimização de ${e.drive} concluída`); load(); } });
  }, []);
  async function act(id: string, m: string, p: any, ok: string) {
    setBusy(id);
    const r = await run(call<DOpt>(m, p), ok);
    if (r) setS(r);
    setBusy(null);
  }
  if (!s) return <div className="empty"><Spinner size={24} /></div>;
  return (
    <>
      <Card className="pad-0">
        <div style={{ padding: '16px 18px 4px' }}><CardTitle icon="disk" color="#fbbf24" right={!s.admin && <AdminLock />}>Unidades</CardTitle></div>
        <div className="list">
          {s.volumes.map(v => (
            <div key={v.name} className="li">
              <Icon name="disk" size={20} className="muted" />
              <div className="grow">
                <div className="row" style={{ gap: 8 }}><span className="t">{v.name} {v.label}</span>{v.media && <span className="chip">{v.media}</span>}<span className="chip">{v.fs}</span>
                  {v.aligned === false && <span className="chip warn" title="Partição não alinhada a 4 KiB: perde velocidade em SSD">desalinhada</span>}
                  {v.aligned === true && v.media === 'SSD' && <span className="chip ok">alinhada 4K</span>}</div>
                <div style={{ marginTop: 6, maxWidth: 420 }}><Bar value={(1 - v.free / v.total) * 100} kind={v.free / v.total < 0.1 ? 'bad' : v.free / v.total < 0.2 ? 'warn' : undefined} /></div>
                <div className="s" style={{ marginTop: 3 }}>{bytes(v.free)} livres de {bytes(v.total)}</div>
              </div>
              <button className="btn sm" disabled={!s.admin || !!s.running} onClick={() => act('opt' + v.name, 'drives.optimize', { drive: v.name }, `Otimizando ${v.name}…`)}>
                {s.running === v.name ? <Spinner size={13} /> : <Icon name="zap" size={14} />}{v.media === 'SSD' ? 'TRIM agora' : v.media === 'HDD' ? 'Desfragmentar' : 'Otimizar'}</button>
            </div>
          ))}
        </div>
        {log.length > 0 && <pre className="mono small selectable" style={{ margin: 0, padding: '12px 18px', maxHeight: 200, overflow: 'auto', borderTop: '1px solid var(--line)', color: 'var(--text-2)', whiteSpace: 'pre-wrap' }}>{log.join('\n')}</pre>}
      </Card>
      <div className="grid g3" style={{ marginTop: 16 }}>
        <Card>
          <div className="row"><div style={{ flex: 1 }}><b>TRIM</b><div className="small muted">Avisa o SSD sobre blocos apagados — mantém a velocidade de gravação. Deve ficar ligado.</div></div>
            {busy === 'trim' ? <Spinner /> : <Toggle on={s.trim} disabled={!s.admin} onChange={v => act('trim', 'drives.trim', { on: v }, v ? 'TRIM ligado' : 'TRIM desligado')} />}</div>
        </Card>
        <Card>
          <div className="row"><div style={{ flex: 1 }}><b>Indexação da pesquisa</b><div className="small muted">Desligar economiza disco e RAM em PCs com HD; a busca por arquivos no Iniciar fica mais lenta.</div></div>
            {busy === 'idx' ? <Spinner /> : <Toggle on={s.indexing} disabled={!s.admin} onChange={v => act('idx', 'drives.indexing', { on: v }, v ? 'Indexação ligada' : 'Indexação desligada')} />}</div>
        </Card>
        <Card>
          <div className="row"><div style={{ flex: 1 }}><b>Hibernação</b><div className="small muted">Desligar apaga o hiberfil.sys (vários GB no C:) e a Inicialização Rápida.</div></div>
            {busy === 'hib' ? <Spinner /> : <Toggle on={s.hibernation} disabled={!s.admin} onChange={async v => { setBusy('hib'); await run(call('power.setHibernation', { on: v }), v ? 'Hibernação ligada' : 'Hibernação desligada'); load(); setBusy(null); }} />}</div>
        </Card>
      </div>
    </>
  );
}

export default function Disks() {
  const [tab, setTab] = useState<'health' | 'speed' | 'opt'>('health');
  const sub = useMemo(() => ({ health: 'Saúde S.M.A.R.T. completa, como no CrystalDiskInfo.', speed: 'Velocidade real de leitura e gravação, como no CrystalDiskMark.', opt: 'TRIM, desfragmentação, alinhamento e indexação.' }), []);
  return (
    <div className="page">
      <PageHead title="Discos" sub={sub[tab]}
        right={<Tabs layoutId="dktab" value={tab} onChange={setTab} items={[{ id: 'health', label: 'Saúde' }, { id: 'speed', label: 'Velocidade' }, { id: 'opt', label: 'Otimizar' }]} />} />
      {tab === 'health' && <HealthTab />}
      {tab === 'speed' && <SpeedTab />}
      {tab === 'opt' && <OptimizeTab />}
    </div>
  );
}
