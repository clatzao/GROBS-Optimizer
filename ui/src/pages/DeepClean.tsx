import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import AiSettings, { AiCfg } from '../components/AiSettings';
import { Bar, Card, CardTitle, Check, Modal, PageHead, Spinner } from '../components/ui';
import { bytes } from '../format';
import { run, toast } from '../store';

type Item = { id: string; title: string; why: string; risk: 'safe' | 'review'; kind: string; path: string; size: number; ageDays: number; inUse: boolean };
type Res = { items: Item[]; date: string; free: number; total: number; admin: boolean; freed?: number; recycled?: number; failed?: number; done?: number; errors?: string[] };
type St = { running: boolean; phase: string; error: string | null; result: Res | null };
type AiRes = { summary: string; order: string[]; notes: Record<string, string>; warn: string[]; provider: string; model: string };

export default function DeepClean() {
  const [st, setSt] = useState<St>({ running: false, phase: '', error: null, result: null });
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [ai, setAi] = useState<AiRes | null>(null);
  const [aiBusy, setAiBusy] = useState(false);
  const [cfg, setCfg] = useState<AiCfg | null>(null);
  const [showCfg, setShowCfg] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<Res | null>(null);

  // começa com o que é seguro e não está em uso; o resto só com a sua marcação
  const preselect = (r: Res | null) => setSel(new Set((r?.items ?? []).filter(i => i.risk === 'safe' && !i.inUse).map(i => i.id)));
  useEffect(() => {
    call<St>('dclean.status').then(s => { setSt(s); preselect(s.result); }).catch(() => {});
    call<AiCfg>('ai.config').then(setCfg).catch(() => {});
    return on('diskClean', (s: St) => { setSt(s); if (!s.running) { preselect(s.result); if (s.error) toast(s.error, 'err'); } });
  }, []);

  async function scan() { setAi(null); setDone(null); const r = await run(call<St>('dclean.start')); if (r) setSt(r); }
  async function askAi() {
    if (!cfg) return;
    const p = cfg.providers.find(x => x.id === cfg.provider);
    if ((!p?.local && !cfg.consent) || (p?.needsKey && !p.hasKey)) { setShowCfg(true); return; }
    setAiBusy(true);
    const r = await run(call<AiRes>('ai.cleanup'));
    setAiBusy(false);
    if (r) setAi(r);
  }
  async function del() {
    setConfirm(false); setBusy(true);
    const r = await run(call<Res>('dclean.delete', { ids: [...sel] }));
    setBusy(false);
    if (!r) return;
    setDone(r); setSel(new Set()); setAi(null);
    setSt(s => ({ ...s, result: r }));
    toast(`${bytes(r.freed ?? 0)} liberados`);
  }

  const r = st.result;
  const items = useMemo(() => {
    const list = [...(r?.items ?? [])];
    if (ai?.order.length) list.sort((a, b) => { const x = ai.order.indexOf(a.id), y = ai.order.indexOf(b.id); return (x < 0 ? 999 : x) - (y < 0 ? 999 : y); });
    return list;
  }, [r, ai]);
  const chosen = items.filter(i => sel.has(i.id));
  const total = chosen.reduce((a, i) => a + i.size, 0);
  const toggle = (id: string, v: boolean) => setSel(s => { const n = new Set(s); v ? n.add(id) : n.delete(id); return n; });
  const row = (i: Item) => (
    <label key={i.id} className="li" style={{ alignItems: 'flex-start', cursor: 'pointer' }}>
      <Check on={sel.has(i.id)} onChange={v => toggle(i.id, v)} />
      <div className="grow">
        <div className="row wrap" style={{ gap: 8 }}>
          <span className="t">{i.title}</span>
          {i.inUse && <span className="chip warn" title="Apagar com o programa aberto pode atrapalhar o que ele está fazendo">programa aberto</span>}
          {ai?.order.includes(i.id) && <span className="chip accent"><Icon name="sparkles" size={10} />IA: nº {ai.order.indexOf(i.id) + 1}</span>}
          {ai?.warn.includes(i.id) && <span className="chip bad"><Icon name="alert" size={10} />IA: cuidado</span>}
        </div>
        <div className="s selectable" style={{ marginTop: 3 }}>{i.why}</div>
        {i.path && i.kind !== 'clean' && <div className="small mono dim ellipsis selectable" style={{ marginTop: 2 }} title={i.path}>{i.path}</div>}
        {ai?.notes[i.id] && <div className="small selectable" style={{ marginTop: 4, color: '#c4b5fd' }}>{ai.notes[i.id]}</div>}
      </div>
      <b className="small" style={{ flex: 'none' }}>{bytes(i.size)}</b>
      {i.path && i.kind !== 'clean' && <button className="btn sm ghost iconbtn" style={{ flex: 'none' }} title="Mostrar na pasta" onClick={e => { e.preventDefault(); call('shell.reveal', { path: i.path }); }}><Icon name="external" size={12} /></button>}
    </label>
  );
  const usedPct = r ? (1 - r.free / r.total) * 100 : 0;
  const safe = items.filter(i => i.risk === 'safe'), review = items.filter(i => i.risk === 'review');
  return (
    <div className="page">
      <PageHead title="Limpeza com IA" sub="Acha o que está ocupando o disco e pode sair. A IA explica e prioriza; só é apagado o que você marcar."
        right={<button className="btn primary lg" onClick={scan} disabled={st.running || busy}>{st.running ? <Spinner size={16} /> : <Icon name="search" size={16} />}{r ? 'Procurar de novo' : 'Procurar o que limpar'}</button>} />

      {st.running && <Card style={{ marginBottom: 16 }}><div className="row"><Spinner color="var(--accent)" /><b>{st.phase || 'Preparando…'}</b></div></Card>}
      {!r && !st.running && <Card><div className="empty"><Icon name="disk" size={30} /><div style={{ marginTop: 10 }}>Procura caches de programas e ferramentas de desenvolvimento, projetos parados, arquivos grandes esquecidos, Lixeira e hibernação. Só lê o disco: nada é apagado nessa etapa.</div></div></Card>}

      {r && <>
        <Card style={{ marginBottom: 16 }}>
          <div className="row"><b style={{ flex: 1 }}>Disco do sistema</b><span className="small muted">{bytes(r.free)} livres de {bytes(r.total)}</span></div>
          <div style={{ marginTop: 8 }}><Bar value={usedPct} kind={usedPct > 90 ? 'bad' : usedPct > 80 ? 'warn' : undefined} /></div>
          {done && <div className="small ok-t" style={{ marginTop: 8 }}><Icon name="check" size={12} /> {bytes(done.freed ?? 0)} liberados.{(done.recycled ?? 0) > 0 ? ` ${bytes(done.recycled ?? 0)} foram para a Lixeira (só liberam espaço depois de esvaziá-la).` : ''}{(done.failed ?? 0) > 0 ? ` ${done.failed} arquivo(s) em uso ficaram.` : ''}</div>}
          {done?.errors?.map(e => <div key={e} className="small bad-t" style={{ marginTop: 4 }}>{e}</div>)}
        </Card>

        <Card style={{ marginBottom: 16, borderColor: 'rgba(139,92,246,.35)' }}>
          <CardTitle icon="sparkles" color="#a78bfa" right={<>
            <button className="btn sm ghost" onClick={() => setShowCfg(true)}><Icon name="settings" size={13} />{cfg?.providers.find(p => p.id === cfg.provider)?.title.split(' (')[0] ?? 'IA'}</button>
            <button className="btn sm primary" onClick={askAi} disabled={aiBusy || items.length === 0}>{aiBusy ? <Spinner size={13} /> : <Icon name="sparkles" size={13} />}{ai ? 'Perguntar de novo' : 'Pedir opinião da IA'}</button>
          </>}>Opinião da IA (grátis)</CardTitle>
          {aiBusy && <div className="small muted"><Spinner size={12} /> A IA está lendo a lista…</div>}
          {!ai && !aiBusy && <div className="small muted">Opcional. A IA recebe só o tipo, o tamanho e a idade de cada item, sem nomes de arquivos nem caminhos. Ela ordena e explica, mas não marca nem apaga nada.</div>}
          {ai && !aiBusy && <>
            <p style={{ margin: '0 0 8px' }} className="selectable">{ai.summary}</p>
            <div className="small dim">{ai.provider} · {ai.model}. IA pode errar: leia a descrição de cada item antes de marcar.</div>
          </>}
        </Card>

        <div className="row" style={{ marginBottom: 10 }}>
          <b>{chosen.length} marcado(s) · {bytes(total)}</b>
          <span className="spacer" />
          <button className="btn sm ghost" onClick={() => setSel(new Set())} disabled={sel.size === 0}>Desmarcar tudo</button>
          <button className="btn sm danger" onClick={() => setConfirm(true)} disabled={sel.size === 0 || busy}>{busy ? <Spinner size={13} /> : <Icon name="trash" size={14} />}Apagar marcados</button>
        </div>

        {items.length === 0 ? <Card><div className="empty">Nada relevante para limpar.</div></Card> : <>
          {safe.length > 0 && <Card className="pad-0" style={{ marginBottom: 16 }}>
            <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}><Icon name="shieldCheck" size={16} style={{ color: 'var(--ok)' }} /><b>Pode sair: se recria ou baixa de novo</b><span className="spacer" /><span className="small dim">{bytes(safe.reduce((a, i) => a + i.size, 0))}</span></div>
            <div className="list">{safe.map(row)}</div>
          </Card>}
          {review.length > 0 && <Card className="pad-0">
            <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}><Icon name="alert" size={16} style={{ color: 'var(--warn)' }} /><b>Confira antes: pode ser importante para você</b><span className="spacer" /><span className="small dim">{bytes(review.reduce((a, i) => a + i.size, 0))}</span></div>
            <div className="list">{review.map(row)}</div>
          </Card>}
        </>}
      </>}

      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Apagar {chosen.length} item(ns)?</h3>
        <p className="muted" style={{ marginTop: 0 }}>{bytes(total)} no total. Arquivos seus vão para a Lixeira; <b>o resto é apagado definitivamente</b>.</p>
        <div className="list" style={{ border: '1px solid var(--line)', borderRadius: 12, marginBottom: 14, maxHeight: 220, overflow: 'auto' }}>
          {chosen.map(i => <div key={i.id} className="li" style={{ padding: '7px 12px' }}><span className="small grow ellipsis">{i.title}</span>{i.inUse && <span className="chip warn">programa aberto</span>}<b className="small">{bytes(i.size)}</b></div>)}
        </div>
        {chosen.some(i => i.inUse) && <p className="small warn-t" style={{ marginTop: 0 }}>Há programas abertos usando itens marcados. Apagar agora pode fazer eles falharem no meio do trabalho.</p>}
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn danger" onClick={del}><Icon name="trash" size={14} />Apagar</button></div>
      </Modal>
      <AiSettings open={showCfg} onClose={() => setShowCfg(false)} cfg={cfg} setCfg={setCfg} />
    </div>
  );
}
