import { useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import TweakList from '../components/TweakList';
import { Card, CardTitle, PageHead, Spinner } from '../components/ui';
import { run } from '../store';

const COLORS = ['#8b5cf6', '#6366f1', '#0ea5e9', '#14b8a6', '#22c55e', '#eab308', '#f97316', '#ef4444', '#ec4899', '#64748b'];

export default function Beautify() {
  const [busy, setBusy] = useState<string | null>(null);
  const [custom, setCustom] = useState('#8b5cf6');

  async function accent(hex: string) {
    setBusy(hex);
    await run(call('accent.set', { hex }), 'Cor de destaque aplicada');
    setBusy(null);
  }

  return (
    <div className="page">
      <PageHead title="Embelezar o Windows" sub="Tema, cores, barra de tarefas e Explorador — os ajustes de visual mais pedidos do Windows 10/11, num clique." />
      <Card style={{ marginBottom: 16 }}>
        <CardTitle icon="sparkles" color="#f472b6">Cor de destaque</CardTitle>
        <div className="row wrap" style={{ gap: 10 }}>
          {COLORS.map(c => (
            <button key={c} title={c} onClick={() => accent(c)} disabled={!!busy}
              style={{ width: 38, height: 38, borderRadius: 12, border: '2px solid rgba(255,255,255,.12)', background: c, display: 'grid', placeItems: 'center', boxShadow: `0 6px 18px -6px ${c}`, transition: 'transform .2s' }}
              onMouseEnter={e => (e.currentTarget.style.transform = 'scale(1.12)')} onMouseLeave={e => (e.currentTarget.style.transform = '')}>
              {busy === c && <Spinner size={14} color="#fff" />}
            </button>
          ))}
          <span className="small dim" style={{ marginLeft: 8 }}>ou</span>
          <input type="color" value={custom} onChange={e => setCustom(e.target.value)} style={{ width: 44, height: 38, border: 0, background: 'transparent', padding: 0 }} />
          <button className="btn sm" onClick={() => accent(custom)} disabled={!!busy}>Aplicar</button>
        </div>
        <p className="small dim" style={{ marginBottom: 0 }}><Icon name="info" size={12} /> Para ver a cor na barra de tarefas, ative também “Cor de destaque no Iniciar e barra de tarefas” e o modo escuro do Windows.</p>
      </Card>
      <TweakList groups={['Tema', 'Barra de tarefas e Iniciar', 'Explorador de Arquivos', 'Área de trabalho']}
        icons={{ 'Tema': ['eye', '#a78bfa'], 'Barra de tarefas e Iniciar': ['list', '#22d3ee'], 'Explorador de Arquivos': ['folder', '#fbbf24'], 'Área de trabalho': ['dashboard', '#34d399'] }} />
      <Card style={{ marginTop: 16 }}>
        <CardTitle icon="external" color="#60a5fa">Quer ir além?</CardTitle>
        <div className="small muted">Ferramentas gratuitas e confiáveis da comunidade: <b>Windhawk</b> (mods da barra e do Iniciar), <b>Lively Wallpaper</b> (papéis de parede animados) e <b>Rainmeter</b> (widgets na área de trabalho).</div>
        <div className="row wrap" style={{ marginTop: 10 }}>
          <a className="btn sm" href="https://windhawk.net" target="_blank" rel="noreferrer">Windhawk</a>
          <a className="btn sm" href="https://www.rocksdanister.com/lively/" target="_blank" rel="noreferrer">Lively Wallpaper</a>
          <a className="btn sm" href="https://www.rainmeter.net" target="_blank" rel="noreferrer">Rainmeter</a>
        </div>
      </Card>
    </div>
  );
}
