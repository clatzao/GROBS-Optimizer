import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from './Icon';
import { AdminLock, Card, Spinner, Stagger, Toggle } from './ui';
import { run, toast } from '../store';

export type Tweak = { id: string; group: string; title: string; desc: string; risk: string; admin?: boolean; locked?: boolean; restart?: boolean; explorer?: boolean; on: boolean };

/** Lista de ajustes reversíveis agrupada, reaproveitada por Recursos do Windows e Embelezar. */
export default function TweakList({ groups, icons }: { groups: string[]; icons: Record<string, [string, string]> }) {
  const [list, setList] = useState<Tweak[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [needExplorer, setNeedExplorer] = useState(false);
  useEffect(() => { call<Tweak[]>('tweaks.list', { groups }).then(setList).catch(() => setList([])); }, []);

  async function set(t: Tweak, on: boolean) {
    setBusy(t.id);
    const r = await run(call<boolean>('tweaks.set', { id: t.id, on }));
    if (r !== undefined) {
      setList(l => l?.map(x => (x.id === t.id ? { ...x, on } : x)) ?? null);
      if (t.explorer) setNeedExplorer(true);
      toast(t.restart ? `${t.title}: reinicie o PC para ter efeito.` : `${t.title}: ${on ? 'aplicado' : 'revertido'}`, t.restart ? 'info' : 'ok');
    }
    setBusy(null);
  }
  async function restartExplorer() {
    await run(call('tools.restartExplorer'), 'Explorer reiniciado — mudanças aplicadas');
    setNeedExplorer(false);
  }

  if (!list) return <div className="empty"><Spinner size={24} /></div>;
  return (
    <>
      {needExplorer && (
        <Card style={{ marginBottom: 14, borderColor: 'rgba(139,92,246,.4)' }}>
          <div className="row"><Icon name="refresh" size={18} style={{ color: '#a78bfa' }} /><span style={{ flex: 1 }}>Algumas mudanças aparecem depois de reiniciar o Explorer (a barra de tarefas pisca por 1 segundo).</span>
            <button className="btn sm primary" onClick={restartExplorer}>Reiniciar Explorer</button></div>
        </Card>
      )}
      <div className="grid g2">
        {groups.filter(g => list.some(t => t.group === g)).map(g => (
          <Card key={g} className="pad-0">
            <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
              <Icon name={icons[g]?.[0] ?? 'settings'} size={17} style={{ color: icons[g]?.[1] ?? '#a78bfa' }} />
              <b>{g}</b><span className="spacer" /><span className="small dim">{list.filter(t => t.group === g && t.on).length}/{list.filter(t => t.group === g).length} ativos</span>
            </div>
            <Stagger className="list" delay={0.025}>
              {list.filter(t => t.group === g).map(t => (
                <div key={t.id} className="li" style={{ alignItems: 'flex-start' }}>
                  <div className="grow">
                    <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
                      <span className="t">{t.title}</span>
                      {t.risk !== 'low' && <span className={`risk ${t.risk}`}>{t.risk === 'medium' ? 'moderado' : 'avançado'}</span>}
                    </div>
                    {t.desc && <div className="s" style={{ marginTop: 3 }}>{t.desc}</div>}
                  </div>
                  {t.locked && <AdminLock />}
                  {busy === t.id ? <Spinner /> : <Toggle on={t.on} onChange={v => set(t, v)} disabled={t.locked} />}
                </div>
              ))}
            </Stagger>
          </Card>
        ))}
      </div>
      {list.length === 0 && <Card><div className="empty">Nenhum ajuste disponível nesta versão do Windows.</div></Card>}
    </>
  );
}
