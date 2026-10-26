import { motion, AnimatePresence } from 'motion/react';
import { Fragment, useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import CountUp from '../components/rb/CountUp';
import GradientText from '../components/rb/GradientText';
import StarBorder from '../components/rb/StarBorder';
import GlareHover from '../components/rb/GlareHover';
import { Card, CardTitle, Gauge, Modal, Spark, Spinner, Stagger, Beam } from '../components/ui';
import { bits, bytes, mhz, pct, rate, temp, uptime } from '../format';
import { appStore, boostStore, historyStore, run, statsStore, toast, useStore } from '../store';
import type { PageId } from '../App';

type Issue = { id: string; level: 'bad' | 'warn' | 'info'; title: string; detail: string; page: PageId; penalty: number; fix?: 'clean' };
type Check = { score: number; issues: Issue[]; junk: any[]; when: Date };

let lastCheck: Check | null = null; // mantém o resultado ao trocar de página

async function runCheck(): Promise<Check> {
  const [junk, startup, sec, disks, sys] = await Promise.all([
    call<any[]>('clean.scan').catch(() => []),
    call<any[]>('startup.list').catch(() => []),
    call<any>('sec.status').catch(() => null),
    call<any>('sys.disks').catch(() => null),
    call<any>('sys.sample').catch(() => null),
  ]);
  const issues: Issue[] = [];
  const junkSize = junk.filter(j => j.default && !j.locked).reduce((a, j) => a + j.size, 0);
  if (junkSize > 100e6) {
    const p = junkSize > 5e9 ? 20 : junkSize > 2e9 ? 12 : junkSize > 500e6 ? 6 : 2;
    issues.push({ id: 'junk', level: p >= 12 ? 'warn' : 'info', title: `${bytes(junkSize)} de arquivos desnecessários`, detail: 'Temporários, caches e relatórios de erro que podem ser removidos com segurança.', page: 'clean', penalty: p, fix: 'clean' });
  }
  const enabled = startup.filter(s => s.enabled).length;
  if (enabled > 5) {
    const p = enabled > 12 ? 12 : enabled > 8 ? 8 : 4;
    issues.push({ id: 'startup', level: enabled > 8 ? 'warn' : 'info', title: `${enabled} programas iniciam com o Windows`, detail: 'Desativar os que você não usa sempre deixa a inicialização mais rápida.', page: 'optimize', penalty: p });
  }
  if (sec) {
    const otherAv = (sec.products ?? []).some((p: any) => p.enabled && !/defender/i.test(p.name));
    if (sec.defender && !sec.realtime && !otherAv) issues.push({ id: 'rt', level: 'bad', title: 'Proteção em tempo real desativada', detail: 'Seu PC está sem antivírus ativo.', page: 'security', penalty: 25 });
    if (sec.defender && sec.sigAge > 7 && !otherAv) issues.push({ id: 'sig', level: 'warn', title: `Definições de vírus com ${sec.sigAge} dias`, detail: 'Atualize as definições do Microsoft Defender.', page: 'security', penalty: 8 });
    if (sec.defender && sec.quickAge > 14 && !otherAv) issues.push({ id: 'scan', level: 'info', title: 'Nenhuma verificação recente', detail: `A última verificação rápida foi há ${sec.quickAge} dias.`, page: 'security', penalty: 4 });
    const fw = sec.firewall ?? {};
    if (fw.domain === false || fw.private === false || fw.public === false) issues.push({ id: 'fw', level: 'bad', title: 'Firewall desativado em algum perfil', detail: 'Ative o Firewall do Windows em todas as redes.', page: 'security', penalty: 10 });
    if (sec.uac === false) issues.push({ id: 'uac', level: 'bad', title: 'Controle de Conta de Usuário desativado', detail: 'Programas podem obter privilégios sem pedir.', page: 'security', penalty: 10 });
  }
  if (disks) {
    for (const d of disks.physical ?? []) {
      if (d.health === 'bad') issues.push({ id: 'dh' + d.name, level: 'bad', title: `Disco "${d.name}" com falha`, detail: 'Faça backup dos seus arquivos imediatamente.', page: 'monitor', penalty: 30 });
      else if (d.health === 'warn') issues.push({ id: 'dh' + d.name, level: 'warn', title: `Disco "${d.name}" com alerta de saúde`, detail: 'Considere fazer backup e trocar o disco.', page: 'monitor', penalty: 15 });
    }
    const c = (disks.volumes ?? []).find((v: any) => v.name === 'C:');
    if (c) {
      const free = c.free / c.total;
      if (free < 0.1) issues.push({ id: 'cfree', level: 'bad', title: `Unidade C: quase cheia (${bytes(c.free)} livres)`, detail: 'Pouco espaço deixa o Windows lento e impede atualizações.', page: 'disk', penalty: 12 });
      else if (free < 0.2) issues.push({ id: 'cfree', level: 'warn', title: `Unidade C: com ${Math.round(free * 100)}% livre`, detail: 'Veja o que está ocupando espaço.', page: 'disk', penalty: 5 });
    }
  }
  if (sys && sys.mem > 85) issues.push({ id: 'mem', level: 'warn', title: `Memória em ${Math.round(sys.mem)}% de uso`, detail: 'Feche programas pesados ou veja quem está consumindo.', page: 'monitor', penalty: 5 });
  const score = Math.max(0, 100 - issues.reduce((a, i) => a + i.penalty, 0));
  return { score, issues: issues.sort((a, b) => b.penalty - a.penalty), junk, when: new Date() };
}

function scoreInfo(s: number) {
  if (s >= 90) return { label: 'Excelente', colors: ['#34d399', '#22d3ee'] as [string, string], text: 'Seu PC está em ótima forma.' };
  if (s >= 70) return { label: 'Bom', colors: ['#8b5cf6', '#22d3ee'] as [string, string], text: 'Alguns ajustes podem deixar tudo mais rápido.' };
  if (s >= 50) return { label: 'Atenção', colors: ['#fbbf24', '#f97316'] as [string, string], text: 'Encontramos pontos que merecem cuidado.' };
  return { label: 'Crítico', colors: ['#f87171', '#f43f5e'] as [string, string], text: 'Há problemas importantes para resolver.' };
}

function Hero({ go }: { go: (p: PageId) => void }) {
  const [check, setCheck] = useState<Check | null>(lastCheck);
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [fixing, setFixing] = useState(false);

  async function scan() {
    setBusy(true);
    const c = await run(runCheck());
    if (c) { lastCheck = c; setCheck(c); }
    setBusy(false);
  }

  const cleanable = check?.junk.filter(j => j.default && !j.locked && j.size > 0) ?? [];
  async function fix() {
    setConfirm(false); setFixing(true);
    const r = await run(call<any>('clean.run', { ids: cleanable.map(j => j.id) }));
    if (r) toast(`${bytes(r.freed)} liberados em ${r.files.toLocaleString('pt-BR')} arquivos`);
    await scan();
    setFixing(false);
  }

  const info = check ? scoreInfo(check.score) : null;
  return (
    <Card className="hero" style={{ padding: 26, overflow: 'hidden' }}>
      {busy && <Beam size={160} duration={3} />}
      <div className="row" style={{ gap: 30, alignItems: 'center' }}>
        <div style={{ position: 'relative' }}>
          <Gauge value={busy ? 100 : check?.score ?? 0} size={170} stroke={8} colors={info?.colors}>
            {busy ? <div className="tri-loader" /> :
              check ? (
                <div>
                  <div className="stat-big" style={{ fontSize: 46 }}><CountUp key={check.when.getTime()} to={check.score} duration={1.2} trigger="load" /></div>
                  <div className="small muted">de 100</div>
                </div>
              ) : <Icon name="heart" size={40} stroke={1.4} style={{ color: 'var(--text-3)' }} />}
          </Gauge>
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="small dim" style={{ textTransform: 'uppercase', letterSpacing: 1.4, marginBottom: 6 }}>Saúde do sistema</div>
          <h2 style={{ font: '650 30px/1.15 var(--font-display)', margin: '0 0 8px', letterSpacing: -0.4 }}>
            {busy ? 'Analisando seu PC…' : info ? <GradientText colors={[...info.colors, info.colors[0]]} animationSpeed={6}>{info.label}</GradientText> : <GradientText colors={['#a78bfa', '#22d3ee', '#a78bfa']} animationSpeed={6}>Vamos verificar seu PC</GradientText>}
          </h2>
          <p className="muted" style={{ margin: '0 0 18px', maxWidth: 520 }}>
            {busy ? 'Procurando arquivos desnecessários, programas na inicialização, proteção e saúde dos discos.'
              : info ? `${info.text} Última verificação às ${check!.when.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}.`
                : 'Uma verificação completa leva poucos segundos e nada é alterado sem a sua confirmação.'}
          </p>
          <div className="row wrap">
            <StarBorder as="button" className="sb-cta" onClick={scan} disabled={busy || fixing} color="#a78bfa" trailColor="#22d3ee" duration={4} radius={12} glow={0.6} backgroundColor="#151826" textColor="#fff">
              <span className="row" style={{ gap: 8, fontWeight: 600 }}>{busy ? <Spinner size={16} /> : <Icon name="sparkles" size={16} />}{check ? 'Verificar novamente' : 'Verificar agora'}</span>
            </StarBorder>
            {cleanable.length > 0 && !busy && (
              <button className="btn primary lg" onClick={() => setConfirm(true)} disabled={fixing}>
                {fixing ? <Spinner size={16} /> : <Icon name="broom" size={16} />}Limpar {bytes(cleanable.reduce((a, j) => a + j.size, 0))}
              </button>
            )}
          </div>
        </div>
      </div>

      <AnimatePresence>
        {check && !busy && (
          <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }} style={{ overflow: 'hidden' }}>
            <div style={{ marginTop: 22, borderTop: '1px solid var(--line)', paddingTop: 14 }}>
              {check.issues.length === 0 && <div className="row ok-t" style={{ padding: '6px 2px' }}><Icon name="check" />Nenhum problema encontrado.</div>}
              <Stagger className="list">
                {check.issues.map(i => (
                  <div key={i.id} className="li" style={{ padding: '10px 4px', cursor: 'pointer' }} onClick={() => go(i.page)}>
                    <span className={`dot ${i.level === 'info' ? 'ok' : i.level}`} style={i.level === 'info' ? { background: 'var(--info)', boxShadow: '0 0 10px var(--info)' } : undefined} />
                    <div className="grow"><div className="t">{i.title}</div><div className="s">{i.detail}</div></div>
                    <span className="dim small">−{i.penalty}</span>
                    <Icon name="chevronRight" size={16} className="dim" />
                  </div>
                ))}
              </Stagger>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Limpar arquivos desnecessários?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Os itens abaixo serão removidos. Senhas, histórico e cookies dos navegadores não são tocados. Itens em uso são ignorados.</p>
        <div className="list" style={{ maxHeight: 260, overflow: 'auto', margin: '8px 0 16px', border: '1px solid var(--line)', borderRadius: 12 }}>
          {cleanable.map(j => (
            <div key={j.id} className="li" style={{ padding: '9px 12px' }}><div className="grow ellipsis">{j.name}</div><b>{bytes(j.size)}</b></div>
          ))}
        </div>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn primary" onClick={fix}><Icon name="broom" size={15} />Limpar agora</button></div>
      </Modal>
    </Card>
  );
}

function LiveTile({ icon, label, color, value, sub, data, max, extra }: { icon: string; label: string; color: string; value: string; sub: string; data: number[]; max?: number; extra?: React.ReactNode }) {
  return (
    <Card spotlight color={`${color}33`} className="tile" style={{ padding: 16 }}>
      <div className="row" style={{ marginBottom: 10 }}>
        <span className="ico" style={{ width: 30, height: 30, borderRadius: 9, display: 'grid', placeItems: 'center', color, background: `${color}1f` }}><Icon name={icon} size={16} /></span>
        <span className="muted" style={{ fontWeight: 550 }}>{label}</span>
        <span className="spacer" />{extra}
      </div>
      <div className="stat-big" style={{ fontSize: 26 }}>{value}</div>
      <div className="small dim ellipsis" style={{ marginTop: 3, minHeight: 17 }}>{sub}</div>
      <div style={{ marginTop: 10, marginInline: -4 }}><Spark data={data} max={max} color={color} height={40} /></div>
    </Card>
  );
}

function LiveGrid() {
  const s = useStore(statsStore);
  const h = useStore(historyStore);
  const unit = useStore(appStore, a => a?.settings.tempUnit ?? 'C');
  if (!s) return <div className="grid g3">{[0, 1, 2, 3, 4, 5].map(i => <Card key={i} style={{ height: 150, display: 'grid', placeItems: 'center' }}><Spinner color="var(--text-3)" /></Card>)}</div>;
  const hasTemp = s.cpuTemp > 0 || s.gpuTemp > 0;
  return (
    <Stagger className="grid g3">
      <LiveTile icon="cpu" label="Processador" color="#8b5cf6" value={pct(s.cpu)} sub={`${mhz(s.cpuMhz)} · ${s.procs ?? '—'} processos`} data={h.cpu} max={100} />
      <LiveTile icon="memory" label="Memória" color="#22d3ee" value={pct(s.mem)} sub={`${bytes(s.memUsed)} de ${bytes(s.memTotal)}`} data={h.mem} max={100} />
      <LiveTile icon="gpu" label="Placa de vídeo" color="#f472b6" value={pct(s.gpu)} sub={s.gpuMem > 0 ? `${bytes(s.gpuMem)} de VRAM em uso` : 'Uso do motor 3D'} data={h.gpu} max={100} />
      <LiveTile icon="disk" label="Disco" color="#fbbf24" value={pct(s.disk)} sub={`L ${rate(s.diskRead)} · E ${rate(s.diskWrite)}`} data={h.disk} max={100} />
      <LiveTile icon="wifi" label="Rede" color="#34d399" value={bits(s.netDown)} sub={`↑ ${bits(s.netUp)}`} data={h.netDown} />
      {hasTemp
        ? <LiveTile icon="thermo" label="Temperatura" color="#fb7185" value={temp(s.cpuTemp > 0 ? s.cpuTemp : s.gpuTemp, unit)} sub={s.cpuTemp > 0 ? `CPU (${s.cpuTempSrc})${s.gpuTemp > 0 ? ` · GPU ${temp(s.gpuTemp, unit)}` : ''}` : `GPU (${s.gpuTempSrc})`} data={h.cpuTemp} max={100} />
        : <LiveTile icon="clock" label="Tempo ligado" color="#60a5fa" value={uptime(s.uptime)} sub={`${(s.threads ?? 0).toLocaleString('pt-BR')} threads`} data={h.cpu} max={100} />}
    </Stagger>
  );
}

function QuickActions({ go }: { go: (p: PageId) => void }) {
  const boost = useStore(boostStore);
  const apps = useStore(appStore, a => a?.settings.boostApps);
  async function toggleBoost() {
    if (boost) { await run(call('boost.stop'), 'Modo Turbo desativado — plano de energia restaurado'); boostStore.set(false); }
    else {
      const r = await run(call<any>('boost.start', { apps: apps ?? [] }));
      if (r) { boostStore.set(true); toast(`Modo Turbo ativo${r.plan ? ` · plano "${r.plan}"` : ''}${r.closed ? ` · ${r.closed} app(s) fechados` : ''}`); }
    }
  }
  const items = [
    { icon: 'rocket', title: boost ? 'Desligar Turbo' : 'Modo Turbo', sub: boost ? 'Restaurar o plano anterior' : 'Desempenho máximo para jogos', color: '#a78bfa', onClick: toggleBoost, active: boost },
    { icon: 'broom', title: 'Limpeza', sub: 'Escolher o que limpar', color: '#22d3ee', onClick: () => go('clean') },
    { icon: 'power', title: 'Inicialização', sub: 'Acelerar o boot', color: '#34d399', onClick: () => go('optimize') },
    { icon: 'shieldCheck', title: 'Verificar vírus', sub: 'Microsoft Defender', color: '#fbbf24', onClick: () => go('security') },
  ];
  return (
    <div className="grid g4">
      {items.map(it => (
        <GlareHover key={it.title} background="var(--surface)" borderColor={it.active ? 'rgba(139,92,246,.6)' : 'rgba(255,255,255,0.07)'} borderRadius="16px" width="100%" height="auto" glareColor="#ffffff" glareOpacity={0.18} glareSize={220} className="qa">
          <button className="qa-btn" onClick={it.onClick}>
            <span className="qa-ico" style={{ color: it.color, background: `${it.color}1f` }}><Icon name={it.icon} size={20} /></span>
            <span style={{ textAlign: 'left', minWidth: 0 }}>
              <b style={{ display: 'block' }}>{it.title}</b>
              <span className="small dim ellipsis" style={{ display: 'block' }}>{it.sub}</span>
            </span>
          </button>
        </GlareHover>
      ))}
    </div>
  );
}

function SystemCard() {
  const [info, setInfo] = useState<any>(null);
  useEffect(() => { call('sys.info').then(setInfo).catch(() => {}); }, []);
  return (
    <Card>
      <CardTitle icon="info" color="#60a5fa">Seu sistema</CardTitle>
      {!info ? <div className="empty" style={{ padding: 20 }}><Spinner /></div> : (
        <dl className="kv selectable">
          <dt>Sistema</dt><dd>{info.os} {info.osVersion} <span className="dim">({info.osBuild})</span></dd>
          <dt>Processador</dt><dd className="ellipsis" title={info.cpu}>{info.cpu}</dd>
          <dt>Núcleos</dt><dd>{info.cpuCores} núcleos · {info.cpuThreads} threads</dd>
          <dt>Memória</dt><dd>{bytes(info.ramTotal)} · {info.ramSticks} pente(s){info.ramSpeed ? ` · ${info.ramSpeed} MT/s` : ''}</dd>
          {(info.gpus ?? []).map((g: any, i: number) => <Fragment key={i}><dt>Vídeo {info.gpus.length > 1 ? i + 1 : ''}</dt><dd className="ellipsis" title={g.name}>{g.name}</dd></Fragment>)}
          {info.board && <><dt>Placa-mãe</dt><dd className="ellipsis">{info.board}</dd></>}
        </dl>
      )}
    </Card>
  );
}

function StorageCard({ go }: { go: (p: PageId) => void }) {
  const [d, setD] = useState<any>(null);
  useEffect(() => { call('sys.disks').then(setD).catch(() => {}); }, []);
  return (
    <Card>
      <CardTitle icon="disk" color="#fbbf24" right={<button className="btn ghost sm" onClick={() => go('disk')}>Analisar<Icon name="chevronRight" size={14} /></button>}>Armazenamento</CardTitle>
      {!d ? <div className="empty" style={{ padding: 20 }}><Spinner /></div> : (
        <div className="list" style={{ gap: 14 }}>
          {d.volumes.map((v: any) => {
            const used = (1 - v.free / v.total) * 100;
            return (
              <div key={v.name}>
                <div className="row small" style={{ marginBottom: 6 }}>
                  <b style={{ fontSize: 13 }}>{v.name}</b><span className="dim ellipsis">{v.label}</span><span className="spacer" />
                  <span className="muted">{bytes(v.free)} livres de {bytes(v.total)}</span>
                </div>
                <div className={`bar ${used > 90 ? 'bad' : used > 80 ? 'warn' : ''}`}><i style={{ ["--v" as any]: used / 100 }} /></div>
              </div>
            );
          })}
          <div className="row wrap" style={{ gap: 6 }}>
            {d.physical.map((p: any) => (
              <span key={p.name} className={`chip ${p.health === 'ok' ? 'ok' : p.health === 'unknown' ? '' : p.health === 'warn' ? 'warn' : 'bad'}`}>
                <span className={`dot ${p.health === 'ok' ? 'ok' : p.health === 'warn' ? 'warn' : p.health === 'bad' ? 'bad' : ''}`} style={{ width: 6, height: 6 }} />
                {p.type} · {p.name}
              </span>
            ))}
          </div>
        </div>
      )}
    </Card>
  );
}

export default function Dashboard({ go }: { go: (p: PageId) => void }) {
  return (
    <div className="page">
      <Hero go={go} />
      <h2 className="sec"><Icon name="activity" size={16} />Em tempo real</h2>
      <LiveGrid />
      <h2 className="sec"><Icon name="zap" size={16} />Ações rápidas</h2>
      <QuickActions go={go} />
      <div className="grid g2" style={{ marginTop: 16 }}>
        <SystemCard />
        <StorageCard go={go} />
      </div>
    </div>
  );
}
