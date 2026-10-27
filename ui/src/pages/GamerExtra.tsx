import { useEffect, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Bar, Card, CardTitle, Spark, Spinner, Toggle } from '../components/ui';
import { appStore, run, toast, useStore } from '../store';

type GmCfg = { enabled: boolean; closeApps: boolean; apps: string[]; pauseServices: boolean; maxPower: boolean };
type GmSt = { config: GmCfg; active: boolean; paused: string[]; admin: boolean };

const SUGGEST = ['chrome', 'msedge', 'firefox', 'OneDrive', 'Teams', 'ms-teams', 'Spotify', 'Dropbox', 'GoogleDriveFS', 'EpicGamesLauncher', 'EADesktop', 'Battle.net', 'qbittorrent', 'Skype', 'WhatsApp'];

export function GameModeTab() {
  const [s, setS] = useState<GmSt | null>(null);
  const [add, setAdd] = useState('');
  useEffect(() => {
    call<GmSt>('gamemode.status').then(setS).catch(() => {});
    return on('gameMode', (x: GmSt) => setS(x));
  }, []);
  async function save(patch: Partial<GmCfg>) {
    if (!s) return;
    const config = { ...s.config, ...patch };
    setS({ ...s, config });
    const r = await run(call<GmSt>('gamemode.config', { config }));
    if (r) setS(r);
  }
  if (!s) return <div className="empty"><Spinner size={24} /></div>;
  const c = s.config;
  const addApp = (n: string) => { n = n.trim().replace(/\.exe$/i, ''); if (n && !c.apps.some(a => a.toLowerCase() === n.toLowerCase())) save({ apps: [...c.apps, n] }); setAdd(''); };
  const row = (title: string, desc: string, on: boolean, set: (v: boolean) => void, lock = false) => (
    <div className="li" style={{ alignItems: 'flex-start' }}>
      <div className="grow"><div className="t">{title}</div><div className="s">{desc}</div></div>
      {lock && <AdminLock />}
      <Toggle on={on} onChange={set} disabled={lock || !c.enabled} />
    </div>
  );
  return (
    <div className="grid g2">
      <Card className="pad-0">
        <div style={{ padding: '18px 18px 8px' }}>
          <div className="row">
            <span className="qa-ico" style={{ color: '#a78bfa', background: '#a78bfa1f' }}><Icon name="gamepad" size={20} /></span>
            <div style={{ flex: 1 }}><b style={{ fontSize: 16 }}>Modo Jogo automático</b><div className="small muted">Quando qualquer jogo da sua lista abrir, o GROBS prepara o PC; quando fechar, volta tudo.</div></div>
            {s.active && <span className="chip accent pulse">jogo em andamento</span>}
            <Toggle on={c.enabled} onChange={v => save({ enabled: v })} />
          </div>
        </div>
        <div className="list">
          {row('Plano de energia máximo', 'Ativa o Desempenho Máximo (ou Alto desempenho) e volta ao plano anterior depois.', c.maxPower, v => save({ maxPower: v }))}
          {row('Pausar serviços pesados', 'Windows Update, BITS, indexação da pesquisa e SysMain ficam parados durante o jogo e são religados ao sair.' + (s.paused.length ? ` Pausados agora: ${s.paused.join(', ')}.` : ''), c.pauseServices, v => save({ pauseServices: v }), !s.admin)}
          {row('Fechar apps escolhidos', 'Fecha os programas da lista ao lado quando o jogo abre (não reabre ao sair).', c.closeApps, v => save({ closeApps: v }))}
        </div>
        <div className="small dim" style={{ padding: '10px 18px 16px' }}><Icon name="info" size={12} /> A RAM também é liberada ao abrir o jogo se estiver ligado em <b>Memória e latência</b>. Os jogos vêm da aba <b>Meus jogos</b> (use "Procurar jogos").</div>
      </Card>
      <Card>
        <CardTitle icon="x" color="#f87171">Apps para fechar ao jogar</CardTitle>
        <div className="row">
          <input type="text" placeholder="nome do programa (ex.: chrome)" value={add} onChange={e => setAdd(e.target.value)} onKeyDown={e => e.key === 'Enter' && addApp(add)} style={{ flex: 1 }} disabled={!c.enabled} />
          <button className="btn sm" onClick={() => addApp(add)} disabled={!c.enabled || !add.trim()}><Icon name="check" size={13} />Adicionar</button>
        </div>
        <div className="row wrap" style={{ marginTop: 12, gap: 6 }}>
          {c.apps.length === 0 && <span className="small dim">Nenhum app na lista.</span>}
          {c.apps.map(a => <button key={a} className="chip accent" style={{ cursor: 'pointer' }} onClick={() => save({ apps: c.apps.filter(x => x !== a) })} title="Remover">{a}<Icon name="x" size={10} /></button>)}
        </div>
        <div className="small dim" style={{ margin: '14px 0 6px' }}>Sugestões</div>
        <div className="row wrap" style={{ gap: 6 }}>
          {SUGGEST.filter(x => !c.apps.some(a => a.toLowerCase() === x.toLowerCase())).map(x => <button key={x} className="chip" style={{ cursor: 'pointer' }} onClick={() => addApp(x)} disabled={!c.enabled}>+ {x}</button>)}
        </div>
        <p className="small dim" style={{ marginBottom: 0 }}>Discord e programas de voz ficam de fora de propósito. Downloads em andamento nesses apps são interrompidos.</p>
      </Card>
    </div>
  );
}

type Summary = { id: string; game: string; exe: string; date: string; duration: number; manual: boolean; avgFps: number; low1: number; low01: number; minFps: number; maxFps: number; frametime: number; ft99: number;
  cpu: number; core: number; gpu: number; cpuTempMax: number; gpuTempMax: number; gpuBound: number; cpuBound: number; limited: number; verdict: 'gpu' | 'cpu' | 'limit' | 'mixed' };
type Sec = { t: number; fps: number; ft: number; cpu: number; core: number; gpu: number; ct: number; gt: number };
type LogSt = { enabled: boolean; recording: boolean; game: string | null; admin: boolean; elapsed: number; duration: number; live: Sec | null };

const VERDICT = {
  gpu: { title: 'A placa de vídeo é o gargalo', color: '#f472b6', text: 'A GPU ficou acima de 90% a maior parte do tempo. Para ganhar FPS: baixe resolução ou qualidade gráfica (sombras, reflexos, antisserrilhado), ative DLSS/FSR/XeSS, ou faça um overclock leve na aba Overclock.' },
  cpu: { title: 'O processador é o gargalo', color: '#a78bfa', text: 'Um núcleo da CPU ficou no limite enquanto a GPU sobrava. Reduza opções que pesam na CPU (distância de visão, multidões, física, simulação), feche programas em segundo plano (Modo Jogo), e use o perfil Jogo / núcleos sempre ativos.' },
  limit: { title: 'FPS limitado (nem CPU nem GPU no máximo)', color: '#22d3ee', text: 'Nada estava no limite: o FPS provavelmente está travado por V-Sync, limitador de FPS do jogo/driver ou pelo próprio motor do jogo. Para mais FPS, desligue o V-Sync ou aumente o limite.' },
  mixed: { title: 'Equilibrado / variável', color: '#34d399', text: 'O limite mudou ao longo da partida (cenas pesadas na GPU, outras na CPU). O PC está bem equilibrado para este jogo.' },
};

function Chart({ data, keys }: { data: Sec[]; keys: { k: keyof Sec; color: string; label: string; max?: number }[] }) {
  return (
    <div style={{ position: 'relative' }}>
      {keys.map((k, i) => (
        <div key={k.k} style={{ position: i ? 'absolute' : 'relative', inset: i ? 0 : undefined }}>
          <Spark data={data.map(d => Math.max(0, d[k.k]))} points={Math.max(2, data.length)} height={90} color={k.color} fill={i === 0} max={k.max} />
        </div>
      ))}
      <div className="row small" style={{ gap: 14, marginTop: 6 }}>{keys.map(k => <span key={k.k} className="row" style={{ gap: 5 }}><i className="dot" style={{ background: k.color, boxShadow: 'none' }} />{k.label}</span>)}</div>
    </div>
  );
}

export function SessionsTab() {
  const admin = useStore(appStore, a => a?.admin);
  const [st, setSt] = useState<LogSt | null>(null);
  const [list, setList] = useState<Summary[] | null>(null);
  const [open, setOpen] = useState<{ summary: Summary; seconds: Sec[] } | null>(null);
  const [busy, setBusy] = useState(false);
  const load = () => call<Summary[]>('fpslog.list').then(setList).catch(() => setList([]));
  useEffect(() => {
    call<LogSt>('fpslog.status').then(setSt).catch(() => {});
    load();
    const a = on('fpsLog', (s: LogSt) => setSt(s));
    const b = on('sessionSaved', (s: Summary) => { toast(`Sessão de ${s.game} gravada: ${s.avgFps} FPS médio`); load(); });
    return () => { a(); b(); };
  }, []);
  useEffect(() => { if (!st?.recording) return; const t = setInterval(() => call<LogSt>('fpslog.status').then(setSt).catch(() => {}), 2000); return () => clearInterval(t); }, [st?.recording]);

  async function analyze() {
    setBusy(true);
    const r = await run(call<LogSt>('fpslog.analyze', { seconds: 60 }));
    if (r) setSt(r);
    setBusy(false);
  }
  async function view(id: string) { const r = await run(call<any>('fpslog.get', { id })); if (r) setOpen(r); }
  async function del(id: string) { const r = await run(call<Summary[]>('fpslog.delete', { id })); if (r) { setList(r); setOpen(null); } }

  if (!admin) return (
    <Card><div className="empty"><Icon name="lock" size={28} /><div style={{ margin: '10px 0 14px' }}>A gravação de FPS e a análise de gargalo usam o contador de quadros do Windows (ETW), que precisa do modo administrador.</div>
      <button className="btn primary" onClick={() => call('app.elevate')}><Icon name="shield" size={14} />Executar como admin</button></div></Card>
  );
  return (
    <>
      <div className="grid g2">
        <Card>
          <div className="row">
            <span className="qa-ico" style={{ color: '#34d399', background: '#34d3991f' }}><Icon name="activity" size={20} /></span>
            <div style={{ flex: 1 }}><b>Gravar sessões automaticamente</b><div className="small muted">Grava FPS, 1% low, frametime, CPU, GPU e temperaturas sempre que um jogo da sua lista abrir.</div></div>
            {st && <Toggle on={st.enabled} onChange={async v => { const r = await run(call<LogSt>('fpslog.enable', { on: v })); if (r) setSt(r); }} />}
          </div>
        </Card>
        <Card>
          <div className="row">
            <span className="qa-ico" style={{ color: '#f472b6', background: '#f472b61f' }}><Icon name="search" size={20} /></span>
            <div style={{ flex: 1 }}><b>Análise de gargalo agora</b><div className="small muted">Abra o jogo, comece a jogar, volte aqui (Alt+Tab) e clique: o GROBS grava 60 s do jogo em primeiro plano.</div></div>
            {st?.recording ? <button className="btn sm" onClick={() => call<LogSt>('fpslog.stop').then(setSt)}><Icon name="stop" size={13} />Parar</button>
              : <button className="btn sm primary" onClick={analyze} disabled={busy}>{busy ? <Spinner size={13} /> : <Icon name="play" size={13} />}Analisar 60 s</button>}
          </div>
        </Card>
      </div>
      {st?.recording && (
        <Card style={{ marginTop: 16, borderColor: 'rgba(52,211,153,.35)' }}>
          <div className="row"><span className="dot ok pulse" /><b>Gravando {st.game}</b><span className="small dim">{st.elapsed}s{st.duration ? ` / ${st.duration}s` : ''}</span><span className="spacer" />
            {st.live && <span className="small mono">{st.live.fps} FPS · {st.live.ft} ms · CPU {st.live.cpu}% (núcleo {st.live.core}%) · GPU {st.live.gpu}%</span>}</div>
          {st.duration > 0 && <div style={{ marginTop: 10 }}><Bar value={(st.elapsed / st.duration) * 100} /></div>}
        </Card>
      )}
      <h2 className="sec"><Icon name="clock" size={16} />Sessões gravadas</h2>
      {!list ? <div className="empty"><Spinner /></div> : list.length === 0 ? <Card><div className="empty">Nenhuma sessão ainda. Jogue algum jogo da lista (com o GROBS aberto ou na bandeja) ou use a análise acima.</div></Card> : (
        <Card className="pad-0">
          <div className="list">
            {list.map(s => (
              <button key={s.id} className="li" onClick={() => view(s.id)} style={{ border: 0, borderBottom: '1px solid var(--line)', background: 'transparent', textAlign: 'left', width: '100%' }}>
                <span className="qa-ico" style={{ width: 38, height: 38, color: VERDICT[s.verdict].color, background: `${VERDICT[s.verdict].color}1f` }}><Icon name="gamepad" size={18} /></span>
                <div className="grow"><div className="t">{s.game}{s.manual && <span className="chip" style={{ marginLeft: 8 }}>análise</span>}</div><div className="s">{s.date} · {Math.round(s.duration / 60)} min</div></div>
                <div style={{ textAlign: 'right' }}><b className="mono">{s.avgFps} FPS</b><div className="small dim">1% low {s.low1}</div></div>
                <span className="chip" style={{ color: VERDICT[s.verdict].color, minWidth: 120, justifyContent: 'center' }}>{{ gpu: 'gargalo: GPU', cpu: 'gargalo: CPU', limit: 'FPS limitado', mixed: 'equilibrado' }[s.verdict]}</span>
              </button>
            ))}
          </div>
        </Card>
      )}
      {open && (() => {
        const s = open.summary, v = VERDICT[s.verdict];
        return (
          <Card style={{ marginTop: 16 }}>
            <CardTitle icon="gamepad" color={v.color} right={<><button className="btn sm ghost" onClick={() => del(s.id)}><Icon name="trash" size={13} />Excluir</button><button className="btn sm ghost iconbtn" onClick={() => setOpen(null)}><Icon name="x" size={14} /></button></>}>{s.game} · {s.date}</CardTitle>
            <div className="grid g6" style={{ marginBottom: 16 }}>
              {[['FPS médio', s.avgFps], ['1% low', s.low1], ['0,1% low', s.low01], ['Frametime', `${s.frametime} ms`], ['Frametime 99%', `${s.ft99} ms`], ['Mín / máx', `${s.minFps} / ${s.maxFps}`]].map(([k, val]) => (
                <div key={k as string} style={{ padding: 12, borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)' }}><div className="small dim">{k}</div><div className="stat-big" style={{ fontSize: 22, marginTop: 4 }}>{val}</div></div>
              ))}
            </div>
            <div className="grid g2">
              <div>
                <div className="small dim" style={{ marginBottom: 6 }}>FPS por segundo</div>
                <Chart data={open.seconds} keys={[{ k: 'fps', color: '#34d399', label: 'FPS' }]} />
                <div className="small dim" style={{ margin: '14px 0 6px' }}>Uso (%)</div>
                <Chart data={open.seconds} keys={[{ k: 'gpu', color: '#f472b6', label: 'GPU', max: 100 }, { k: 'core', color: '#a78bfa', label: 'CPU (núcleo mais usado)', max: 100 }, { k: 'cpu', color: '#22d3ee', label: 'CPU total', max: 100 }]} />
              </div>
              <div>
                <div style={{ padding: 16, borderRadius: 14, background: `${v.color}14`, border: `1px solid ${v.color}55` }}>
                  <b style={{ color: v.color, fontSize: 15 }}>{v.title}</b>
                  <p className="small" style={{ margin: '8px 0 0' }}>{v.text}</p>
                </div>
                <div style={{ marginTop: 14 }}>
                  {[['GPU no limite', s.gpuBound, '#f472b6'], ['CPU no limite', s.cpuBound, '#a78bfa'], ['Limitado (V-Sync/limitador)', s.limited, '#22d3ee']].map(([k, val, col]) => (
                    <div key={k as string} style={{ marginBottom: 10 }}><div className="row small"><span>{k}</span><span className="spacer" /><b>{val}% do tempo</b></div>
                      <div className="bar"><i style={{ ['--v' as any]: (val as number) / 100, background: col as string }} /></div></div>
                  ))}
                </div>
                <dl className="kv" style={{ marginTop: 12 }}>
                  <dt>CPU média</dt><dd>{s.cpu}% (núcleo mais usado {s.core}%)</dd>
                  <dt>GPU média</dt><dd>{s.gpu}%</dd>
                  {s.cpuTempMax > 0 && <><dt>Temperatura máx. CPU</dt><dd>{s.cpuTempMax} °C</dd></>}
                  {s.gpuTempMax > 0 && <><dt>Temperatura máx. GPU</dt><dd>{s.gpuTempMax} °C</dd></>}
                </dl>
              </div>
            </div>
          </Card>
        );
      })()}
    </>
  );
}
