import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import { Card, CardTitle, Check, Modal, PageHead, Spinner, Toggle } from '../components/ui';
import { appStore, run, statsStore, useStore } from '../store';
import { temp } from '../format';

type Clock = { offset: number; min: number; max: number; base: number };
type St = { supported: boolean; reason?: string; name?: string; editable?: boolean; core?: Clock; mem?: Clock; power?: { min: number; def: number; max: number; cur: number }; fan?: { count: number; manual: boolean; level: number; rpm: number }; admin: boolean };

function Slider({ label, value, min, max, unit, onChange, step = 5 }: { label: string; value: number; min: number; max: number; unit: string; onChange: (v: number) => void; step?: number }) {
  return (
    <div style={{ marginBottom: 16 }}>
      <div className="row small" style={{ marginBottom: 6 }}><span className="muted">{label}</span><span className="spacer" /><b className="mono" style={{ fontSize: 14 }}>{value > 0 && unit !== '%' ? '+' : ''}{value} {unit}</b></div>
      <input type="range" min={min} max={max} step={step} value={value} onChange={e => onChange(+e.target.value)} style={{ width: '100%', accentColor: '#8b5cf6' }} />
      <div className="row small dim"><span>{min}</span><span className="spacer" /><span>{max}</span></div>
    </div>
  );
}

export default function Overclock() {
  const [st, setSt] = useState<St | null>(null);
  const [agree, setAgree] = useState(false);
  const [core, setCore] = useState(0), [mem, setMem] = useState(0), [power, setPower] = useState(100), [fan, setFan] = useState(-1);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const admin = useStore(appStore, a => a?.admin);
  const gpuTemp = useStore(statsStore, s => s?.gpuTemp ?? -1);
  const unit = useStore(appStore, a => a?.settings.tempUnit ?? 'C');

  const load = () => call<St>('oc.status').then(s => {
    setSt(s);
    if (s.core) setCore(s.core.offset); if (s.mem) setMem(s.mem.offset); if (s.power) setPower(s.power.cur);
    if (s.fan) setFan(s.fan.manual ? s.fan.level : -1);
  }).catch(() => setSt({ supported: false, admin: false, reason: 'Falha ao consultar a GPU' }));
  useEffect(() => { load(); }, []);

  async function apply() {
    setConfirm(false); setBusy('apply');
    if (st?.core || st?.mem) await run(call('oc.clocks', { core, mem }));
    if (st?.power) await run(call('oc.power', { percent: power }));
    if (st?.fan) await run(call('oc.fan', { level: fan }));
    setBusy(null); load();
  }
  async function reset() { setBusy('reset'); await run(call('oc.reset'), 'Valores de fábrica restaurados'); setBusy(null); load(); }

  return (
    <div className="page">
      <PageHead title="Overclock da GPU" sub="Aumente o clock, o limite de energia e controle a ventoinha da placa de vídeo." />
      <Card style={{ marginBottom: 16, borderColor: 'rgba(248,113,113,.35)', background: 'rgba(248,113,113,.05)' }}>
        <div className="row" style={{ alignItems: 'flex-start', gap: 14 }}>
          <Icon name="alert" size={22} className="bad-t" />
          <div style={{ flex: 1 }}>
            <b className="bad-t">Cuidado: overclock é por sua conta e risco</b>
            <ul className="small muted" style={{ margin: '6px 0 0', paddingLeft: 18 }}>
              <li>Pode causar travamentos, artefatos na tela, telas azuis e mais calor/consumo. Suba de +15 a +25 MHz por vez e teste.</li>
              <li>Os limites vêm do próprio driver: o GROBS nunca passa do que a placa permite. Os valores voltam ao padrão ao reiniciar o PC.</li>
              <li>Overclock de <b>CPU</b> pelo Windows não é suportado de forma segura na maioria dos PCs: use a BIOS, o Intel XTU ou o AMD Ryzen Master.</li>
              <li>Disponível para GPUs NVIDIA. Para AMD, use o painel do Radeon Software (Adrenalin) → Desempenho → Ajuste.</li>
            </ul>
            <label className="row small" style={{ marginTop: 10, cursor: 'pointer' }}><Check on={agree} onChange={setAgree} />Entendi os riscos e quero liberar os controles</label>
          </div>
        </div>
      </Card>

      {!st ? <div className="empty"><Spinner size={24} /></div> : !st.supported ? (
        <Card><div className="row"><Icon name="info" size={18} className="dim" /><div><b>Overclock indisponível neste PC</b><div className="small muted">{st.reason}</div></div></div></Card>
      ) : (
        <div className="grid" style={{ gridTemplateColumns: '1.3fr 1fr', gap: 16, opacity: agree ? 1 : 0.45, pointerEvents: agree ? 'auto' : 'none', transition: 'opacity .3s' }}>
          <Card>
            <CardTitle icon="zap" color="#76b900" right={<span className="chip">{st.name}</span>}>Ajustes</CardTitle>
            {!admin && <div className="small warn-t" style={{ marginBottom: 12 }}><Icon name="lock" size={12} /> Aplicar exige administrador.</div>}
            {st.editable === false && <div className="small warn-t" style={{ marginBottom: 12 }}>O driver desta GPU bloqueia ajuste de clock (comum em notebooks).</div>}
            {st.core && <Slider label="Clock do núcleo (offset)" value={core} min={st.core.min} max={st.core.max} unit="MHz" onChange={setCore} />}
            {st.mem && <Slider label="Clock da memória (offset)" value={mem} min={st.mem.min} max={st.mem.max} unit="MHz" step={10} onChange={setMem} />}
            {st.power && <Slider label="Limite de energia" value={power} min={st.power.min} max={st.power.max} unit="%" step={1} onChange={setPower} />}
            {st.fan && (
              <div style={{ marginBottom: 6 }}>
                <div className="row small" style={{ marginBottom: 6 }}><span className="muted">Ventoinha</span><span className="spacer" /><span className="small dim">Automática</span><Toggle on={fan < 0} onChange={v => setFan(v ? -1 : 60)} /></div>
                {fan >= 0 && <Slider label="Velocidade manual" value={fan} min={30} max={100} unit="%" onChange={setFan} />}
              </div>
            )}
            <div className="row" style={{ marginTop: 8 }}>
              <button className="btn" onClick={reset} disabled={!admin || !!busy}>{busy === 'reset' ? <Spinner size={13} /> : <Icon name="restore" size={14} />}Restaurar padrão</button>
              <span className="spacer" />
              <button className="btn primary" onClick={() => setConfirm(true)} disabled={!admin || !!busy}>{busy === 'apply' ? <Spinner size={13} /> : <Icon name="check" size={14} />}Aplicar</button>
            </div>
          </Card>
          <Card>
            <CardTitle icon="thermo" color="#fb7185">Agora</CardTitle>
            <dl className="kv">
              <dt>Temperatura</dt><dd className={gpuTemp > 83 ? 'bad-t' : gpuTemp > 75 ? 'warn-t' : ''}>{temp(gpuTemp, unit)}</dd>
              {st.core && <><dt>Clock base (P0)</dt><dd>{st.core.base} MHz</dd><dt>Offset atual</dt><dd>{st.core.offset} MHz</dd></>}
              {st.mem && <><dt>Offset memória</dt><dd>{st.mem.offset} MHz</dd></>}
              {st.power && <><dt>Limite de energia</dt><dd>{st.power.cur}% (padrão {st.power.def}%)</dd></>}
              {st.fan && <><dt>Ventoinha</dt><dd>{st.fan.rpm > 0 ? `${st.fan.rpm} RPM · ` : ''}{st.fan.level}%{st.fan.manual ? ' (manual)' : ' (auto)'}</dd></>}
            </dl>
            <p className="small dim" style={{ marginBottom: 0 }}>Teste a estabilidade com um jogo pesado ou benchmark por 15–30 min e acompanhe a temperatura no Overlay de FPS.</p>
          </Card>
        </div>
      )}
      <Modal open={confirm} onClose={() => setConfirm(false)}>
        <h3>Aplicar overclock?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Núcleo {core > 0 ? '+' : ''}{core} MHz · Memória {mem > 0 ? '+' : ''}{mem} MHz{st?.power ? ` · Energia ${power}%` : ''}{st?.fan ? ` · Ventoinha ${fan < 0 ? 'auto' : fan + '%'}` : ''}. Se o PC travar, reinicie: tudo volta ao padrão.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(false)}>Cancelar</button><button className="btn primary" onClick={apply}>Aplicar</button></div>
      </Modal>
    </div>
  );
}
