import { useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import GlareHover from '../components/rb/GlareHover';
import { AdminLock, Card, CardTitle, PageHead, Spinner } from '../components/ui';
import { appStore, run, useStore } from '../store';

type Ping = { host: string; avg: number; min: number; max: number; lost: number };
const HOSTS = [{ host: '1.1.1.1', name: 'Cloudflare' }, { host: '8.8.8.8', name: 'Google DNS' }, { host: '208.67.222.222', name: 'OpenDNS' }];

export default function Tools() {
  const admin = useStore(appStore, a => a?.admin);
  const [busy, setBusy] = useState<string | null>(null);
  const [pings, setPings] = useState<Ping[]>([]);

  async function act(id: string, m: string, ok: string, p?: any) {
    setBusy(id);
    await run(call(m, p), ok);
    setBusy(null);
  }
  async function pingAll() {
    setBusy('ping'); setPings([]);
    for (const h of HOSTS) {
      const r = await call<Ping>('tools.ping', { host: h.host }).catch(() => null);
      if (r) setPings(p => [...p, r]);
    }
    setBusy(null);
  }

  const tools = [
    { id: 'dns', icon: 'globe', color: '#22d3ee', title: 'Limpar cache DNS', desc: 'Resolve sites que não abrem após mudança de endereço.', run: () => act('dns', 'tools.flushDns', 'Cache DNS limpo') },
    { id: 'explorer', icon: 'refresh', color: '#a78bfa', title: 'Reiniciar o Explorer', desc: 'Corrige barra de tarefas ou área de trabalho travadas.', run: () => act('explorer', 'tools.restartExplorer', 'Explorer reiniciado') },
    { id: 'restore', icon: 'restore', color: '#34d399', title: 'Ponto de restauração', desc: 'Cria um ponto para desfazer mudanças no sistema.', admin: true, run: () => act('restore', 'tools.restorePoint', 'Ponto de restauração criado') },
    { id: 'sfc', icon: 'shieldCheck', color: '#fbbf24', title: 'Verificar arquivos do sistema', desc: 'Executa o SFC /scannow em uma janela (pede permissão).', run: () => act('sfc', 'tools.repair', 'SFC aberto em nova janela', { which: 'sfc' }) },
    { id: 'dism', icon: 'wrench', color: '#f472b6', title: 'Reparar imagem do Windows', desc: 'DISM /RestoreHealth: corrige o que o SFC não consegue.', run: () => act('dism', 'tools.repair', 'DISM aberto em nova janela', { which: 'dism' }) },
  ];

  return (
    <div className="page">
      <PageHead title="Utilitários" sub="Ferramentas do próprio Windows, a um clique." />
      <div className="grid g3">
        {tools.map(t => (
          <GlareHover key={t.id} background="var(--surface)" borderColor="rgba(255,255,255,0.07)" borderRadius="16px" width="100%" height="100%" glareOpacity={0.14} glareSize={260}>
            <button className="tool-btn" onClick={t.run} disabled={!!busy || (t.admin && !admin)}>
              <span className="qa-ico" style={{ color: t.color, background: `${t.color}1f` }}>{busy === t.id ? <Spinner size={18} /> : <Icon name={t.icon} size={20} />}</span>
              <b>{t.title} {t.admin && !admin && <AdminLock />}</b>
              <span className="small dim">{t.desc}</span>
            </button>
          </GlareHover>
        ))}
      </div>

      <h2 className="sec"><Icon name="network" size={16} />Latência da internet</h2>
      <Card>
        <CardTitle icon="wifi" color="#34d399" right={<button className="btn sm primary" onClick={pingAll} disabled={busy === 'ping'}>{busy === 'ping' ? <Spinner size={13} /> : <Icon name="play" size={13} />}Testar</button>}>Ping para servidores públicos</CardTitle>
        <div className="grid g3">
          {HOSTS.map(h => {
            const r = pings.find(p => p.host === h.host);
            const q = !r ? '' : r.avg < 0 ? 'bad' : r.avg < 30 ? 'ok' : r.avg < 80 ? 'warn' : 'bad';
            return (
              <div key={h.host} style={{ padding: 14, borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)' }}>
                <div className="small dim">{h.name} · {h.host}</div>
                <div className={`stat-big ${q}-t`} style={{ fontSize: 26, margin: '6px 0 2px' }}>{!r ? (busy === 'ping' ? <Spinner /> : '—') : r.avg < 0 ? 'sem resposta' : `${Math.round(r.avg)} ms`}</div>
                {r && r.avg >= 0 && <div className="small dim">mín {r.min} · máx {r.max} · {r.lost} perdido(s)</div>}
              </div>
            );
          })}
        </div>
      </Card>
    </div>
  );
}
