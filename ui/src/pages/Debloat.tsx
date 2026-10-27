import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import TweakList from '../components/TweakList';
import { Card, CardTitle, Modal, PageHead, Spinner, Tabs } from '../components/ui';
import { run, toast } from '../store';

type App = { name: string; title: string; version: string };

function Bloatware() {
  const [list, setList] = useState<App[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<App | null>(null);
  useEffect(() => { call<App[]>('appx.list').then(setList).catch(() => setList([])); }, []);

  async function remove(a: App) {
    setConfirm(null); setBusy(a.name);
    const r = await run(call('appx.remove', { name: a.name }));
    setBusy(null);
    if (r !== undefined) { toast(`${a.title} removido`); setList(l => l?.filter(x => x.name !== a.name) ?? null); }
  }

  return (
    <Card className="pad-0">
      <div style={{ padding: '16px 18px 6px' }}>
        <CardTitle icon="trash" color="#f472b6">Apps pré-instalados dispensáveis</CardTitle>
        <p className="small muted" style={{ margin: '-4px 0 8px' }}>Só aparecem apps reconhecidamente inúteis para a maioria. Todos podem ser reinstalados pela Microsoft Store.</p>
      </div>
      {!list ? <div className="empty"><Spinner /> <div className="small dim" style={{ marginTop: 8 }}>Lendo apps instalados…</div></div>
        : list.length === 0 ? <div className="empty">Nenhum app dispensável encontrado — seu Windows já está limpo.</div>
          : (
            <div className="grid g2" style={{ gap: 0 }}>
              {list.map(a => (
                <div key={a.name} className="li">
                  <div className="grow"><div className="t ellipsis">{a.title}</div><div className="s ellipsis mono">{a.name}</div></div>
                  <button className="btn sm danger" disabled={!!busy} onClick={() => setConfirm(a)}>{busy === a.name ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Remover</button>
                </div>
              ))}
            </div>
          )}
      <Modal open={!!confirm} onClose={() => setConfirm(null)}>
        <h3>Remover {confirm?.title}?</h3>
        <p className="muted" style={{ marginTop: 0 }}>O app é desinstalado da sua conta. Se precisar, reinstale pela Microsoft Store.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(null)}>Cancelar</button><button className="btn danger" onClick={() => confirm && remove(confirm)}>Remover</button></div>
      </Modal>
    </Card>
  );
}

export default function Debloat() {
  const [tab, setTab] = useState<'features' | 'apps'>('features');
  return (
    <div className="page">
      <PageHead title="Recursos do Windows" sub="Desligue Copilot, Cortana, Recall, Widgets, anúncios, telemetria do Windows e de programas, apps em segundo plano e apps que vêm de fábrica — no estilo do Optimizer (hellzerg) e do WinUtil. Tudo reversível."
        right={<Tabs layoutId="dbtab" value={tab} onChange={setTab} items={[{ id: 'features', label: 'Recursos' }, { id: 'apps', label: 'Apps pré-instalados' }]} />} />
      {tab === 'features'
        ? <TweakList groups={['IA e assistentes', 'Barra de tarefas', 'Anúncios e sugestões', 'Sistema', 'Telemetria', 'Apps em segundo plano', 'Navegadores']}
            icons={{ 'IA e assistentes': ['sparkles', '#a78bfa'], 'Barra de tarefas': ['list', '#22d3ee'], 'Anúncios e sugestões': ['eye', '#f472b6'], 'Sistema': ['settings', '#fbbf24'], 'Telemetria': ['eye', '#fb7185'], 'Apps em segundo plano': ['layers', '#34d399'], 'Navegadores': ['globe', '#60a5fa'] }} />
        : <Bloatware />}
    </div>
  );
}
