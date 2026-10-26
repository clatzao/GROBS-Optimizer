import { Icon } from './Icon';
import { Card, CardTitle, Check } from './ui';
import { appStore, setSetting, toast, useStore } from '../store';

// mesmos módulos do instalador
export const MODULES: { id: string; title: string; desc: string }[] = [
  { id: 'monitor', title: 'Monitor do sistema', desc: 'CPU, RAM, GPU, discos, sensores e processos' },
  { id: 'repair', title: 'Diagnóstico', desc: 'Acha erros e resolve' },
  { id: 'clean', title: 'Limpeza', desc: 'Temporários, caches e restos de atualizações' },
  { id: 'optimize', title: 'Otimização', desc: 'Inicialização, serviços, recursos e efeitos visuais' },
  { id: 'update', title: 'Windows Update', desc: 'Pausar, adiar, desativar e limpar cache' },
  { id: 'security', title: 'Segurança', desc: 'Central com o Microsoft Defender' },
  { id: 'gamer', title: 'Gamer', desc: 'Otimização por jogo, perfis NVIDIA, ping e DNS' },
  { id: 'overlay', title: 'Overlay de FPS', desc: 'FPS e sensores por cima dos jogos' },
  { id: 'oc', title: 'Overclock da GPU', desc: 'Clock, energia e ventoinha (NVIDIA)' },
  { id: 'drivers', title: 'Drivers', desc: 'Windows Update + NVIDIA, Intel e AMD' },
  { id: 'beautify', title: 'Embelezar o Windows', desc: 'Tema, cores, barra de tarefas, Explorer' },
  { id: 'tools', title: 'Ferramentas', desc: 'Desinstalador, analisador de disco, utilitários' },
];

/** Liga/desliga módulos depois de instalado — esconde as abas que você não usa. */
export default function ModulesCard() {
  const mods = useStore(appStore, a => a?.settings.modules);
  const on = (id: string) => !mods || mods.length === 0 || mods.includes(id);
  function toggle(id: string, v: boolean) {
    const cur = MODULES.map(m => m.id).filter(on);
    const next = v ? [...cur, id] : cur.filter(x => x !== id);
    if (next.length === 0) { toast('Deixe pelo menos um módulo ativo', 'warn'); return; }
    setSetting('modulesUser', next.length === MODULES.length ? MODULES.map(m => m.id) : next);
  }
  return (
    <Card className="pad-0" style={{ marginTop: 16 }}>
      <div style={{ padding: '16px 18px 4px' }}>
        <CardTitle icon="dashboard" color="#34d399" right={<span className="small dim">{MODULES.filter(m => on(m.id)).length}/{MODULES.length} ativos</span>}>Módulos</CardTitle>
        <div className="small muted" style={{ margin: '-4px 0 8px' }}><Icon name="info" size={12} /> Desative o que você não usa: a aba some do menu. Ajustes que você já aplicou continuam valendo.</div>
      </div>
      <div className="grid g3" style={{ gap: 0 }}>
        {MODULES.map(m => (
          <label key={m.id} className="li" style={{ padding: '10px 18px', cursor: 'pointer' }}>
            <Check on={on(m.id)} onChange={v => toggle(m.id, v)} />
            <div className="grow"><div className="t" style={{ fontSize: 13 }}>{m.title}</div><div className="s ellipsis">{m.desc}</div></div>
          </label>
        ))}
      </div>
    </Card>
  );
}
