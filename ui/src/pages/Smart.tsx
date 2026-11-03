import { useEffect, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import AiSettings, { AiCfg } from '../components/AiSettings';
import { AdminLock, Beam, Card, CardTitle, Modal, PageHead, Spinner, Toggle } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, toast, useStore } from '../store';

type Finding = { id: string; impact: 'high' | 'medium' | 'low'; title: string; why: string; evidence: string; kind?: string; label?: string; confirm?: boolean };
type Speed = { spawnMs: number; filesMs: number; ramFree: number; cpu: number; procs: number; date: string };
type Result = { date: string; cpu: number; mem: number; disk: number; pages: number; temp: number; freePct: number; admin: boolean; top: { name: string; cpu: number; ramMB: number; count: number }[];
  boot: { ms?: number; main?: number; post?: number; date?: string }; findings: Finding[]; speed: Speed };
type St = { running: boolean; phase: string; result: Result | null; error: string | null };
type Cfg = AiCfg;
type AiRes = { summary: string; order: string[]; notes: Record<string, string>; tips: string[]; provider: string; model: string };

const IMPACT = { high: ['Prioridade alta', 'high'], medium: ['Prioridade média', 'medium'], low: ['Prioridade baixa', 'low'] } as const;

/** Compara duas medições. Abaixo de 10% é tratado como variação normal: não vendemos ruído como ganho. */
function Compare({ a, b }: { a: Speed; b: Speed }) {
  const row = (name: string, x: number, y: number, unit: string) => {
    const d = ((x - y) / x) * 100;
    const txt = Math.abs(d) < 10 ? 'variação normal' : d > 0 ? `${Math.round(d)}% mais rápido` : `${Math.round(-d)}% mais lento`;
    return <tr key={name}><td>{name}</td><td className="num">{x} {unit}</td><td className="num">{y} {unit}</td><td className={`num ${Math.abs(d) < 10 ? 'dim' : d > 0 ? 'ok-t' : 'bad-t'}`}>{txt}</td></tr>;
  };
  return (
    <table className="table">
      <thead><tr><th>Tarefa real</th><th className="num">Antes</th><th className="num">Agora</th><th className="num">Resultado</th></tr></thead>
      <tbody>
        {row('Abrir um programa simples', a.spawnMs, b.spawnMs, 'ms')}
        {row('Criar, ler e apagar 1000 arquivos', a.filesMs, b.filesMs, 'ms')}
        <tr><td>Memória disponível</td><td className="num">{bytes(a.ramFree)}</td><td className="num">{bytes(b.ramFree)}</td><td className="num dim">{b.ramFree > a.ramFree ? `+${bytes(b.ramFree - a.ramFree)}` : '—'}</td></tr>
      </tbody>
    </table>
  );
}

export default function Smart() {
  const admin = useStore(appStore, a => a?.admin);
  const [st, setSt] = useState<St>({ running: false, phase: '', result: null, error: null });
  const [cfg, setCfg] = useState<Cfg | null>(null);
  const [ai, setAi] = useState<AiRes | null>(null);
  const [aiBusy, setAiBusy] = useState(false);
  const [showCfg, setShowCfg] = useState(false);
  const [done, setDone] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [ask, setAsk] = useState<Finding | null>(null);
  const [after, setAfter] = useState<Speed | null>(null);
  const [resp, setResp] = useState<{ on: boolean; total: number; demoted: string[] } | null>(null);

  const loadResp = () => call<any>('responsive.status').then(setResp).catch(() => {});
  useEffect(() => {
    call<St>('analysis.status').then(setSt).catch(() => {});
    call<Cfg>('ai.config').then(setCfg).catch(() => {});
    loadResp();
    const t = setInterval(loadResp, 4000);
    const off = on('analysis', (s: St) => { setSt(s); if (!s.running && s.error) toast(s.error, 'err'); });
    return () => { clearInterval(t); off(); };
  }, []);

  async function start() { setAi(null); setDone({}); setAfter(null); const r = await run(call<St>('analysis.start', { seconds: 30 })); if (r) setSt(r); }
  async function askAi() {
    if (!cfg) return;
    const p = cfg.providers.find(x => x.id === cfg.provider);
    if (!p?.local && !cfg.consent) { setShowCfg(true); return; }
    if (p?.needsKey && !p.hasKey) { setShowCfg(true); return; }
    setAiBusy(true);
    const r = await run(call<AiRes>('ai.analyze'));
    setAiBusy(false);
    if (r) setAi(r);
  }
  async function apply(f: Finding) {
    setAsk(null); setBusy(f.id);
    const r = await run(call<{ text: string }>('analysis.apply', { id: f.id }));
    setBusy(null);
    if (r) { setDone(d => ({ ...d, [f.id]: r.text })); toast(r.text); loadResp(); }
  }
  async function applyRecommended() {
    if (!st.result) return;
    // fechar programa nunca entra no automático: pode perder trabalho não salvo
    const ids = ai?.order.length ? ai.order : st.result.findings.filter(f => f.impact !== 'low').map(f => f.id);
    for (const id of ids) {
      const f = st.result.findings.find(x => x.id === id);
      if (f?.kind && !f.confirm && !done[f.id]) await apply(f);
    }
  }
  async function prove() { setBusy('prove'); const r = await run(call<Speed>('analysis.measure')); setBusy(null); if (r) setAfter(r); }

  const r = st.result;
  const provider = cfg?.providers.find(p => p.id === cfg.provider);
  const list = r ? [...r.findings].sort((a, b) => {
    const ia = ai?.order.indexOf(a.id) ?? -1, ib = ai?.order.indexOf(b.id) ?? -1;
    if (ia >= 0 && ib >= 0) return ia - ib;
    if (ia >= 0) return -1;
    if (ib >= 0) return 1;
    return 0;
  }) : [];
  const auto = list.filter(f => f.kind && !f.confirm && !done[f.id] && (ai?.order.length ? ai.order.includes(f.id) : f.impact !== 'low')).length;

  return (
    <div className="page">
      <PageHead title="Análise inteligente" sub="Mede onde este PC realmente perde tempo, mostra a evidência, aplica o que ajuda e prova o resultado."
        right={<>{!admin && <AdminLock text="Sem admin a análise é parcial: sem tempo de boot e sem algumas ações" />}<button className="btn primary lg" onClick={start} disabled={st.running}>{st.running ? <Spinner size={16} /> : <Icon name="search" size={16} />}{r ? 'Analisar de novo' : 'Analisar meu PC'}</button></>} />

      <Card style={{ marginBottom: 16 }}>
        <div className="row">
          <span className="qa-ico" style={{ color: '#34d399', background: '#34d3991f' }}><Icon name="zap" size={20} /></span>
          <div style={{ flex: 1 }}><b>Janela em uso sempre rápida</b><div className="small muted">Quando o PC está ocupado, programas em segundo plano perdem prioridade até a carga passar. Não fecha nada e funciona com o GROBS na bandeja.{resp?.on ? ` Ajustou ${resp.total} programa(s) nesta sessão${resp.demoted.length ? `; agora: ${resp.demoted.join(', ')}` : ''}.` : ''}</div></div>
          {resp && <Toggle on={resp.on} onChange={async v => { const x = await run(call<any>('responsive.set', { on: v })); if (x) setResp(x); }} />}
        </div>
      </Card>

      {st.running && <Card style={{ marginBottom: 16 }}><Beam /><div className="row"><Spinner color="var(--accent)" /><b>{st.phase || 'Preparando…'}</b><span className="spacer" /><span className="small dim">Use o PC normalmente: a análise mede o que está acontecendo de verdade.</span></div></Card>}
      {!r && !st.running && <Card><div className="empty"><Icon name="search" size={30} /><div style={{ marginTop: 10 }}>Leva cerca de 1 minuto. Abra o que você costuma usar e clique em <b>Analisar meu PC</b>. Nada é alterado sem você mandar.</div></div></Card>}

      {r && (
        <>
          <div className="grid g4" style={{ marginBottom: 16 }}>
            {[['CPU média', `${r.cpu}%`], ['Memória em uso', `${r.mem}%`], ['Disco ocupado', `${r.disk}%`], ['Último boot', r.boot.ms ? `${Math.round(r.boot.ms / 1000)} s` : '—']].map(([k, v]) => (
              <Card key={k}><div className="small dim">{k}</div><div className="stat-big" style={{ marginTop: 6 }}>{v}</div></Card>
            ))}
          </div>

          <Card style={{ marginBottom: 16, borderColor: 'rgba(139,92,246,.35)' }}>
            <CardTitle icon="sparkles" color="#a78bfa" right={<>
              <button className="btn sm ghost" onClick={() => setShowCfg(true)}><Icon name="settings" size={13} />{provider?.title.split(' (')[0] ?? 'IA'}</button>
              <button className="btn sm primary" onClick={askAi} disabled={aiBusy}>{aiBusy ? <Spinner size={13} /> : <Icon name="sparkles" size={13} />}{ai ? 'Perguntar de novo' : 'Pedir opinião da IA'}</button>
            </>}>Opinião da IA (grátis)</CardTitle>
            {aiBusy && <div className="small muted"><Spinner size={12} /> A IA está lendo as medições…</div>}
            {!ai && !aiBusy && <div className="small muted">Opcional. A IA lê as medições, explica em português o que mais pesa neste PC e escolhe a prioridade. Ela só escolhe entre as ações da lista abaixo: nunca executa nada sozinha.</div>}
            {ai && !aiBusy && <>
              <p style={{ margin: '0 0 8px' }} className="selectable">{ai.summary}</p>
              {ai.tips.length > 0 && <ul className="small muted selectable" style={{ margin: '0 0 8px', paddingLeft: 18 }}>{ai.tips.map((t, i) => <li key={i}>{t}</li>)}</ul>}
              <div className="small dim">{ai.provider} · {ai.model}. IA pode errar: confira a evidência de cada item.</div>
            </>}
          </Card>

          <div className="row" style={{ margin: '0 0 10px' }}>
            <h2 className="sec" style={{ margin: 0 }}><Icon name="list" size={16} />{list.length} achado(s)</h2>
            <span className="spacer" />
            <button className="btn sm" disabled={auto === 0 || !!busy} onClick={applyRecommended} title="Não inclui fechar programas: esses pedem confirmação um a um"><Icon name="zap" size={13} />Aplicar recomendados ({auto})</button>
          </div>
          <Card className="pad-0">
            <div className="list">
              {list.length === 0 && <div className="empty">Nada relevante encontrado: neste momento o PC não tem desperdício que o software possa resolver.</div>}
              {list.map(f => (
                <div key={f.id} className="li" style={{ alignItems: 'flex-start' }}>
                  <div className="grow">
                    <div className="row wrap" style={{ gap: 8 }}><span className="t">{f.title}</span><span className={`risk ${IMPACT[f.impact][1]}`}>{IMPACT[f.impact][0]}</span>{ai?.order.includes(f.id) && <span className="chip accent"><Icon name="sparkles" size={10} />IA: nº {ai.order.indexOf(f.id) + 1}</span>}</div>
                    <div className="s selectable" style={{ marginTop: 3 }}>{f.why}</div>
                    <div className="small mono dim selectable" style={{ marginTop: 3 }}>{f.evidence}</div>
                    {ai?.notes[f.id] && <div className="small selectable" style={{ marginTop: 4, color: '#c4b5fd' }}>{ai.notes[f.id]}</div>}
                    {done[f.id] && <div className="small ok-t" style={{ marginTop: 4 }}><Icon name="check" size={12} /> {done[f.id]}</div>}
                  </div>
                  {f.kind && !done[f.id] && <button className={`btn sm ${f.confirm ? 'danger' : ''}`} style={{ flex: 'none' }} disabled={!!busy} onClick={() => (f.confirm ? setAsk(f) : apply(f))}>{busy === f.id ? <Spinner size={13} /> : null}{f.label}</button>}
                  {!f.kind && <span className="chip" style={{ flex: 'none' }}>só você resolve</span>}
                </div>
              ))}
            </div>
          </Card>

          <h2 className="sec"><Icon name="activity" size={16} />Provar o resultado</h2>
          <Card>
            <div className="row" style={{ marginBottom: after ? 12 : 0 }}>
              <div className="small muted" style={{ flex: 1 }}>Depois de aplicar, meça de novo com o PC no mesmo uso. Diferença abaixo de 10% é variação normal, e o GROBS diz isso. O tempo de boot só muda depois de reiniciar: rode a análise de novo depois.</div>
              <button className="btn sm primary" onClick={prove} disabled={busy === 'prove'}>{busy === 'prove' ? <Spinner size={13} /> : <Icon name="play" size={13} />}Medir agora</button>
            </div>
            {after && <Compare a={r.speed} b={after} />}
          </Card>

          <h2 className="sec"><Icon name="cpu" size={16} />Quem mais pesou durante a análise</h2>
          <Card className="pad-0">
            <table className="table">
              <thead><tr><th>Programa</th><th className="num">CPU média</th><th className="num">RAM</th><th className="num">Processos</th></tr></thead>
              <tbody>{r.top.map(t => <tr key={t.name}><td>{t.name}</td><td className="num">{t.cpu}%</td><td className="num">{bytes(t.ramMB * 1048576)}</td><td className="num">{t.count}</td></tr>)}</tbody>
            </table>
          </Card>
        </>
      )}

      <Modal open={!!ask} onClose={() => setAsk(null)}>
        <h3>{ask?.label}?</h3>
        <p className="muted" style={{ marginTop: 0 }}>O programa fecha na hora, sem perguntar se quer salvar. <b>Salve seu trabalho nele antes.</b></p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setAsk(null)}>Cancelar</button><button className="btn danger" onClick={() => ask && apply(ask)}>Fechar agora</button></div>
      </Modal>

      <AiSettings open={showCfg} onClose={() => setShowCfg(false)} cfg={cfg} setCfg={setCfg} />
    </div>
  );
}
