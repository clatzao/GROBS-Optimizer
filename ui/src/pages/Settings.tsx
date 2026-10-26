import { call } from '../bridge';
import { Icon } from '../components/Icon';
import GradientText from '../components/rb/GradientText';
import { Card, CardTitle, PageHead, Toggle } from '../components/ui';
import ModulesCard from '../components/ModulesCard';
import { appStore, run, setSetting, toast, useStore } from '../store';

export default function SettingsPage() {
  const app = useStore(appStore);
  if (!app) return null;
  const s = app.settings;

  async function elevate() {
    const ok = await run(call<boolean>('app.elevate'));
    if (ok === false) toast('Elevação cancelada', 'info');
  }

  const seg = <T extends string>(value: T, items: { id: T; label: string }[], onChange: (v: T) => void) => (
    <div className="tabs">
      {items.map(i => <button key={i.id} className={`tab ${value === i.id ? 'active' : ''}`} onClick={() => onChange(i.id)}>{value === i.id && <i className="tabbg" />}<span>{i.label}</span></button>)}
    </div>
  );

  const row = (title: string, desc: string, control: React.ReactNode) => (
    <div className="li" style={{ padding: '14px 0' }}>
      <div className="grow"><div className="t">{title}</div><div className="s">{desc}</div></div>
      {control}
    </div>
  );

  return (
    <div className="page">
      <PageHead title="Configurações" />
      <div className="grid g2">
        <Card>
          <CardTitle icon="sparkles" color="#a78bfa">Aparência e desempenho</CardTitle>
          <div className="list">
            {row('Animações', 'Reduza em PCs mais fracos. "Desligadas" remove todos os efeitos.', seg(s.animations, [{ id: 'full', label: 'Completas' }, { id: 'reduced', label: 'Reduzidas' }, { id: 'off', label: 'Desligadas' }], v => setSetting('animations', v)))}
            {row('Atualização do monitor', 'Intervalos maiores usam ainda menos CPU.', seg(String(s.interval) as any, [{ id: '1000', label: '1 s' }, { id: '2000', label: '2 s' }, { id: '5000', label: '5 s' }], v => setSetting('interval', Number(v))))}
            {row('Unidade de temperatura', '', seg(s.tempUnit, [{ id: 'C', label: '°C' }, { id: 'F', label: '°F' }], v => setSetting('tempUnit', v)))}
          </div>
        </Card>
        <Card>
          <CardTitle icon="settings" color="#22d3ee">Comportamento</CardTitle>
          <div className="list">
            {row('Iniciar com o Windows', 'Abre minimizado na bandeja, sem ocupar a tela.', <Toggle on={s.startWithWindows} onChange={v => setSetting('startWithWindows', v)} />)}
            {row('Fechar para a bandeja', 'O botão X esconde o app em vez de encerrar. Oculto, ele fica suspenso e não usa CPU.', <Toggle on={s.trayOnClose} onChange={v => setSetting('trayOnClose', v)} />)}
            {row('Atalho Ctrl+Alt+F4 = finalizar à força', 'Fecha na hora o app em primeiro plano, mesmo travado (como o SuperF4).', <Toggle on={!!(s as any).killHotkey} onChange={v => setSetting('killHotkey' as any, v as any)} />)}
            {row('Sensores completos ao abrir', 'Lê a temperatura real da CPU no Painel (modo admin). Desligue para economizar ~15 MB de RAM.', <Toggle on={(s as any).sensorsAuto !== false} onChange={v => setSetting('sensorsAuto' as any, v as any)} />)}
            {row('Privilégios de administrador', app.admin ? 'Ativo: todos os recursos liberados.' : 'Necessário para limpar pastas do Windows, ajustes de sistema e S.M.A.R.T.',
              app.admin ? <span className="chip ok"><Icon name="shieldCheck" size={12} />Ativo</span> : <button className="btn sm primary" onClick={elevate}><Icon name="shield" size={14} />Executar como admin</button>)}
          </div>
        </Card>
      </div>

      <ModulesCard />
      <Card style={{ marginTop: 16, textAlign: 'center', padding: 28 }}>
        <div style={{ font: '700 26px var(--font-display)' }}><GradientText colors={['#a78bfa', '#22d3ee', '#f472b6', '#a78bfa']} animationSpeed={8}>GROBS Optimizer</GradientText></div>
        <div className="muted" style={{ margin: '6px 0 14px' }}>Versão {app.version} · {app.proc64 ? '64 bits (x64)' : '32 bits (x86)'} · Windows {app.os64 ? '64 bits' : '32 bits'}</div>
        <div className="small dim" style={{ maxWidth: 640, margin: '0 auto' }}>
          Sem telemetria, sem anúncios, sem limpeza de registro e sem "turbinadores" de RAM. Tudo roda localmente.
          Interface com componentes de React Bits, Magic UI (21st.dev) e Uiverse.io.
        </div>
        <button className="btn sm ghost" style={{ marginTop: 12 }} onClick={() => call('app.openLog')}><Icon name="list" size={13} />Abrir log de erros</button>
      </Card>
    </div>
  );
}
