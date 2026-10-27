import { useEffect, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import TweakList from '../components/TweakList';
import { AdminLock, Bar, Card, CardTitle, Gauge, PageHead, Spinner, Tabs, Toggle } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, toast, useStore } from '../store';

type RamCfg = { enabled: boolean; threshold: number; onGame: boolean; workingSets: boolean };
type Ram = { total: number; available: number; load: number; admin: boolean; standby?: number; modified?: number; free?: number; config: RamCfg; last: string; freed?: number };

function MemoryTab() {
  const admin = useStore(appStore, a => a?.admin);
  const [ram, setRam] = useState<Ram | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const load = () => call<Ram>('ram.status').then(setRam).catch(() => {});
  useEffect(() => { load(); const t = setInterval(load, 3000); const off = on('ramCleaned', (r: Ram) => setRam(r)); return () => { clearInterval(t); off(); }; }, []);

  async function clean(ws: boolean) {
    setBusy(ws ? 'ws' : 'sb');
    const r = await run(call<Ram>('ram.clean', { workingSets: ws }));
    setBusy(null);
    if (r) { setRam(r); toast(`${bytes(r.freed ?? 0)} liberados`); }
  }
  async function cfg(patch: Partial<RamCfg>) {
    if (!ram) return;
    const config = { ...ram.config, ...patch };
    setRam({ ...ram, config });
    await run(call('ram.config', { config }));
  }

  if (!ram) return <div className="empty"><Spinner size={24} /></div>;
  const used = ram.total - ram.available;
  return (
    <>
      <div className="grid g2">
        <Card spotlight>
          <CardTitle icon="memory" color="#a78bfa" right={!admin && <AdminLock />}>Liberador de RAM</CardTitle>
          <div className="row" style={{ gap: 22, alignItems: 'center' }}>
            <Gauge value={ram.load} size={130} stroke={11} colors={ram.load > 85 ? ['#f87171', '#fbbf24'] : ['#8b5cf6', '#22d3ee']}>
              <div><div className="stat-big" style={{ fontSize: 28 }}>{ram.load}%</div><div className="small dim">em uso</div></div>
            </Gauge>
            <dl className="kv" style={{ flex: 1 }}>
              <dt>Em uso</dt><dd>{bytes(used)} de {bytes(ram.total)}</dd>
              <dt>Disponível</dt><dd>{bytes(ram.available)}</dd>
              {ram.standby !== undefined && <><dt>Em espera (standby)</dt><dd>{bytes(ram.standby)}</dd></>}
              {ram.modified !== undefined && <><dt>Modificada</dt><dd>{bytes(ram.modified)}</dd></>}
              {ram.free !== undefined && <><dt>Livre de verdade</dt><dd>{bytes(ram.free)}</dd></>}
              {ram.last && <><dt>Última limpeza</dt><dd>{ram.last}</dd></>}
            </dl>
          </div>
          <div className="row wrap" style={{ marginTop: 16 }}>
            <button className="btn primary" disabled={!admin || !!busy} onClick={() => clean(false)}>{busy === 'sb' ? <Spinner size={14} /> : <Icon name="zap" size={15} />}Liberar memória em espera</button>
            <button className="btn" disabled={!admin || !!busy} onClick={() => clean(true)} title="Também reduz a memória dos programas abertos">{busy === 'ws' ? <Spinner size={14} /> : <Icon name="memory" size={15} />}Liberar tudo</button>
          </div>
          <p className="small dim" style={{ marginBottom: 0 }}>A lista "em espera" é cache: o Windows normalmente a devolve sozinho, mas às vezes demora e o jogo engasga (o problema que o ISLC resolve). "Liberar tudo" também esvazia a memória dos programas abertos — eles ficam um pouco lentos ao voltar.</p>
        </Card>
        <Card>
          <CardTitle icon="clock" color="#22d3ee">Liberar automaticamente</CardTitle>
          <div className="list">
            <div className="li" style={{ padding: '12px 0' }}>
              <div className="grow"><div className="t">Quando a RAM passar do limite</div><div className="s">Verifica a cada 30 s, no máximo uma limpeza a cada 2 min. Funciona com o GROBS na bandeja.</div></div>
              <Toggle on={ram.config.enabled} onChange={v => cfg({ enabled: v })} disabled={!admin} />
            </div>
            <div className="li" style={{ padding: '12px 0' }}>
              <div className="grow"><div className="t">Limite: {ram.config.threshold}%</div>
                <input type="range" min={60} max={95} step={5} value={ram.config.threshold} onChange={e => cfg({ threshold: Number(e.target.value) })} style={{ width: '100%', accentColor: '#8b5cf6' }} disabled={!admin} /></div>
            </div>
            <div className="li" style={{ padding: '12px 0' }}>
              <div className="grow"><div className="t">Ao abrir um jogo</div><div className="s">Limpa assim que um jogo da sua lista (aba Gamer) abrir.</div></div>
              <Toggle on={ram.config.onGame} onChange={v => cfg({ onGame: v })} disabled={!admin} />
            </div>
            <div className="li" style={{ padding: '12px 0' }}>
              <div className="grow"><div className="t">Limpeza automática completa</div><div className="s">Inclui a memória dos programas abertos (mais agressivo).</div></div>
              <Toggle on={ram.config.workingSets} onChange={v => cfg({ workingSets: v })} disabled={!admin} />
            </div>
          </div>
        </Card>
      </div>
      <PagefileCard />
    </>
  );
}

type PF = { auto: boolean; files: { path: string; size: number; used: number; peak: number }[]; settings: { path: string; initial: number; max: number }[]; ramMb: number; compression: boolean | null; admin: boolean; recommended: { initial: number; max: number } };

function PagefileCard() {
  const [pf, setPf] = useState<PF | null>(null);
  const [busy, setBusy] = useState(false);
  const [custom, setCustom] = useState<{ drive: string; initial: number; max: number } | null>(null);
  useEffect(() => { call<PF>('pagefile.status').then(p => { setPf(p); setCustom({ drive: (p.settings[0]?.path ?? 'C:').slice(0, 2), initial: p.settings[0]?.initial || p.recommended.initial, max: p.settings[0]?.max || p.recommended.max }); }).catch(() => {}); }, []);
  async function apply(auto: boolean) {
    if (!custom) return;
    setBusy(true);
    const r = await run(call<PF>('pagefile.set', { auto, ...custom }), 'Memória virtual ajustada. Reinicie o PC para aplicar.');
    if (r) setPf(r);
    setBusy(false);
  }
  async function comp(v: boolean) {
    setBusy(true);
    const r = await run(call<PF>('pagefile.compression', { on: v }), `Compressão de memória ${v ? 'ligada' : 'desligada'} (vale após reiniciar)`);
    if (r) setPf(r);
    setBusy(false);
  }
  return (
    <>
      <h2 className="sec"><Icon name="disk" size={16} />Memória virtual</h2>
      <Card>
        {!pf || !custom ? <div className="empty"><Spinner /></div> : (
          <div className="grid g2">
            <div>
              <div className="row"><b style={{ flex: 1 }}>Arquivo de paginação</b>{!pf.admin && <AdminLock />}<span className={`chip ${pf.auto ? 'ok' : 'accent'}`}>{pf.auto ? 'Gerenciado pelo Windows' : 'Tamanho personalizado'}</span></div>
              {pf.files.map(f => (
                <div key={f.path} style={{ marginTop: 12 }}>
                  <div className="row small"><span className="mono">{f.path}</span><span className="spacer" /><span className="muted">{f.used} MB em uso · pico {f.peak} MB · {f.size} MB alocados</span></div>
                  <Bar value={(f.used / Math.max(1, f.size)) * 100} kind={f.used / Math.max(1, f.size) > 0.8 ? 'warn' : undefined} />
                </div>
              ))}
              <p className="small dim">Recomendado para {Math.round(pf.ramMb / 1024)} GB de RAM: deixar o Windows gerenciar. Se quiser fixar (evita o arquivo crescer durante o jogo): {pf.recommended.initial} – {pf.recommended.max} MB no SSD mais rápido.</p>
              <div className="row wrap">
                <input type="text" value={custom.drive} onChange={e => setCustom({ ...custom, drive: e.target.value.slice(0, 2).toUpperCase() })} style={{ width: 60 }} />
                <input type="text" value={custom.initial} onChange={e => setCustom({ ...custom, initial: Number(e.target.value.replace(/\D/g, '')) })} style={{ width: 90 }} title="Inicial (MB)" />
                <span className="dim">–</span>
                <input type="text" value={custom.max} onChange={e => setCustom({ ...custom, max: Number(e.target.value.replace(/\D/g, '')) })} style={{ width: 90 }} title="Máximo (MB)" />
                <span className="small dim">MB</span>
                <button className="btn sm" disabled={!pf.admin || busy} onClick={() => apply(false)}>Aplicar tamanho fixo</button>
                <button className="btn sm ghost" disabled={!pf.admin || busy || pf.auto} onClick={() => apply(true)}>Voltar ao automático</button>
              </div>
            </div>
            <div>
              <div className="row"><div style={{ flex: 1 }}><b>Compressão de memória</b><div className="small muted">O Windows comprime páginas pouco usadas em vez de mandá-las para o disco. Ligada é melhor com pouca RAM (8 GB ou menos); com 16 GB+ desligar economiza um pouco de CPU.</div></div>
                {pf.compression === null ? <span className="chip">indisponível</span> : <Toggle on={pf.compression} onChange={comp} disabled={!pf.admin || busy} />}</div>
            </div>
          </div>
        )}
      </Card>
    </>
  );
}

type Timer = { current: number; best: number; default: number; on: boolean; support: 'global' | 'perProcess'; admin: boolean };
type Parking = { minCores: number; off: boolean; admin: boolean };
type Msi = { id: string; name: string; kind: 'gpu' | 'net'; on: boolean };

function LatencyTab() {
  const admin = useStore(appStore, a => a?.admin);
  const [timer, setTimer] = useState<Timer | null>(null);
  const [park, setPark] = useState<Parking | null>(null);
  const [msi, setMsi] = useState<Msi[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => {
    call<Timer>('timer.status').then(setTimer).catch(() => {});
    call<Parking>('parking.status').then(setPark).catch(() => {});
    call<Msi[]>('msi.list').then(setMsi).catch(() => setMsi([]));
  }, []);

  async function act<T>(id: string, p: Promise<T>, ok: string, set: (v: T) => void) {
    setBusy(id);
    const r = await run(p, ok);
    if (r !== undefined) set(r);
    setBusy(null);
  }

  return (
    <>
      <Card style={{ marginBottom: 16, borderColor: 'rgba(251,191,36,.35)' }}>
        <div className="row"><Icon name="alert" size={20} style={{ color: '#fbbf24' }} />
          <div style={{ flex: 1 }}><b>Ajustes avançados</b><div className="small muted">Eles mexem em agendamento da CPU, interrupções de hardware e energia. Em quase todo PC ajudam ou não mudam nada, mas em alguns podem causar instabilidade. Antes do primeiro ajuste de risco o GROBS cria um ponto de restauração do Windows automaticamente — e tudo aqui tem "desfazer" no mesmo botão.</div></div>
        </div>
      </Card>
      <div className="grid g3">
        <Card>
          <CardTitle icon="timer" color="#22d3ee">Resolução do timer</CardTitle>
          {!timer ? <Spinner /> : (
            <>
              <div className="small muted">Pede ao Windows o timer de {timer.best.toFixed(1)} ms (padrão {timer.default.toFixed(1)} ms). Frametime mais estável em jogos que não fazem isso sozinhos. Vale enquanto o GROBS estiver aberto (pode ficar na bandeja).</div>
              {timer.support === 'perProcess' && <div className="small warn-t" style={{ marginTop: 8 }}><Icon name="info" size={12} /> No Windows 10 2004 ou mais novo o pedido só vale para o próprio processo: aqui o ajuste não afeta os jogos. A maioria dos jogos já pede 1 ms sozinha.</div>}
              <div className="row" style={{ marginTop: 12 }}><span style={{ flex: 1 }}>Timer de 0,5 ms <span className="small dim">(agora: {timer.current.toFixed(2).replace('.', ',')} ms)</span></span>{timer.support === 'global' && !admin && <AdminLock />}
                {busy === 'timer' ? <Spinner /> : <Toggle on={timer.on} disabled={timer.support === 'perProcess' && !timer.on} onChange={v => act('timer', call<Timer>('timer.set', { on: v }), v ? 'Timer de 0,5 ms ativo' : 'Timer padrão', setTimer)} />}</div>
            </>
          )}
        </Card>
        <Card>
          <CardTitle icon="cpu" color="#a78bfa">Estacionamento de núcleos</CardTitle>
          {!park ? <Spinner /> : (
            <>
              <div className="small muted">O Windows "dorme" núcleos ociosos para economizar energia; acordá-los leva microssegundos e pode causar engasgos. Desligar mantém todos os núcleos prontos (na tomada, no plano atual).</div>
              <div className="row" style={{ marginTop: 12 }}><span style={{ flex: 1 }}>Todos os núcleos sempre ativos {park.minCores >= 0 && <span className="small dim">(mínimo {park.minCores}%)</span>}</span>{!admin && <AdminLock />}
                {busy === 'park' ? <Spinner /> : <Toggle on={park.off} disabled={!admin} onChange={v => act('park', call<Parking>('parking.set', { off: v }), v ? 'Core parking desligado' : 'Core parking restaurado', setPark)} />}</div>
            </>
          )}
        </Card>
        <Card>
          <CardTitle icon="zap" color="#f472b6">Modo MSI (interrupções)</CardTitle>
          <div className="small muted">Interrupções por mensagem (MSI) reduzem a latência da GPU e da placa de rede. Muitos drivers já usam; ligue só nos que estão desligados. Requer reiniciar.</div>
          {!msi ? <Spinner /> : msi.length === 0 ? <div className="small dim" style={{ marginTop: 10 }}>Nenhuma GPU/placa de rede PCI encontrada.</div> : (
            <div className="list" style={{ marginTop: 6 }}>
              {msi.map(m => (
                <div key={m.id} className="li" style={{ padding: '8px 0' }}>
                  <Icon name={m.kind === 'gpu' ? 'gpu' : 'wifi'} size={15} className="muted" />
                  <div className="grow ellipsis small" title={m.name}>{m.name}</div>
                  {busy === m.id ? <Spinner /> : <Toggle on={m.on} disabled={!admin} onChange={v => act(m.id, call('msi.set', { id: m.id, on: v }), 'Modo MSI alterado — reinicie o PC', () => setMsi(l => l?.map(x => (x.id === m.id ? { ...x, on: v } : x)) ?? null))} />}
                </div>
              ))}
            </div>
          )}
        </Card>
      </div>
      <h2 className="sec"><Icon name="activity" size={16} />Agendamento e resposta</h2>
      <TweakList groups={['Latência e resposta']} icons={{ 'Latência e resposta': ['activity', '#22d3ee'] }} />
    </>
  );
}

type Net = { autotuning: string; rss: string; ecn: string; rsc: string; fastopen: string; adapters: { name: string; powerSaveOff: boolean; eee: boolean | null }[]; wifiPower: number; admin: boolean };

function NetTab() {
  const [n, setN] = useState<Net | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => { call<Net>('nettune.status').then(setN).catch(() => {}); }, []);
  async function set(what: string, v: boolean) {
    setBusy(what);
    const r = await run(call<Net>('nettune.set', { what, on: v }), 'Ajuste de rede aplicado');
    if (r) setN(r);
    setBusy(null);
  }
  async function flush() {
    setBusy('flush');
    await run(call('nettune.flush'), 'Caches de DNS, ARP e NetBIOS limpos');
    setBusy(null);
  }
  if (!n) return <div className="empty"><Spinner size={24} /></div>;
  const items: { id: string; title: string; desc: string; on: boolean; rec: boolean }[] = [
    { id: 'autotuning', title: 'Ajuste automático da janela TCP', desc: `Atual: ${n.autotuning || '?'}. "normal" aproveita toda a velocidade da internet; alguns "otimizadores" desligam e deixam downloads lentos.`, on: n.autotuning === 'normal', rec: true },
    { id: 'rss', title: 'Receive-Side Scaling (RSS)', desc: 'Divide o processamento da rede entre vários núcleos.', on: n.rss === 'enabled', rec: true },
    { id: 'rsc', title: 'União de segmentos (RSC)', desc: 'Junta pacotes para economizar CPU, mas pode somar latência. Para jogos: desligado.', on: n.rsc === 'enabled', rec: false },
    { id: 'ecn', title: 'ECN', desc: 'Sinalização de congestionamento. Alguns roteadores antigos lidam mal; padrão do Windows: desligado.', on: n.ecn === 'enabled', rec: false },
    { id: 'fastopen', title: 'TCP Fast Open', desc: 'Abre conexões repetidas mais rápido.', on: n.fastopen === 'enabled', rec: true },
  ];
  const adapterOff = n.adapters.length > 0 && n.adapters.every(a => a.powerSaveOff);
  return (
    <div className="grid g2">
      <Card className="pad-0">
        <div style={{ padding: '16px 18px 4px' }}><CardTitle icon="network" color="#22d3ee" right={!n.admin && <AdminLock />}>TCP/IP</CardTitle></div>
        <div className="list">
          {items.filter(i => n[i.id as keyof Net] !== '').map(i => (
            <div key={i.id} className="li" style={{ alignItems: 'flex-start' }}>
              <div className="grow"><div className="row" style={{ gap: 8 }}><span className="t">{i.title}</span><span className={`risk ${i.on === i.rec ? 'low' : 'medium'}`}>{i.on === i.rec ? 'ideal' : `recomendado: ${i.rec ? 'ligado' : 'desligado'}`}</span></div><div className="s">{i.desc}</div></div>
              {busy === i.id ? <Spinner /> : <Toggle on={i.on} disabled={!n.admin} onChange={v => set(i.id, v)} />}
            </div>
          ))}
        </div>
      </Card>
      <div>
        <Card className="pad-0">
          <div style={{ padding: '16px 18px 4px' }}><CardTitle icon="power" color="#34d399">Economia de energia da rede</CardTitle></div>
          <div className="list">
            <div className="li" style={{ alignItems: 'flex-start' }}>
              <div className="grow"><div className="t">Placa de rede sem economia de energia</div><div className="s">Impede o Windows de desligar a placa e desliga o "Ethernet verde" (EEE), que causa picos de ping. Requer reiniciar.<br />{n.adapters.map(a => a.name).join(' · ')}</div></div>
              {busy === 'adapterPower' ? <Spinner /> : <Toggle on={adapterOff} disabled={!n.admin || n.adapters.length === 0} onChange={v => set('adapterPower', v)} />}
            </div>
            {n.wifiPower >= 0 && (
              <div className="li" style={{ alignItems: 'flex-start' }}>
                <div className="grow"><div className="t">Wi-Fi em desempenho máximo</div><div className="s">O modo de economia do adaptador sem fio aumenta a latência. Vale no plano de energia atual.</div></div>
                {busy === 'wifiPower' ? <Spinner /> : <Toggle on={n.wifiPower === 0} disabled={!n.admin} onChange={v => set('wifiPower', v)} />}
              </div>
            )}
          </div>
        </Card>
        <Card style={{ marginTop: 16 }}>
          <div className="row"><Icon name="refresh" size={18} className="muted" /><div style={{ flex: 1 }}><b>Limpar caches de rede</b><div className="small muted">DNS, ARP e NetBIOS. Resolve sites/servidores que mudaram de endereço.</div></div>
            <button className="btn sm" onClick={flush} disabled={busy === 'flush'}>{busy === 'flush' ? <Spinner size={13} /> : <Icon name="broom" size={14} />}Limpar</button></div>
        </Card>
        <p className="small dim">DNS mais rápido, Nagle e teste de ping ficam na aba <b>Gamer → Ping e rede</b>.</p>
      </div>
    </div>
  );
}

export default function Perf() {
  const [tab, setTab] = useState<'mem' | 'lat' | 'net'>('mem');
  return (
    <div className="page">
      <PageHead title="Memória e latência" sub="Liberador de RAM, memória virtual, timer, núcleos, interrupções e rede — menos engasgos e input lag."
        right={<Tabs layoutId="perftab" value={tab} onChange={setTab} items={[{ id: 'mem', label: 'Memória' }, { id: 'lat', label: 'Latência' }, { id: 'net', label: 'Rede avançada' }]} />} />
      {tab === 'mem' && <MemoryTab />}
      {tab === 'lat' && <LatencyTab />}
      {tab === 'net' && <NetTab />}
    </div>
  );
}
