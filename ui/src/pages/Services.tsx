import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Card, Modal, PageHead, Spinner, Stagger, Toggle } from '../components/ui';
import { appStore, run, toast, useStore } from '../store';

type Svc = { name: string; title: string; desc: string; group: string; risk: string; start: string; default: string; status: string; disabled: boolean; locked: boolean };

const GROUPS: { id: string; icon: string; color: string; hint: string }[] = [
  { id: 'Telemetria', icon: 'eye', color: '#22d3ee', hint: 'Coleta e envio de dados para a Microsoft.' },
  { id: 'Inúteis', icon: 'trash', color: '#f472b6', hint: 'Recursos que quase ninguém usa em casa.' },
  { id: 'Xbox', icon: 'gamepad', color: '#34d399', hint: 'Só necessários para jogos da Xbox/Game Pass.' },
  { id: 'Opcionais', icon: 'settings', color: '#fbbf24', hint: 'Depende do seu uso — leia antes de desativar.' },
];

export default function Services() {
  const [list, setList] = useState<Svc[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<string | null>(null);
  const admin = useStore(appStore, a => a?.admin);
  const load = () => call<Svc[]>('svc.list').then(setList).catch(() => setList([]));
  useEffect(() => { load(); }, []);

  async function set(s: Svc, disable: boolean) {
    setBusy(s.name);
    const r = await run(call('svc.set', { name: s.name, disable }), disable ? `${s.title}: parado e desativado` : `${s.title}: restaurado ao padrão`);
    setBusy(null);
    if (r !== undefined) load();
  }

  async function disableGroup(g: string) {
    setConfirm(null);
    for (const s of (list ?? []).filter(x => x.group === g && !x.disabled && x.risk === 'low')) {
      setBusy(s.name);
      await call('svc.set', { name: s.name, disable: true }).catch(() => {});
    }
    setBusy(null);
    toast(`Serviços de "${g}" desativados (os de risco baixo)`);
    load();
  }

  const off = (list ?? []).filter(s => s.disabled).length;
  return (
    <div className="page">
      <PageHead title="Serviços do Windows" sub="Pare e desative serviços da Microsoft que rodam à toa. Tudo pode ser restaurado ao padrão."
        right={!admin && <span className="chip warn"><Icon name="lock" size={12} />Abra como administrador para alterar</span>} />
      {!list ? <div className="empty"><Spinner size={24} /></div> : (
        <>
          <div className="grid g3" style={{ marginBottom: 16 }}>
            <Card><div className="small dim">Desativados</div><div className="stat-big">{off}</div></Card>
            <Card><div className="small dim">Rodando agora</div><div className="stat-big">{list.filter(s => s.status === 'running').length}</div></Card>
            <Card><div className="small dim">Como funciona</div><div className="small muted" style={{ marginTop: 4 }}>Desativar = parar agora e não iniciar mais. Restaurar = volta ao tipo de inicialização original do Windows.</div></Card>
          </div>
          <div className="grid g2">
            {GROUPS.filter(g => list.some(s => s.group === g.id)).map(g => (
              <Card key={g.id} className="pad-0">
                <div className="row" style={{ padding: '14px 16px', borderBottom: '1px solid var(--line)' }}>
                  <span style={{ width: 30, height: 30, borderRadius: 9, display: 'grid', placeItems: 'center', color: g.color, background: `${g.color}1f` }}><Icon name={g.icon} size={16} /></span>
                  <div style={{ flex: 1 }}><b>{g.id}</b><div className="small dim">{g.hint}</div></div>
                  {g.id !== 'Opcionais' && <button className="btn sm" disabled={!admin || !!busy} onClick={() => setConfirm(g.id)}>Desativar todos</button>}
                </div>
                <Stagger className="list" delay={0.02}>
                  {list.filter(s => s.group === g.id).map(s => (
                    <div key={s.name} className="li" style={{ alignItems: 'flex-start' }}>
                      <div className="grow">
                        <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
                          <span className="t">{s.title}</span>
                          <span className={`risk ${s.risk}`}>{s.risk === 'low' ? 'seguro' : s.risk === 'medium' ? 'depende' : 'cuidado'}</span>
                        </div>
                        <div className="s" style={{ marginTop: 2 }}>{s.desc}</div>
                        <div className="s mono" style={{ marginTop: 3, fontSize: 11 }}>
                          {s.name} · <span className={s.status === 'running' ? 'warn-t' : ''}>{s.status === 'running' ? 'rodando' : s.status === 'stopped' ? 'parado' : s.status}</span> · {s.start === 'disabled' ? 'desativado' : s.start === 'auto' ? 'automático' : 'manual'}
                        </div>
                      </div>
                      {busy === s.name ? <Spinner /> : <Toggle on={s.disabled} onChange={v => set(s, v)} disabled={!admin} title={s.disabled ? 'Restaurar' : 'Desativar'} />}
                    </div>
                  ))}
                </Stagger>
              </Card>
            ))}
          </div>
          {!admin && <p className="small dim" style={{ marginTop: 14 }}><AdminLock /> Alterar serviços exige privilégios de administrador (Configurações → Executar como admin).</p>}
        </>
      )}
      <Modal open={!!confirm} onClose={() => setConfirm(null)}>
        <h3>Desativar serviços de “{confirm}”?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Só os marcados como <b>seguro</b> serão desativados. Você pode restaurar cada um depois.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(null)}>Cancelar</button><button className="btn primary" onClick={() => disableGroup(confirm!)}>Desativar</button></div>
      </Modal>
    </div>
  );
}
