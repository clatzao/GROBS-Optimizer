import { useEffect, useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import GlareHover from '../components/rb/GlareHover';
import { Card, PageHead, Spinner, Stagger, Toggle } from '../components/ui';
import { run } from '../store';

type Fx = { id: string; title: string; desc: string; weight: number; on: boolean; keep: boolean };

const PRESETS = [
  { id: 'performance', icon: 'zap', color: '#34d399', title: 'Desempenho máximo', desc: 'Remove toda a “beleza” do Windows. Ideal para PCs fracos e jogos.' },
  { id: 'balanced', icon: 'activity', color: '#a78bfa', title: 'Equilibrado', desc: 'Tira só os efeitos pesados (transparência, sombras, animações de janela).' },
  { id: 'beauty', icon: 'sparkles', color: '#f472b6', title: 'Padrão do Windows', desc: 'Liga todos os efeitos de novo, como veio de fábrica.' },
];

export default function Visual() {
  const [list, setList] = useState<Fx[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => { call<Fx[]>('vfx.list').then(setList).catch(() => setList([])); }, []);

  async function preset(id: string, title: string) {
    setBusy(id);
    const r = await run(call<Fx[]>('vfx.preset', { preset: id }), `Perfil "${title}" aplicado`);
    if (r) setList(r);
    setBusy(null);
  }
  async function set(f: Fx, on: boolean) {
    setList(l => l?.map(x => (x.id === f.id ? { ...x, on } : x)) ?? null);
    const r = await run(call<Fx[]>('vfx.set', { id: f.id, on }));
    if (r) setList(r);
  }

  const offCount = (list ?? []).filter(f => !f.on).length;
  return (
    <div className="page">
      <PageHead title="Efeitos visuais" sub="Os mesmos itens de “Opções de desempenho” do Windows, aplicados na hora — sem reiniciar." />
      <div className="grid g3" style={{ marginBottom: 18 }}>
        {PRESETS.map(p => (
          <GlareHover key={p.id} background="var(--surface)" borderColor="rgba(255,255,255,0.07)" borderRadius="16px" width="100%" height="100%" glareOpacity={0.14} glareSize={260}>
            <button className="tool-btn" onClick={() => preset(p.id, p.title)} disabled={!!busy}>
              <span className="qa-ico" style={{ color: p.color, background: `${p.color}1f` }}>{busy === p.id ? <Spinner size={18} /> : <Icon name={p.icon} size={20} />}</span>
              <b>{p.title}</b>
              <span className="small dim">{p.desc}</span>
            </button>
          </GlareHover>
        ))}
      </div>
      <Card className="pad-0">
        <div className="row" style={{ padding: '14px 18px', borderBottom: '1px solid var(--line)' }}>
          <Icon name="eye" size={17} style={{ color: '#a78bfa' }} /><b>Efeitos individuais</b><span className="spacer" />
          <span className="small dim">{list ? `${offCount} desligado(s) de ${list.length}` : ''}</span>
        </div>
        {!list ? <div className="empty"><Spinner /></div> : (
          <Stagger className="list" delay={0.02}>
            {list.map(f => (
              <div key={f.id} className="li">
                <div className="grow">
                  <div className="row" style={{ gap: 8 }}>
                    <span className="t">{f.title}</span>
                    {f.weight >= 2 && <span className="risk medium">{f.weight === 3 ? 'pesado' : 'médio'}</span>}
                    {f.keep && <span className="risk low">manter</span>}
                  </div>
                  {f.desc && <div className="s">{f.desc}</div>}
                </div>
                <Toggle on={f.on} onChange={v => set(f, v)} />
              </div>
            ))}
          </Stagger>
        )}
      </Card>
    </div>
  );
}
