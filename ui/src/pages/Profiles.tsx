import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import SpotlightCard from '../components/rb/SpotlightCard';
import { AdminLock, Beam, PageHead, Spinner } from '../components/ui';
import { appStore, run, toast, useStore } from '../store';

const PROFILES = [
  { id: 'game', icon: 'gamepad', color: '#a78bfa', title: 'Jogo', desc: 'Plano de energia máximo, Modo de Jogo, sem gravação em segundo plano, mouse 1:1, prioridade multimídia para jogos, RAM liberada.' },
  { id: 'work', icon: 'list', color: '#22d3ee', title: 'Trabalho', desc: 'Plano equilibrado, efeitos visuais equilibrados, sem dicas, anúncios e sugestões que distraem.' },
  { id: 'laptop', icon: 'battery', color: '#34d399', title: 'Notebook / bateria', desc: 'Plano equilibrado, sem transparência e animações, apps da Store e Edge sem rodar em segundo plano.' },
  { id: 'max', icon: 'rocket', color: '#f472b6', title: 'Desempenho máximo', desc: 'Tudo do perfil Jogo + efeitos visuais no mínimo, núcleos sempre ativos e sem limitação de energia.' },
];

export default function Profiles() {
  const admin = useStore(appStore, a => a?.admin);
  const [active, setActive] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => { call<{ active: string | null }>('profiles.status').then(s => setActive(s.active)).catch(() => {}); }, []);

  async function apply(id: string) {
    setBusy(id);
    const r = await run(call<{ active: string; changed: number; skipped: number }>('profiles.apply', { id }));
    setBusy(null);
    if (r) { setActive(r.active); toast(`Perfil aplicado: ${r.changed} ajuste(s)${r.skipped ? ` · ${r.skipped} precisam de admin` : ''}`); }
  }
  async function undo() {
    setBusy('undo');
    const r = await run(call<{ active: string | null }>('profiles.undo'), 'Perfil desfeito — tudo voltou como estava');
    setBusy(null);
    if (r) setActive(r.active);
  }

  return (
    <div className="page">
      <PageHead title="Perfis de 1 clique" sub="Um conjunto de ajustes por situação. O GROBS guarda como estava antes e desfaz com um clique."
        right={<>{!admin && <AdminLock text="Sem admin, só os ajustes do usuário são aplicados" />}{active && <button className="btn" onClick={undo} disabled={!!busy}>{busy === 'undo' ? <Spinner size={14} /> : <Icon name="restore" size={15} />}Desfazer perfil</button>}</>} />
      <div className="grid g2">
        {PROFILES.map(p => (
          <SpotlightCard key={p.id} className="card" spotlightColor={`${p.color}33`} intensity={0.9} spotlightSize={380} borderGlow={0.6} grain={0}
            style={{ borderColor: active === p.id ? p.color : undefined, padding: 22 }}>
            {active === p.id && <Beam c1={p.color} c2="#22d3ee" size={140} />}
            <div className="row" style={{ gap: 14, alignItems: 'flex-start' }}>
              <span className="qa-ico" style={{ color: p.color, background: `${p.color}1f`, width: 52, height: 52 }}><Icon name={p.icon} size={24} /></span>
              <div style={{ flex: 1 }}>
                <div className="row"><b style={{ font: '650 18px var(--font-display)' }}>{p.title}</b>{active === p.id && <span className="chip ok"><Icon name="check" size={11} />ativo</span>}</div>
                <div className="small muted" style={{ margin: '6px 0 14px' }}>{p.desc}</div>
                <button className={`btn ${active === p.id ? '' : 'primary'}`} onClick={() => apply(p.id)} disabled={!!busy}>{busy === p.id ? <Spinner size={14} /> : <Icon name="zap" size={15} />}{active === p.id ? 'Aplicar de novo' : 'Aplicar'}</button>
              </div>
            </div>
          </SpotlightCard>
        ))}
      </div>
      <p className="small dim" style={{ marginTop: 16 }}>Ao trocar de perfil, o anterior é desfeito antes. Ajustes de risco moderado criam um ponto de restauração do Windows antes (uma vez por sessão).</p>
    </div>
  );
}
