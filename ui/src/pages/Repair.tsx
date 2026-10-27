import { useEffect, useRef, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import GlareHover from '../components/rb/GlareHover';
import { AdminLock, Bar, Card, CardTitle, PageHead, Spinner } from '../components/ui';
import { appStore, run, useStore } from '../store';

type St = { job: string | null; running: boolean; pct: number; exit: number; lines: string[]; admin: boolean };

const JOBS = [
  { id: 'sfc', icon: 'shieldCheck', color: '#34d399', title: 'Verificar arquivos do sistema', desc: 'SFC /scannow — encontra e troca arquivos do Windows corrompidos.', time: '5–15 min' },
  { id: 'dism', icon: 'wrench', color: '#a78bfa', title: 'Reparar imagem do Windows', desc: 'DISM /RestoreHealth — baixa da Microsoft o que o SFC não consegue consertar. Rode antes do SFC se ele falhar.', time: '10–30 min' },
  { id: 'chkdsk', icon: 'disk', color: '#fbbf24', title: 'Verificar o disco C:', desc: 'CHKDSK /scan — procura erros no sistema de arquivos sem reiniciar.', time: '2–10 min' },
  { id: 'net', icon: 'wifi', color: '#22d3ee', title: 'Resetar a rede', desc: 'Winsock, TCP/IP, ARP e DNS. Resolve "sem internet" com Wi-Fi conectado e erros de DNS. Pede reiniciar.', time: '< 1 min' },
  { id: 'wu', icon: 'refresh', color: '#f472b6', title: 'Reparar o Windows Update', desc: 'Para os serviços, recria as pastas SoftwareDistribution e catroot2 e religa. Para atualizações travadas ou com erro.', time: '1 min' },
  { id: 'store', icon: 'package', color: '#60a5fa', title: 'Reparar a Microsoft Store', desc: 'WSReset: limpa o cache da Store (downloads parados, loja que não abre).', time: '< 1 min' },
  { id: 'icons', icon: 'sparkles', color: '#fb7185', title: 'Recriar cache de ícones', desc: 'Ícones em branco ou errados na área de trabalho e no Explorer. Reinicia o Explorer.', time: '< 1 min' },
];

export default function Repair() {
  const admin = useStore(appStore, a => a?.admin);
  const [st, setSt] = useState<St | null>(null);
  const logRef = useRef<HTMLPreElement>(null);
  useEffect(() => {
    call<St>('repair.status').then(setSt).catch(() => {});
    return on('repair', (s: St) => setSt(s));
  }, []);
  useEffect(() => { logRef.current?.scrollTo({ top: logRef.current.scrollHeight }); }, [st?.lines.length]);

  async function start(id: string) {
    const r = await run(call<St>('repair.start', { id }));
    if (r) setSt(r);
  }
  const job = JOBS.find(j => j.id === st?.job);
  return (
    <div className="page">
      <PageHead title="Reparar o Windows" sub="As ferramentas de reparo do próprio Windows, com o andamento aqui na tela." right={!admin && <AdminLock text="Os reparos precisam do modo administrador" />} />
      <div className="grid g3">
        {JOBS.map(j => (
          <GlareHover key={j.id} background="var(--surface)" borderColor={st?.job === j.id && st.running ? j.color : 'rgba(255,255,255,0.07)'} borderRadius="16px" width="100%" height="100%" glareOpacity={0.12} glareSize={260}>
            <button className="tool-btn" onClick={() => start(j.id)} disabled={!admin || !!st?.running}>
              <span className="qa-ico" style={{ color: j.color, background: `${j.color}1f` }}>{st?.running && st.job === j.id ? <Spinner size={18} /> : <Icon name={j.icon} size={20} />}</span>
              <b>{j.title}</b>
              <span className="small dim">{j.desc}</span>
              <span className="chip" style={{ marginTop: 'auto' }}><Icon name="clock" size={11} />{j.time}</span>
            </button>
          </GlareHover>
        ))}
      </div>
      {st?.job && (
        <Card style={{ marginTop: 16 }}>
          <CardTitle icon={job?.icon ?? 'wrench'} color={job?.color} right={st.running ? <span className="chip accent pulse">em andamento</span> : st.exit === 0 ? <span className="chip ok">concluído</span> : <span className="chip warn">terminou com código {st.exit}</span>}>{job?.title}</CardTitle>
          {st.running && st.pct >= 0 && <div style={{ marginBottom: 10 }}><Bar value={st.pct} /><div className="small dim" style={{ marginTop: 4 }}>{st.pct}%</div></div>}
          <pre ref={logRef} className="mono small selectable" style={{ margin: 0, padding: 12, borderRadius: 10, background: 'var(--bg-2)', border: '1px solid var(--line)', maxHeight: 300, overflow: 'auto', whiteSpace: 'pre-wrap', color: 'var(--text-2)' }}>{st.lines.join('\n') || '…'}</pre>
        </Card>
      )}
    </div>
  );
}
