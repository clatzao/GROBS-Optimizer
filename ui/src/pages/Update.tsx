import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import { Beam, Card, CardTitle, Modal, PageHead, Spinner } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, toast, useStore } from '../store';

type St = { pausedUntil: string | null; disabled: boolean; deferFeature: number; deferQuality: number; cache: number; scheduled: { next: string } | null; canPause: boolean; admin: boolean };

export default function Update() {
  const [st, setSt] = useState<St | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const [feature, setFeature] = useState(0);
  const [quality, setQuality] = useState(0);
  const admin = useStore(appStore, a => a?.admin);

  const load = () => call<St>('wu.status').then(s => { setSt(s); setFeature(s.deferFeature); setQuality(s.deferQuality); }).catch(() => {});
  useEffect(() => { load(); }, []);

  async function act(id: string, m: string, p?: any, ok?: string) {
    setBusy(id);
    const r = await run(call<any>(m, p), ok);
    setBusy(null);
    if (r && r.pausedUntil !== undefined) setSt(r); else load();
    return r;
  }
  async function clean() {
    setBusy('clean');
    const r = await run(call<any>('wu.clean'));
    setBusy(null);
    if (r) toast(`${bytes(r.freed)} liberados do cache do Windows Update`);
    load();
  }

  if (!st) return <div className="page"><div className="empty"><Spinner size={24} /></div></div>;
  const state = st.disabled ? 'off' : st.pausedUntil ? 'paused' : 'on';
  const color = state === 'off' ? '#f87171' : state === 'paused' ? '#fbbf24' : '#34d399';

  return (
    <div className="page">
      <PageHead title="Windows Update" sub="Controle quando o Windows atualiza — e limpe o cache de atualizações já instaladas."
        right={!admin && <span className="chip warn"><Icon name="lock" size={12} />Requer administrador</span>} />

      <Card style={{ padding: 24, marginBottom: 16, overflow: 'hidden', borderColor: `${color}55` }}>
        {busy && <Beam size={180} duration={2.5} c1={color} c2="#22d3ee" />}
        <div className="row" style={{ gap: 20 }}>
          <div style={{ width: 64, height: 64, borderRadius: 18, display: 'grid', placeItems: 'center', background: `${color}22`, color }}><Icon name={state === 'off' ? 'stop' : state === 'paused' ? 'clock' : 'refresh'} size={30} /></div>
          <div style={{ flex: 1 }}>
            <div className="stat-big" style={{ fontSize: 24 }}>{state === 'off' ? 'Atualizações desativadas' : state === 'paused' ? `Pausado até ${st.pausedUntil}` : 'Atualizações ativas'}</div>
            <div className="muted small" style={{ marginTop: 4 }}>
              {state === 'off' ? 'Seu PC não recebe correções de segurança. Reative assim que possível.' : state === 'paused' ? 'O Windows volta a atualizar sozinho na data acima.' : 'O Windows baixa e instala atualizações automaticamente.'}
            </div>
          </div>
          {state === 'off'
            ? <button className="btn primary" disabled={!admin || !!busy} onClick={() => act('enable', 'wu.enable', undefined, 'Windows Update reativado')}><Icon name="play" size={14} />Reativar</button>
            : state === 'paused' ? <button className="btn" disabled={!admin || !!busy} onClick={() => act('resume', 'wu.pause', { days: 0 }, 'Pausa removida')}>Retomar agora</button> : null}
        </div>
      </Card>

      <div className="grid g2">
        <Card>
          <CardTitle icon="clock" color="#fbbf24">Pausar</CardTitle>
          <p className="small muted" style={{ marginTop: 0 }}>Forma recomendada: o Windows segue seguro e só atualiza depois.</p>
          {!st.canPause ? <div className="small dim">Disponível no Windows 10 e 11.</div> : (
            <div className="row wrap">
              {[7, 14, 21, 35].map(d => <button key={d} className="btn sm" disabled={!admin || !!busy} onClick={() => act('p' + d, 'wu.pause', { days: d }, `Atualizações pausadas por ${d} dias`)}>{busy === 'p' + d ? <Spinner size={12} /> : null}{d} dias</button>)}
            </div>
          )}
          <h4 style={{ margin: '18px 0 8px', fontSize: 13 }}>Adiar (política)</h4>
          <div className="grid g2" style={{ gap: 10 }}>
            <label className="small muted">Atualizações de recursos (versões novas)
              <select value={feature} onChange={e => setFeature(+e.target.value)} style={{ width: '100%', marginTop: 4 }} disabled={!admin}>
                {[0, 30, 90, 180, 365].map(d => <option key={d} value={d}>{d === 0 ? 'Não adiar' : `${d} dias`}</option>)}
              </select>
            </label>
            <label className="small muted">Atualizações de qualidade (mensais)
              <select value={quality} onChange={e => setQuality(+e.target.value)} style={{ width: '100%', marginTop: 4 }} disabled={!admin}>
                {[0, 7, 14, 30].map(d => <option key={d} value={d}>{d === 0 ? 'Não adiar' : `${d} dias`}</option>)}
              </select>
            </label>
          </div>
          <button className="btn sm" style={{ marginTop: 10 }} disabled={!admin || !!busy} onClick={() => act('defer', 'wu.defer', { feature, quality }, 'Adiamento salvo')}>Salvar adiamento</button>
        </Card>

        <Card>
          <CardTitle icon="broom" color="#22d3ee">Cache de atualizações</CardTitle>
          <div className="row" style={{ marginBottom: 12 }}>
            <div style={{ flex: 1 }}><div className="stat-big" style={{ fontSize: 26 }}>{bytes(st.cache)}</div><div className="small dim">em SoftwareDistribution\Download</div></div>
            <button className="btn primary" disabled={!admin || !!busy} onClick={clean}>{busy === 'clean' ? <Spinner size={14} /> : <Icon name="broom" size={14} />}Limpar agora</button>
          </div>
          <div className="small muted" style={{ marginBottom: 8 }}>Limpeza automática agendada (roda mesmo com o app fechado):</div>
          <div className="row wrap">
            {[{ id: 'off', l: 'Desligada' }, { id: 'daily', l: 'Diária' }, { id: 'weekly', l: 'Semanal' }, { id: 'monthly', l: 'Mensal' }].map(f => (
              <button key={f.id} className="btn sm" disabled={!admin || !!busy} onClick={() => act('sch', 'wu.schedule', { freq: f.id }, f.id === 'off' ? 'Agendamento removido' : `Limpeza ${f.l.toLowerCase()} agendada`)}>{f.l}</button>
            ))}
          </div>
          <div className="small dim" style={{ marginTop: 8 }}>{st.scheduled ? <><Icon name="check" size={12} className="ok-t" /> Agendada · próxima execução: {st.scheduled.next || '—'}</> : 'Nenhuma limpeza agendada.'}</div>
        </Card>
      </div>

      <Card style={{ marginTop: 16, borderColor: 'rgba(248,113,113,.25)' }}>
        <div className="row">
          <Icon name="alert" size={20} className="bad-t" />
          <div style={{ flex: 1 }}><b>Desativar totalmente</b><div className="small muted">Desliga os serviços e as tarefas do Windows Update. Não recomendado: o PC fica sem correções de segurança. Reversível pelo botão “Reativar”.</div></div>
          <button className="btn danger" disabled={!admin || !!busy || st.disabled} onClick={() => setConfirm(true)}>Desativar</button>
        </div>
      </Card>

      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Desativar o Windows Update?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Você deixará de receber correções de segurança e de drivers. Prefira <b>pausar</b> se o objetivo é evitar reinícios durante jogos ou trabalho.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button>
          <button className="btn danger" onClick={() => { setConfirm(false); act('disable', 'wu.disable', undefined, 'Windows Update desativado'); }}>Desativar mesmo assim</button></div>
      </Modal>
    </div>
  );
}
