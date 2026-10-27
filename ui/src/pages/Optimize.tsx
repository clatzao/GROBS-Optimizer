import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Beam, Card, CardTitle, PageHead, Spinner, Stagger, Tabs, Toggle } from '../components/ui';
import { appStore, boostStore, run, setSetting, toast, useStore } from '../store';

type StartupItem = { id: string; name: string; desc: string; company: string; signer: string | null; enabled: boolean; location: string; path: string; admin?: boolean; exists: boolean; suspicious?: boolean };

function StartupTab() {
  const [list, setList] = useState<StartupItem[] | null>(null);
  const admin = useStore(appStore, a => a?.admin);
  const [delays, setDelays] = useState<Record<string, { seconds: number }>>({});
  const load = () => { call<StartupItem[]>('startup.list').then(setList).catch(() => setList([])); call<Record<string, { seconds: number }>>('startup.delays').then(d => setDelays(d ?? {})).catch(() => {}); };
  useEffect(() => { load(); }, []);
  async function delay(it: StartupItem, seconds: number) {
    const r = await run(call<Record<string, { seconds: number }>>('startup.delay', { id: it.id, seconds }), seconds ? `${it.desc || it.name} vai abrir ${seconds >= 60 ? seconds / 60 + ' min' : seconds + ' s'} depois do logon` : 'Atraso removido');
    if (r) { setDelays(r); load(); }
  }

  async function set(it: StartupItem, enabled: boolean) {
    setList(l => l?.map(x => (x.id === it.id ? { ...x, enabled } : x)) ?? null);
    const ok = await run(call('startup.set', { id: it.id, enabled }), enabled ? `${it.name} vai iniciar com o Windows` : `${it.name} não vai mais iniciar com o Windows`);
    if (ok === undefined) load();
  }

  if (!list) return <div className="empty"><Spinner size={24} /></div>;
  const on = list.filter(i => i.enabled).length;
  return (
    <>
      <div className="grid g3" style={{ marginBottom: 16 }}>
        <Card><div className="small dim">Ativos</div><div className="stat-big">{on}</div></Card>
        <Card><div className="small dim">Desativados</div><div className="stat-big">{list.length - on}</div></Card>
        <Card><div className="small dim">Dica</div><div className="small muted" style={{ marginTop: 4 }}>Desativar não desinstala nada. Para o que você precisa mas não na hora (Steam, Discord), use "Atrasar": o Windows liga mais rápido e o programa abre depois.</div></Card>
      </div>
      <Card className="pad-0">
        <Stagger className="list" delay={0.025}>
          {list.map(it => (
            <div key={it.id} className="li">
              <div style={{ width: 36, height: 36, borderRadius: 10, display: 'grid', placeItems: 'center', background: 'var(--surface-3)', color: it.enabled ? '#c4b5fd' : 'var(--text-3)', flex: 'none' }}><Icon name="power" size={17} /></div>
              <div className="grow">
                <div className="row" style={{ gap: 8 }}>
                  <span className="t ellipsis">{it.desc || it.name}</span>
                  {it.suspicious && <span className="chip bad">pasta Temp</span>}
                  {!it.exists && <span className="chip">arquivo ausente</span>}
                </div>
                <div className="s ellipsis" title={it.path}>{it.company || it.signer || 'Editor desconhecido'} · {it.location}</div>
              </div>
              {delays[it.id] && <span className="chip accent"><Icon name="clock" size={11} />atrasado {delays[it.id].seconds >= 60 ? delays[it.id].seconds / 60 + ' min' : delays[it.id].seconds + ' s'}</span>}
              {(it.enabled || delays[it.id]) && admin && (
                <select value={delays[it.id]?.seconds ?? 0} onChange={e => delay(it, Number(e.target.value))} title="Abrir este programa alguns segundos depois do logon (o Windows liga mais rápido)" style={{ padding: '4px 8px', fontSize: 12 }}>
                  {[[0, 'Sem atraso'], [30, 'Atrasar 30 s'], [60, 'Atrasar 1 min'], [120, 'Atrasar 2 min'], [300, 'Atrasar 5 min']].map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                </select>
              )}
              {it.admin && !admin ? <AdminLock /> : null}
              <Toggle on={it.enabled} onChange={v => set(it, v)} disabled={(it.admin && !admin) || !!delays[it.id]} title={it.path} />
            </div>
          ))}
          {list.length === 0 && <div className="empty">Nenhum programa configurado para iniciar com o Windows.</div>}
        </Stagger>
      </Card>
    </>
  );
}

type Tweak = { id: string; group: string; title: string; desc: string; risk: string; admin?: boolean; locked?: boolean; restart?: boolean; on: boolean };

function TweaksTab() {
  const [list, setList] = useState<Tweak[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const admin = useStore(appStore, a => a?.admin);
  useEffect(() => { call<Tweak[]>('tweaks.list').then(setList).catch(() => setList([])); }, []);

  async function set(t: Tweak, on: boolean) {
    setBusy(t.id);
    const r = await run(call<boolean>('tweaks.set', { id: t.id, on }));
    if (r !== undefined) {
      setList(l => l?.map(x => (x.id === t.id ? { ...x, on } : x)) ?? null);
      toast(t.restart ? `${t.title}: aplicado. Reinicie o PC para ter efeito.` : `${t.title}: ${on ? 'aplicado' : 'revertido'}`, t.restart ? 'info' : 'ok');
    }
    setBusy(null);
  }
  async function restorePoint() {
    setBusy('rp');
    await run(call('tools.restorePoint'), 'Ponto de restauração criado');
    setBusy(null);
  }

  if (!list) return <div className="empty"><Spinner size={24} /></div>;
  const groups = ['Desempenho', 'Privacidade'];
  return (
    <>
      <Card style={{ marginBottom: 16 }}>
        <div className="row">
          <Icon name="restore" size={20} className="muted" />
          <div style={{ flex: 1 }}><b>Segurança primeiro</b><div className="small muted">Todo ajuste pode ser revertido aqui mesmo. Para garantia extra, crie um ponto de restauração do Windows antes.</div></div>
          <button className="btn" onClick={restorePoint} disabled={!admin || busy === 'rp'}>{busy === 'rp' ? <Spinner size={14} /> : <Icon name="restore" size={15} />}Criar ponto de restauração</button>
          {!admin && <AdminLock />}
        </div>
      </Card>
      <div className="grid g2">
        {groups.map(g => (
          <Card key={g} className="pad-0">
            <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
              <Icon name={g === 'Desempenho' ? 'zap' : 'eye'} size={17} style={{ color: g === 'Desempenho' ? '#a78bfa' : '#22d3ee' }} />
              <b>{g}</b><span className="spacer" /><span className="small dim">{list.filter(t => t.group === g && t.on).length}/{list.filter(t => t.group === g).length} ativos</span>
            </div>
            <Stagger className="list" delay={0.03}>
              {list.filter(t => t.group === g).map(t => (
                <div key={t.id} className="li" style={{ alignItems: 'flex-start' }}>
                  <div className="grow">
                    <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
                      <span className="t">{t.title}</span>
                      <span className={`risk ${t.risk}`}>{t.risk === 'low' ? 'seguro' : t.risk === 'medium' ? 'moderado' : 'avançado'}</span>
                      {t.restart && <span className="chip">reiniciar</span>}
                    </div>
                    <div className="s" style={{ marginTop: 3 }}>{t.desc}</div>
                  </div>
                  {t.locked && <AdminLock />}
                  {busy === t.id ? <Spinner /> : <Toggle on={t.on} onChange={v => set(t, v)} disabled={t.locked} />}
                </div>
              ))}
            </Stagger>
          </Card>
        ))}
      </div>
    </>
  );
}

type Plan = { id: string; name: string; kind: string; active: boolean };

function PowerTab() {
  const [plans, setPlans] = useState<Plan[] | null>(null);
  const [hib, setHib] = useState<boolean | null>(null);
  const boost = useStore(boostStore);
  const app = useStore(appStore);
  const [appsText, setAppsText] = useState((app?.settings.boostApps ?? []).join(', '));
  const load = () => call<Plan[]>('power.list').then(setPlans).catch(() => setPlans([]));
  useEffect(() => { load(); call<boolean>('power.hibernation').then(setHib).catch(() => {}); }, []);

  async function activate(p: Plan) { await run(call('power.set', { id: p.id }), `Plano "${p.name}" ativado`); load(); }
  async function unlock() { const r = await run(call<Plan[]>('power.unlockUltimate'), 'Plano Desempenho Máximo disponível'); if (r) setPlans(r); }
  async function toggleBoost() {
    const apps = appsText.split(',').map(s => s.trim()).filter(Boolean);
    await setSetting('boostApps', apps);
    if (boost) { await run(call('boost.stop'), 'Modo Turbo desativado'); boostStore.set(false); }
    else { const r = await run(call<any>('boost.start', { apps })); if (r) { boostStore.set(true); toast(`Modo Turbo ativo${r.closed ? ` · ${r.closed} app(s) fechados` : ''}`); } }
    load();
  }
  async function setHibernation(on: boolean) { const r = await run(call<boolean>('power.setHibernation', { on })); if (r !== undefined) { setHib(r); toast(r ? 'Hibernação ativada' : 'Hibernação desativada — hiberfil.sys removido'); } }

  const icon: Record<string, string> = { balanced: 'activity', high: 'zap', ultimate: 'rocket', saver: 'battery', custom: 'settings' };
  return (
    <>
      <Card style={{ padding: 22, marginBottom: 16, overflow: 'hidden', borderColor: boost ? 'rgba(139,92,246,.45)' : undefined }}>
        {boost && <Beam size={200} duration={4} width={2} />}
        <div className="row" style={{ gap: 20, alignItems: 'flex-start' }}>
          <div style={{ width: 60, height: 60, borderRadius: 18, display: 'grid', placeItems: 'center', background: boost ? 'var(--grad)' : 'var(--surface-3)', boxShadow: boost ? '0 0 30px rgba(139,92,246,.5)' : undefined, transition: 'all .4s' }}><Icon name="rocket" size={28} style={{ color: '#fff' }} /></div>
          <div style={{ flex: 1 }}>
            <div className="row"><b style={{ fontSize: 17 }}>Modo Turbo</b>{boost && <span className="chip accent pulse">ativo</span>}</div>
            <p className="muted small" style={{ margin: '4px 0 12px' }}>Ativa o plano de energia mais rápido disponível e fecha os apps que você listar. Ao desligar, o plano anterior volta. Sem truques de “limpar RAM”: só o que tem efeito real.</p>
            <div className="row">
              <input type="text" placeholder="Apps para fechar (ex.: chrome, discord, onedrive)" value={appsText} onChange={e => setAppsText(e.target.value)} style={{ flex: 1 }} disabled={boost} />
              <button className={`btn ${boost ? '' : 'primary'}`} onClick={toggleBoost}><Icon name={boost ? 'stop' : 'play'} size={14} />{boost ? 'Desligar' : 'Ativar Turbo'}</button>
            </div>
          </div>
        </div>
      </Card>

      <h2 className="sec"><Icon name="zap" size={16} />Planos de energia</h2>
      {!plans ? <div className="empty"><Spinner /></div> : (
        <Stagger className="grid g3">
          {plans.map(p => (
            <Card key={p.id} spotlight color={p.active ? 'rgba(139,92,246,.3)' : undefined} style={{ borderColor: p.active ? 'rgba(139,92,246,.45)' : undefined }}>
              <div className="row" style={{ marginBottom: 10 }}>
                <span style={{ width: 34, height: 34, borderRadius: 10, display: 'grid', placeItems: 'center', background: p.active ? 'rgba(139,92,246,.2)' : 'var(--surface-3)', color: p.active ? '#c4b5fd' : 'var(--text-2)' }}><Icon name={icon[p.kind] ?? 'settings'} size={17} /></span>
                <b className="ellipsis">{p.name}</b>
              </div>
              <div className="small dim" style={{ minHeight: 36 }}>
                {p.kind === 'balanced' && 'Recomendado pela Microsoft: equilibra desempenho e consumo.'}
                {p.kind === 'high' && 'CPU sempre pronta para picos. Mais consumo de energia.'}
                {p.kind === 'ultimate' && 'Elimina micro-latências de energia. Ideal para desktops e jogos.'}
                {p.kind === 'saver' && 'Reduz desempenho para economizar bateria.'}
                {p.kind === 'custom' && 'Plano personalizado.'}
              </div>
              <button className={`btn sm ${p.active ? '' : 'primary'}`} style={{ marginTop: 12, width: '100%' }} disabled={p.active} onClick={() => activate(p)}>{p.active ? <><Icon name="check" size={14} />Ativo</> : 'Ativar'}</button>
            </Card>
          ))}
          {!plans.some(p => p.kind === 'ultimate') && (
            <Card style={{ borderStyle: 'dashed', display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
              <b><Icon name="rocket" size={15} /> Desempenho Máximo</b>
              <div className="small dim" style={{ margin: '6px 0 12px' }}>Plano oculto do Windows (Ultimate Performance).</div>
              <button className="btn sm" onClick={unlock}>Desbloquear</button>
            </Card>
          )}
        </Stagger>
      )}

      <h2 className="sec"><Icon name="power" size={16} />Hibernação</h2>
      <Card>
        <div className="row">
          <div style={{ flex: 1 }}><b>Hibernação e Inicialização Rápida</b><div className="small muted">Desativar apaga o arquivo hiberfil.sys (geralmente vários GB na unidade C:) e desliga a Inicialização Rápida. Não recomendado em notebooks que você hiberna.</div></div>
          {!app?.admin && <AdminLock />}
          {hib === null ? <Spinner /> : <Toggle on={hib} onChange={setHibernation} disabled={!app?.admin} />}
        </div>
      </Card>
    </>
  );
}

type Prio = { exe: string; level: 'idle' | 'below' | 'normal' | 'above' | 'high' };
const PRIO_LABEL = { idle: 'Baixa', below: 'Abaixo do normal', normal: 'Normal', above: 'Acima do normal', high: 'Alta' } as const;

function PriorityTab() {
  const admin = useStore(appStore, a => a?.admin);
  const [list, setList] = useState<Prio[] | null>(null);
  const [exe, setExe] = useState('');
  const [level, setLevel] = useState<Prio['level']>('above');
  useEffect(() => { call<Prio[]>('prio.list').then(setList).catch(() => setList([])); }, []);
  async function set(e: string, l: string) { const r = await run(call<Prio[]>('prio.set', { exe: e, level: l }), l === 'remove' ? 'Prioridade removida' : `${e}: prioridade ${PRIO_LABEL[l as Prio['level']]}`); if (r) setList(r); }
  async function pick() { const p = await call<string>('shell.pickExe'); if (p) setExe(p.split(/[\\/]/).pop() ?? p); }
  return (
    <>
      <Card style={{ marginBottom: 16 }}>
        <div className="row wrap">
          <div style={{ flex: 1, minWidth: 280 }}><b>Prioridade fixa por programa</b><div className="small muted">O Windows aplica a prioridade de CPU toda vez que o programa abre — sem o GROBS precisar estar aberto. "Alta" também sobe a prioridade de disco. Evite "Alta" em programas que ficam abertos o tempo todo.</div></div>
          {!admin && <AdminLock />}
        </div>
        <div className="row wrap" style={{ marginTop: 12 }}>
          <input type="text" placeholder="programa.exe" value={exe} onChange={e => setExe(e.target.value)} style={{ width: 220 }} disabled={!admin} />
          <button className="btn sm ghost" onClick={pick} disabled={!admin}><Icon name="folder" size={13} />Escolher…</button>
          <select value={level} onChange={e => setLevel(e.target.value as Prio['level'])} disabled={!admin}>{(Object.keys(PRIO_LABEL) as Prio['level'][]).map(k => <option key={k} value={k}>{PRIO_LABEL[k]}</option>)}</select>
          <button className="btn sm primary" disabled={!admin || !/\.exe$/i.test(exe.trim())} onClick={() => { set(exe.trim(), level); setExe(''); }}><Icon name="check" size={13} />Aplicar</button>
        </div>
      </Card>
      <Card className="pad-0">
        {!list ? <div className="empty"><Spinner /></div> : list.length === 0 ? <div className="empty">Nenhum programa com prioridade fixa. Jogos otimizados na aba Gamer aparecem aqui.</div> : (
          <div className="list">
            {list.map(p => (
              <div key={p.exe} className="li">
                <Icon name="cpu" size={16} className="muted" />
                <div className="grow t mono" style={{ fontSize: 13 }}>{p.exe}</div>
                <select value={p.level} onChange={e => set(p.exe, e.target.value)} disabled={!admin}>{(Object.keys(PRIO_LABEL) as Prio['level'][]).map(k => <option key={k} value={k}>{PRIO_LABEL[k]}</option>)}</select>
                <button className="btn sm ghost iconbtn" title="Remover" onClick={() => set(p.exe, 'remove')} disabled={!admin}><Icon name="x" size={14} /></button>
              </div>
            ))}
          </div>
        )}
      </Card>
    </>
  );
}

export default function Optimize() {
  const [tab, setTab] = useState<'startup' | 'tweaks' | 'power' | 'prio'>('startup');
  return (
    <div className="page">
      <PageHead title="Otimização" sub="Ajustes reais e reversíveis — inicialização, Windows e energia."
        right={<Tabs layoutId="opttab" value={tab} onChange={setTab} items={[{ id: 'startup', label: 'Inicialização' }, { id: 'tweaks', label: 'Ajustes do Windows' }, { id: 'power', label: 'Energia e Turbo' }, { id: 'prio', label: 'Prioridade' }]} />} />
      {tab === 'startup' && <StartupTab />}
      {tab === 'tweaks' && <TweaksTab />}
      {tab === 'power' && <PowerTab />}
      {tab === 'prio' && <PriorityTab />}
    </div>
  );
}
