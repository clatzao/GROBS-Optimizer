import { useEffect, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import GradientText from '../components/rb/GradientText';
import { Bar, Card, CardTitle, PageHead, Spinner, Toggle } from '../components/ui';
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

      <div className="grid g2" style={{ marginTop: 16 }}>
        <UpdateCard />
        <BackupCard />
      </div>
      <ModulesCard />
      <Card style={{ marginTop: 16, textAlign: 'center', padding: 28 }}>
        <div style={{ font: '700 26px var(--font-display)' }}><GradientText colors={['#a78bfa', '#22d3ee', '#f472b6', '#a78bfa']} animationSpeed={8}>GROBS Optimizer</GradientText></div>
        <div className="muted" style={{ margin: '6px 0 14px' }}>Versão {app.version} · {app.proc64 ? '64 bits (x64)' : '32 bits (x86)'} · Windows {app.os64 ? '64 bits' : '32 bits'}</div>
        <div className="small dim" style={{ maxWidth: 640, margin: '0 auto' }}>
          Sem telemetria, sem anúncios e sem limpeza de registro. Tudo roda localmente — a única conexão é a checagem de atualizações no GitHub (desligável).
          Interface com componentes de React Bits, Magic UI (21st.dev) e Uiverse.io.
        </div>
        <button className="btn sm ghost" style={{ marginTop: 12 }} onClick={() => call('app.openLog')}><Icon name="list" size={13} />Abrir log de erros</button>
      </Card>
    </div>
  );
}

type Upd = { current: string; version: string; notes: string; date: string; url: string; available: boolean; installed: boolean; size?: number };
type UpdSt = { state: 'idle' | 'checking' | 'downloading' | 'installing' | 'error'; progress: number; error: string | null; latest: Upd | null; checked: string | null };

function UpdateCard() {
  const app = useStore(appStore);
  const [st, setSt] = useState<UpdSt | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    call<UpdSt>('update.status').then(setSt).catch(() => {});
    return on('update', (u: Upd) => setSt(x => (x ? { ...x, latest: u } : x)));
  }, []);
  useEffect(() => {
    if (st?.state !== 'downloading' && st?.state !== 'installing') return;
    const t = setInterval(() => call<UpdSt>('update.status').then(setSt).catch(() => {}), 600);
    return () => clearInterval(t);
  }, [st?.state]);
  async function check() {
    setBusy(true);
    const r = await run(call<Upd>('update.check'));
    setBusy(false);
    if (r) { setSt(x => ({ ...(x as UpdSt), latest: r, checked: new Date().toISOString() })); if (!r.available) toast('Você já está na versão mais recente'); }
  }
  async function install() { const r = await run(call<UpdSt>('update.install')); if (r) setSt(r); }
  if (!app) return null;
  const u = st?.latest;
  const auto = app.settings.autoUpdate !== false;
  return (
    <Card>
      <CardTitle icon="download" color="#34d399" right={<Toggle on={auto} onChange={v => setSetting('autoUpdate', v)} title="Verificar sozinho ao abrir e a cada 24 h" />}>Atualizações</CardTitle>
      <div className="small muted">Versão instalada <b>{app.version}</b>. {auto ? 'Verifica sozinho ao abrir e a cada 24 h.' : 'Verificação automática desligada.'}{st?.checked ? ` Última verificação: ${new Date(st.checked).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}.` : ''}</div>
      {u?.available ? (
        <div style={{ marginTop: 12, padding: 14, borderRadius: 12, background: 'rgba(52,211,153,.08)', border: '1px solid rgba(52,211,153,.3)' }}>
          <div className="row"><b style={{ flex: 1 }}>Versão {u.version} disponível</b><span className="small dim">{u.size ? `${(u.size / 1048576).toFixed(1)} MB` : ''}</span></div>
          {u.notes && <pre className="small muted selectable" style={{ whiteSpace: 'pre-wrap', margin: '8px 0', maxHeight: 140, overflow: 'auto', fontFamily: 'inherit' }}>{u.notes}</pre>}
          {st?.state === 'downloading' ? <><Bar value={st.progress} /><div className="small dim" style={{ marginTop: 4 }}>Baixando… {st.progress}%</div></>
            : st?.state === 'installing' ? <div className="small"><Spinner size={12} /> Instalando — o GROBS vai fechar e abrir de novo na versão nova.</div>
              : <button className="btn primary" onClick={install}><Icon name="download" size={15} />Atualizar agora</button>}
          {st?.state === 'error' && <div className="small bad-t" style={{ marginTop: 6 }}>{st.error}</div>}
        </div>
      ) : (
        <button className="btn sm" style={{ marginTop: 12 }} onClick={check} disabled={busy}>{busy ? <Spinner size={13} /> : <Icon name="refresh" size={13} />}Procurar atualizações</button>
      )}
    </Card>
  );
}

function BackupCard() {
  const [busy, setBusy] = useState<string | null>(null);
  async function exp() {
    setBusy('exp');
    const f = await run(call<string | null>('backup.export'));
    setBusy(null);
    if (f) toast(`Backup salvo: ${f.split(/[\\/]/).pop()}`);
  }
  async function imp() {
    setBusy('imp');
    const r = await run(call<{ changed: number; skipped: number; from: string } | null>('backup.import'));
    setBusy(null);
    if (r) { toast(`Backup de ${r.from || 'outro PC'} aplicado: ${r.changed} ajuste(s)${r.skipped ? ` · ${r.skipped} precisam de admin` : ''}`); setTimeout(() => location.reload(), 1500); }
  }
  return (
    <Card>
      <CardTitle icon="upload" color="#a78bfa">Backup dos ajustes</CardTitle>
      <div className="small muted">Salva em um arquivo as configurações do GROBS, os ajustes do Windows ligados, os serviços desativados e os efeitos visuais — para levar a outro PC ou reaplicar depois de formatar.</div>
      <div className="row" style={{ marginTop: 12 }}>
        <button className="btn sm" onClick={exp} disabled={!!busy}>{busy === 'exp' ? <Spinner size={13} /> : <Icon name="download" size={13} />}Exportar…</button>
        <button className="btn sm" onClick={imp} disabled={!!busy}>{busy === 'imp' ? <Spinner size={13} /> : <Icon name="upload" size={13} />}Importar…</button>
      </div>
    </Card>
  );
}
