import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Beam, Card, CardTitle, Modal, PageHead, Spark, Spinner, Stagger, Tabs, Toggle } from '../components/ui';
import { appStore, run, toast, useStore } from '../store';
import { GameModeTab, SessionsTab } from './GamerExtra';

type Game = { name: string; store: string; exe: string; dir: string; appid?: string; gpuHigh: boolean; noFso: boolean; highPriority: boolean; autoTurbo: boolean; running: boolean };
type NvOpt = { key: string; title: string; desc: string; values: string[] };
type List = { games: Game[]; nvidia: boolean; admin: boolean; nvOptions: NvOpt[] };

const VALUE_LABEL: Record<string, string> = {
  default: 'Padrão do driver', max: 'Desempenho máximo', optimal: 'Otimizado', adaptive: 'Adaptável', on: 'Ligado', off: 'Desligado',
  highperf: 'Alto desempenho', perf: 'Desempenho', quality: 'Qualidade', highquality: 'Alta qualidade', app: 'Controlado pelo jogo',
};

const STORE_COLOR: Record<string, string> = { Steam: '#66c0f4', Epic: '#e5e5e5', Manual: '#a78bfa' };

function GameCard({ g, nvidia, nvOptions, admin, onChange }: { g: Game; nvidia: boolean; nvOptions: NvOpt[]; admin: boolean; onChange: (g: Game) => void }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [nv, setNv] = useState<Record<string, string> | null>(null);
  const [nvBusy, setNvBusy] = useState(false);
  const optimized = g.gpuHigh && g.noFso && g.autoTurbo && (g.highPriority || !admin);

  async function set(what: keyof Game, on: boolean) {
    setBusy(what);
    const r = await run(call<any>('games.set', { exe: g.exe, what, on }));
    if (r) onChange({ ...g, ...r, ...(what === 'autoTurbo' ? { autoTurbo: on } : {}) });
    setBusy(null);
  }
  async function optimizeAll() {
    setBusy('all');
    const r = await run(call<any>('games.optimize', { exe: g.exe }), `${g.name} otimizado`);
    if (r) onChange({ ...g, ...r, autoTurbo: true });
    setBusy(null);
    if (nvidia && open) loadNv();
  }
  async function loadNv() { const r = await call<Record<string, string>>('games.nvRead', { exe: g.exe }).catch(() => null); setNv(r); }
  async function applyNv(next: Record<string, string>) {
    setNv(next); setNvBusy(true);
    const r = await run(call<Record<string, string>>('games.nvApply', { exe: g.exe, choices: next }), 'Perfil NVIDIA salvo');
    if (r) setNv(r);
    setNvBusy(false);
  }
  useEffect(() => { if (open && nvidia && !nv) loadNv(); }, [open]);

  const row = (key: keyof Game, title: string, desc: string, needAdmin = false) => (
    <div className="li" style={{ padding: '10px 0' }}>
      <div className="grow"><div className="t" style={{ fontSize: 13 }}>{title}</div><div className="s">{desc}</div></div>
      {needAdmin && !admin && <AdminLock />}
      {busy === key ? <Spinner /> : <Toggle on={!!g[key]} onChange={v => set(key, v)} disabled={needAdmin && !admin} />}
    </div>
  );

  return (
    <Card style={{ padding: 0, overflow: 'hidden', borderColor: g.running ? 'rgba(52,211,153,.45)' : undefined }}>
      {g.running && <Beam size={140} duration={5} c1="#34d399" c2="#22d3ee" />}
      <div className="row" style={{ padding: 14, cursor: 'pointer' }} onClick={() => setOpen(o => !o)}>
        <div style={{ width: 44, height: 44, borderRadius: 12, display: 'grid', placeItems: 'center', flex: 'none', fontWeight: 800, fontSize: 17, color: '#fff',
          background: g.appid ? `url(https://cdn.cloudflare.steamstatic.com/steam/apps/${g.appid}/capsule_184x69.jpg) center/cover` : `hsl(${[...g.name].reduce((h, c) => h + c.charCodeAt(0), 0) % 360} 55% 38%)` }}>
          {!g.appid && g.name[0]?.toUpperCase()}
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="row" style={{ gap: 8 }}>
            <b className="ellipsis">{g.name}</b>
            {g.running && <span className="chip ok"><span className="dot ok" style={{ width: 6, height: 6 }} />rodando</span>}
          </div>
          <div className="small dim ellipsis"><span style={{ color: STORE_COLOR[g.store] }}>{g.store}</span> · {g.exe.split('\\').pop()}</div>
        </div>
        {optimized ? <span className="chip accent"><Icon name="check" size={11} />otimizado</span>
          : <button className="btn sm primary" onClick={e => { e.stopPropagation(); optimizeAll(); }} disabled={!!busy}>{busy === 'all' ? <Spinner size={12} /> : <Icon name="zap" size={13} />}Otimizar</button>}
        <Icon name="chevronRight" size={16} className="dim" style={{ transform: open ? 'rotate(90deg)' : undefined, transition: 'transform .25s' }} />
      </div>
      {open && (
        <div style={{ padding: '0 16px 14px', borderTop: '1px solid var(--line)' }}>
          {row('gpuHigh', 'Usar a GPU de alto desempenho', 'Força a placa de vídeo dedicada (essencial em notebooks com 2 GPUs).')}
          {row('noFso', 'Desativar “otimizações de tela inteira”', 'Tela cheia exclusiva de verdade: costuma reduzir input lag e stutter.')}
          {row('highPriority', 'Prioridade de CPU alta ao abrir', 'O Windows dá preferência ao jogo sobre programas em segundo plano.', true)}
          {row('autoTurbo', 'Modo Turbo automático', 'Liga o plano de máximo desempenho quando o jogo abre e restaura quando fecha.')}
          {nvidia && (
            <div style={{ marginTop: 10 }}>
              <div className="row" style={{ marginBottom: 8 }}><span className="chip" style={{ color: '#76b900', borderColor: '#76b90055' }}>NVIDIA</span><b style={{ fontSize: 13 }}>Configurações do driver para este jogo</b>{nvBusy && <Spinner size={12} />}</div>
              {!admin ? <div className="small dim"><Icon name="lock" size={11} /> Abra como administrador para editar o perfil do driver.</div> : !nv ? <Spinner /> : (
                <div className="grid g2" style={{ gap: 10 }}>
                  {nvOptions.map(o => (
                    <label key={o.key} className="small muted" title={o.desc}>{o.title}
                      <select value={nv[o.key] ?? 'default'} onChange={e => applyNv({ ...nv, [o.key]: e.target.value })} style={{ width: '100%', marginTop: 4 }} disabled={nvBusy}>
                        <option value="default">{VALUE_LABEL.default}</option>
                        {o.values.map(v => <option key={v} value={v}>{VALUE_LABEL[v] ?? (o.key === 'fpslimit' ? `${v} FPS` : v)}</option>)}
                      </select>
                    </label>
                  ))}
                </div>
              )}
            </div>
          )}
          <div className="row" style={{ marginTop: 10 }}>
            <button className="btn sm ghost" onClick={() => call('shell.reveal', { path: g.exe })}><Icon name="folder" size={13} />Pasta do jogo</button>
            {g.store === 'Manual' && <button className="btn sm ghost" onClick={() => call('games.remove', { exe: g.exe }).then(() => onChange({ ...g, name: '__removed__' }))}><Icon name="trash" size={13} />Remover da lista</button>}
          </div>
        </div>
      )}
    </Card>
  );
}

function GamesTab() {
  const [data, setData] = useState<List | null>(null);
  const [q, setQ] = useState('');
  const load = () => call<List>('games.list').then(setData).catch(() => setData({ games: [], nvidia: false, admin: false, nvOptions: [] }));
  useEffect(() => { load(); return on('gameStarted', (n: string) => toast(`${n} abriu — Modo Turbo ligado automaticamente`, 'info')); }, []);
  const [scanning, setScanning] = useState(false);
  async function scan() {
    setScanning(true);
    const r = await run(call<List & { scanned: number }>("games.scan"));
    setScanning(false);
    if (r) { setData(r); toast(r.games.length ? `${r.games.length} jogo(s) encontrado(s)` : "Nenhum jogo encontrado — adicione o .exe manualmente.", r.games.length ? "ok" : "info"); }
  }
  async function add() {
    const exe = await call<string>('shell.pickExe');
    if (!exe) return;
    const r = await run(call<List>('games.add', { exe }), 'Jogo adicionado');
    if (r) setData(r);
  }
  const games = useMemo(() => (data?.games ?? []).filter(g => !q || g.name.toLowerCase().includes(q.toLowerCase())), [data, q]);
  if (!data) return <div className="empty"><Spinner size={24} /><div className="small dim" style={{ marginTop: 8 }}>Procurando jogos da Steam e da Epic…</div></div>;
  return (
    <>
      <Card style={{ marginBottom: 14 }}>
        <div className="row" style={{ alignItems: 'flex-start' }}>
          <Icon name="info" size={18} style={{ color: '#a78bfa', marginTop: 2 }} />
          <div style={{ flex: 1 }} className="small muted">
            Otimizações por jogo que realmente mudam o desempenho: GPU certa, tela cheia exclusiva, prioridade, Turbo automático e{data.nvidia ? ' o perfil do driver NVIDIA (as mesmas opções do Painel de Controle NVIDIA).' : ' — com placa NVIDIA — o perfil do driver.'} Tudo reversível pelos mesmos botões.
          </div>
        </div>
      </Card>
      <div className="row" style={{ marginBottom: 12 }}>
        <div style={{ position: 'relative', width: 280 }}>
          <Icon name="search" size={15} style={{ position: 'absolute', left: 11, top: 10, color: 'var(--text-3)' }} />
          <input type="search" placeholder="Buscar jogo" value={q} onChange={e => setQ(e.target.value)} style={{ width: '100%', paddingLeft: 34 }} />
        </div>
        <span className="small dim">{data.games.length} jogo(s)</span><span className="spacer" />
        <button className="btn sm" onClick={add}><Icon name="folder" size={13} />Adicionar jogo (.exe)</button>
        <button className="btn sm primary" onClick={scan} disabled={scanning}>{scanning ? <Spinner size={12} /> : <Icon name="search" size={13} />}Procurar jogos</button>
      </div>
      {scanning && <Card style={{ marginBottom: 12, overflow: "hidden" }}><Beam size={160} duration={2.5} /><div className="row"><div className="tri-loader" style={{ transform: "scale(.5)" }} /><div><b>Procurando jogos…</b><div className="small dim">Steam, Epic, GOG, Ubisoft, EA, Riot, Blizzard, Xbox/Game Pass e pastas de jogos em todas as unidades.</div></div></div></Card>}
      {games.length === 0 ? <Card><div className="empty">Nenhum jogo encontrado ainda. Clique em “Procurar jogos” para uma busca completa, ou em “Adicionar jogo” para escolher o .exe.</div></Card> : (
        <Stagger className="grid" delay={0.03}>
          {games.map(g => (
            <GameCard key={g.exe} g={g} nvidia={data.nvidia} nvOptions={data.nvOptions} admin={data.admin}
              onChange={n => setData(d => d && ({ ...d, games: n.name === '__removed__' ? d.games.filter(x => x.exe !== g.exe) : d.games.map(x => (x.exe === g.exe ? n : x)) }))} />
          ))}
        </Stagger>
      )}
    </>
  );
}

type Net = { connected: boolean; admin: boolean; name?: string; desc?: string; wifi?: boolean; speed?: number; gateway?: string; dns?: string[]; dnsAuto?: boolean; nagle?: boolean; signal?: number; ssid?: string; band?: string; hogs?: string[] };
type PingR = { host: string; avg: number; min: number; max: number; jitter: number; loss: number; samples: number[] };
type Dns = { name: string; ip: string; ip2: string | null; median: number; fail: number; current: boolean };

const TARGETS = [
  { host: 'gateway', label: 'Seu roteador', hint: 'mede o Wi-Fi/cabo' },
  { host: '1.1.1.1', label: 'Cloudflare', hint: 'internet' },
  { host: '8.8.8.8', label: 'Google', hint: 'internet' },
];

function NetTab() {
  const [info, setInfo] = useState<Net | null>(null);
  const [pings, setPings] = useState<Record<string, PingR | 'busy'>>({});
  const [custom, setCustom] = useState('');
  const [dns, setDns] = useState<Dns[] | null>(null);
  const [dnsBusy, setDnsBusy] = useState(false);
  const [confirmNagle, setConfirmNagle] = useState(false);
  const admin = useStore(appStore, a => a?.admin);
  const load = () => call<Net>('net.info').then(setInfo).catch(() => {});
  useEffect(() => { load(); }, []);

  async function ping(host: string, key = host) {
    setPings(p => ({ ...p, [key]: 'busy' }));
    const r = await call<PingR>('net.ping', { host, count: 20 }).catch(() => null);
    setPings(p => ({ ...p, [key]: r ?? { host, avg: -1, min: -1, max: -1, jitter: 0, loss: 100, samples: [] } }));
  }
  async function pingAll() {
    for (const t of TARGETS) await ping(t.host === 'gateway' ? info?.gateway ?? '' : t.host, t.host);
  }
  async function bench() { setDnsBusy(true); const r = await run(call<Dns[]>('net.dnsBench')); if (r) setDns(r); setDnsBusy(false); }
  async function useDns(d: Dns | null) {
    const r = await run(call<Net>('net.setDns', { primary: d?.ip ?? '', secondary: d?.ip2 ?? '' }), d ? `DNS ${d.name} ativado` : 'DNS automático restaurado');
    if (r) setInfo(r);
  }
  async function nagle(off: boolean) {
    setConfirmNagle(false);
    const r = await run(call<Net>('net.nagle', { off }), off ? 'Nagle desativado — reinicie o PC para valer' : 'Nagle restaurado — reinicie o PC');
    if (r) setInfo(r);
  }

  const quality = (p: PingR) => p.avg < 0 ? 'bad' : p.loss > 1 || p.jitter > 15 || p.avg > 80 ? 'bad' : p.jitter > 6 || p.avg > 40 ? 'warn' : 'ok';
  const tips: string[] = [];
  if (info?.wifi) tips.push(`Você está no Wi-Fi${info.signal !== undefined ? ` (sinal ${info.signal}%)` : ''}. Cabo de rede é a melhoria de ping mais efetiva que existe: elimina jitter e perda.`);
  if (info?.wifi && info.band && /2[.,]4/.test(info.band)) tips.push('Sua rede está em 2,4 GHz. Se o roteador tiver 5 GHz, conecte nela: menos interferência.');
  if (info?.hogs?.length) tips.push(`Apps que podem estar usando a internet agora: ${info.hogs.join(', ')}. Feche ou pause downloads antes de jogar (o Modo Turbo pode fechá-los sozinho).`);
  const gw = pings['gateway']; if (gw && gw !== 'busy' && gw.avg > 5) tips.push(`O ping até o seu próprio roteador está em ${gw.avg} ms (o normal é 1–3 ms no cabo). O problema está na rede da sua casa, não no servidor do jogo.`);

  return (
    <>
      <Card style={{ marginBottom: 14 }}>
        <div className="row" style={{ alignItems: 'flex-start' }}>
          <Icon name="info" size={18} style={{ color: '#22d3ee', marginTop: 2 }} />
          <div className="small muted" style={{ flex: 1 }}>O ping depende principalmente da distância até o servidor e do seu provedor — nenhum app “diminui” isso. O que dá para melhorar: <b>jitter</b> (variação), <b>perda de pacotes</b>, a rede da sua casa e o DNS. É isso que esta aba mede e corrige.</div>
        </div>
      </Card>
      <div className="grid g2">
        <Card>
          <CardTitle icon="network" color="#22d3ee" right={<button className="btn sm primary" onClick={pingAll} disabled={!info?.connected || Object.values(pings).includes('busy')}><Icon name="play" size={13} />Testar</button>}>Latência, jitter e perda</CardTitle>
          {!info ? <Spinner /> : !info.connected ? <div className="small bad-t">Sem conexão com a internet.</div> : (
            <>
              <div className="small dim" style={{ marginBottom: 10 }}>{info.wifi ? <>Wi-Fi <b>{info.ssid}</b>{info.signal !== undefined && ` · sinal ${info.signal}%`}{info.band && ` · ${info.band}`}</> : <>Cabo · {info.desc}</>} · {info.speed} Mbps de link</div>
              {[...TARGETS.map(t => ({ ...t, key: t.host })), ...(custom && pings[custom] ? [{ host: custom, key: custom, label: custom, hint: 'personalizado' }] : [])].map(t => {
                const p = pings[t.key];
                return (
                  <div key={t.key} style={{ padding: '10px 0', borderBottom: '1px solid var(--line)' }}>
                    <div className="row">
                      <div style={{ flex: 1 }}><b style={{ fontSize: 13 }}>{t.label}</b> <span className="small dim">{t.hint}</span></div>
                      {p === 'busy' ? <Spinner size={14} /> : p ? (
                        <div className="row small" style={{ gap: 14 }}>
                          <span className={`${quality(p)}-t`}><b style={{ fontSize: 15 }}>{p.avg < 0 ? '—' : `${Math.round(p.avg)} ms`}</b></span>
                          <span className="dim">jitter {p.jitter} ms</span>
                          <span className={p.loss > 0 ? 'bad-t' : 'dim'}>perda {p.loss}%</span>
                        </div>
                      ) : <span className="small dim">—</span>}
                    </div>
                    {p && p !== 'busy' && p.samples.length > 1 && <div style={{ marginTop: 6 }}><Spark data={p.samples} height={28} color={quality(p) === 'ok' ? '#34d399' : quality(p) === 'warn' ? '#fbbf24' : '#f87171'} points={p.samples.length} /></div>}
                  </div>
                );
              })}
              <div className="row" style={{ marginTop: 10 }}>
                <input type="text" placeholder="IP ou endereço do servidor do jogo" value={custom} onChange={e => setCustom(e.target.value.trim())} style={{ flex: 1 }} />
                <button className="btn sm" disabled={!custom} onClick={() => ping(custom)}>Pingar</button>
              </div>
            </>
          )}
        </Card>
        <div className="grid" style={{ gap: 16, alignContent: 'start' }}>
          <Card>
            <CardTitle icon="sparkles" color="#a78bfa">Diagnóstico da sua rede</CardTitle>
            {tips.length === 0 ? <div className="small dim">Rode o teste de latência para receber recomendações.</div> : (
              <div className="list">{tips.map((t, i) => <div key={i} className="row small" style={{ alignItems: 'flex-start', padding: '6px 0' }}><Icon name="alert" size={14} className="warn-t" style={{ marginTop: 2 }} /><span>{t}</span></div>)}</div>
            )}
          </Card>
          <Card>
            <CardTitle icon="settings" color="#fbbf24">Algoritmo de Nagle</CardTitle>
            <div className="row">
              <div className="small muted" style={{ flex: 1 }}>Envia pacotes TCP na hora, sem agrupar. Ajuda jogos que usam TCP (MMOs como WoW, Tibia, Ragnarök). Jogos de tiro usam UDP e não mudam. Requer reiniciar.</div>
              {!admin && <AdminLock />}
              <Toggle on={!!info?.nagle} onChange={v => (v ? setConfirmNagle(true) : nagle(false))} disabled={!admin || !info?.connected} />
            </div>
          </Card>
        </div>
      </div>

      <Card style={{ marginTop: 16 }}>
        <CardTitle icon="globe" color="#34d399" right={<button className="btn sm primary" onClick={bench} disabled={dnsBusy}>{dnsBusy ? <Spinner size={12} /> : <Icon name="play" size={13} />}Testar DNS</button>}>DNS mais rápido para você</CardTitle>
        <div className="small muted" style={{ marginBottom: 10 }}>O DNS traduz endereços (login, matchmaking, lojas). Um DNS lento atrasa conexões — não o ping dentro da partida. Atual: <b>{info?.dnsAuto ? 'automático (do roteador)' : info?.dns?.join(', ')}</b></div>
        {dns && (
          <div className="list">
            {dns.map((d, i) => (
              <div key={d.name} className="li" style={{ padding: '9px 4px' }}>
                <span style={{ width: 20 }} className={i === 0 ? 'ok-t' : 'dim'}>{i + 1}º</span>
                <div className="grow"><b style={{ fontSize: 13 }}>{d.name}</b> <span className="small dim mono">{d.ip}</span>{d.current && <span className="chip" style={{ marginLeft: 6 }}>em uso</span>}</div>
                <span className={`small ${d.median < 0 ? 'bad-t' : i === 0 ? 'ok-t' : ''}`} style={{ width: 90, textAlign: 'right' }}>{d.median < 0 ? 'sem resposta' : `${d.median} ms`}{d.fail > 0 && d.median >= 0 ? ` · ${d.fail} falha(s)` : ''}</span>
                {!d.current && d.median >= 0 && !d.name.startsWith('Atual') && <button className="btn sm" disabled={!admin} onClick={() => useDns(d)}>Usar</button>}
              </div>
            ))}
          </div>
        )}
        {!info?.dnsAuto && <button className="btn sm ghost" style={{ marginTop: 8 }} disabled={!admin} onClick={() => useDns(null)}><Icon name="restore" size={13} />Voltar ao DNS automático</button>}
      </Card>

      <Modal open={confirmNagle} onClose={() => setConfirmNagle(false)}>
        <h3>Desativar o algoritmo de Nagle?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Afeta só a conexão ativa ({info?.name}). Útil em jogos TCP; pode aumentar um pouco o número de pacotes enviados. Reinicie o PC depois. Reversível aqui mesmo.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirmNagle(false)}>Cancelar</button><button className="btn primary" onClick={() => nagle(true)}>Desativar Nagle</button></div>
      </Modal>
    </>
  );
}

export default function Gamer() {
  const [tab, setTab] = useState<'games' | 'mode' | 'sessions' | 'net'>('games');
  return (
    <div className="page">
      <PageHead title="Gamer" sub="Otimização por jogo e rede para jogar com mais FPS e menos lag."
        right={<Tabs layoutId="gmtab" value={tab} onChange={setTab} items={[{ id: 'games', label: 'Meus jogos' }, { id: 'mode', label: 'Modo Jogo' }, { id: 'sessions', label: 'Sessões e gargalo' }, { id: 'net', label: 'Ping e rede' }]} />} />
      {tab === 'games' && <GamesTab />}
      {tab === 'mode' && <GameModeTab />}
      {tab === 'sessions' && <SessionsTab />}
      {tab === 'net' && <NetTab />}
    </div>
  );
}
