import { useEffect, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Card, CardTitle, Modal, PageHead, Spinner, Toggle } from '../components/ui';
import { bytes } from '../format';
import { run, toast } from '../store';

type Hit = { path: string; kind: 'malware' | 'suspeito' | 'atenção'; reason: string; md5?: string; source: string; score: number; size: number };
type Av = {
  running: boolean; phase: string; error?: string; type: string; files: number; done: number; current: string; elapsed: number; finished?: string;
  hits: Hit[]; sigs: number; sigDate: string; bazaar: boolean; ignored: number; admin: boolean;
};

const KIND: Record<Hit['kind'], [string, string]> = { malware: ['Malware conhecido', 'bad'], suspeito: ['Suspeito', 'warn'], 'atenção': ['Atenção', 'info'] };
const TYPES: Record<string, string> = { quick: 'Rápida', full: 'Completa', custom: 'Pasta', update: 'Atualização' };

function fmt(s: number) { const m = Math.floor(s / 60); return `${m}:${String(s % 60).padStart(2, '0')}`; }

function DefenderCard() {
  const [st, setSt] = useState<any>(null);
  const [ask, setAsk] = useState(false);
  const [busy, setBusy] = useState(false);
  const load = () => call('sec.status').then(setSt).catch(() => setSt({ defender: false }));
  useEffect(() => { load(); }, []);
  async function set(on: boolean) {
    setAsk(false); setBusy(true);
    const r = await run(call('av.defender', { on }), on ? 'Proteção do Defender religada' : 'Proteção em tempo real do Defender desligada');
    setBusy(false);
    if (r) setSt(r); else load();
  }
  const rt = !!st?.realtime;
  return (
    <Card>
      <CardTitle icon="shield" color="#60a5fa" right={st?.defender ? (busy ? <Spinner size={16} /> : <Toggle on={rt} onChange={v => (v ? set(true) : setAsk(true))} />) : null}>Microsoft Defender</CardTitle>
      {!st ? <Spinner /> : !st.defender ? <div className="small muted">O Defender não está ativo neste PC (outro antivírus pode ter assumido).</div> : (
        <div className="small muted">
          Proteção em tempo real: <b className={rt ? 'ok-t' : 'bad-t'}>{rt ? 'ligada' : 'desligada'}</b>
          {' · '}Proteção contra Adulteração: <b className={st.tamper ? 'warn-t' : 'muted'}>{st.tamper ? 'ligada' : 'desligada'}</b>
          <div style={{ marginTop: 6 }}>O antivírus do GROBS só verifica quando você manda e não vigia arquivos o tempo todo. Com o Defender desligado, nada bloqueia um vírus no momento em que ele chega.</div>
          {st.tamper && rt && <div className="warn-t" style={{ marginTop: 6 }}>Para desligar, primeiro desative a Proteção contra Adulteração na Segurança do Windows (o GROBS abre a tela para você).</div>}
        </div>
      )}
      <Modal open={ask} onClose={() => setAsk(false)}>
        <h3>Desligar o Defender?</h3>
        <p className="muted" style={{ marginTop: 0 }}>A proteção em tempo real, a análise de comportamento e a verificação de downloads e scripts do Defender ficam desligadas até você religar aqui.</p>
        <p className="muted">O antivírus do GROBS <b>não</b> substitui isso: ele só encontra o que já está no disco quando você faz uma verificação.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setAsk(false)}>Cancelar</button><button className="btn danger" onClick={() => set(false)}>Desligar mesmo assim</button></div>
      </Modal>
    </Card>
  );
}

// ------------------------------------------------------------------ proteções (tempo real, pendrive, ransomware), quarentena, clique direito
type Guard = {
  realtime: boolean; usb: boolean; ransom: boolean; ransomFolders: string[]; customFolders: string[]; cfa: 'on' | 'off' | 'audit' | 'unavailable';
  contextMenu: boolean; autoQ: boolean; yara: boolean; yaraRules: number; yaraDate: string;
  quarantine: { id: string; path: string; reason: string; date: string; size: number }[];
  alert: { reason: string; time: string; pid?: number; name?: string; path?: string; written?: number; suspended?: boolean } | null; admin: boolean;
};

function useGuard() {
  const [g, setG] = useState<Guard | null>(null);
  useEffect(() => { call<Guard>('avg.status').then(setG).catch(() => {}); return on('avGuard', setG); }, []);
  return [g, setG] as const;
}

function Risk({ level }: { level: 'low' | 'medium' | 'high' }) {
  return <span className={`risk ${level}`}>{level === 'low' ? 'tranquilo' : level === 'medium' ? 'moderado' : 'perigoso'}</span>;
}

function GuardRow({ title, desc, on, risk, onChange, disabled }: { title: string; desc: string; on: boolean; risk: 'low' | 'medium' | 'high'; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <div className="li" style={{ padding: '12px 0' }}>
      <div className="grow"><div className="t">{title} <Risk level={risk} /></div><div className="s" style={{ whiteSpace: 'normal' }}>{desc}</div></div>
      <Toggle on={on} onChange={onChange} disabled={disabled} />
    </div>
  );
}

function GuardCard() {
  const [g, setG] = useGuard();
  if (!g) return <Card><Spinner /></Card>;
  const set = async (what: string, v: boolean) => { const r = await run(call<Guard>('avg.set', { what, on: v })); if (r) setG(r); };
  const add = async () => { const path = await call<string>('shell.pickFolder'); if (!path) return; const r = await run(call<Guard>('avg.addFolder', { path })); if (r) setG(r); };
  const rem = async (path: string) => { const r = await run(call<Guard>('avg.removeFolder', { path })); if (r) setG(r); };
  return (
    <Card>
      <CardTitle icon="shieldCheck" color="#4ade80">Proteção do GROBS</CardTitle>
      <div className="small dim">Funciona enquanto o GROBS estiver aberto, inclusive minimizado na bandeja. Para ficar sempre ligada, ative "Iniciar com o Windows" em Configurações.</div>
      <div className="list">
        <GuardRow title="Tempo real (leve)" risk="low" on={g.realtime} onChange={v => set('realtime', v)}
          desc="Vigia Downloads, Área de Trabalho, pastas temporárias e inicialização e verifica na hora cada programa, script ou instalador novo. Arquivos comuns não são lidos, então quase não pesa." />
        <GuardRow title="Pendrive e HD externo ao conectar" risk="low" on={g.usb} onChange={v => set('usb', v)}
          desc="Quando um dispositivo é conectado, verifica programas, scripts e documentos com macro nele e avisa na bandeja." />
        <GuardRow title="Quarentena automática de malware conhecido" risk="low" on={g.autoQ} onChange={v => set('autoQ', v)}
          desc="Arquivo idêntico a malware catalogado vai direto para a quarentena (dá para restaurar). Suspeitas da heurística e do YARA só avisam." />
        <GuardRow title="Anti-ransomware" risk="medium" on={g.ransom} onChange={v => set('ransom', v)}
          desc="Coloca arquivos-isca ocultos nas pastas protegidas e vigia alterações em massa. Se um programa começar a cifrar ou renomear muitos arquivos, ele é pausado na hora e o GROBS pergunta o que fazer." />
        {g.ransom && (
          <div style={{ padding: '4px 0 10px 12px' }}>
            <div className="small muted" style={{ marginBottom: 6 }}>Pastas protegidas:</div>
            {g.ransomFolders.map(f => (
              <div key={f} className="row small" style={{ gap: 8, marginBottom: 4 }}>
                <Icon name="folder" size={12} /><span className="mono selectable ellipsis" style={{ flex: 1 }}>{f}</span>
                {g.customFolders.includes(f) && <button className="btn ghost sm" onClick={() => rem(f)}><Icon name="x" size={11} /></button>}
              </div>
            ))}
            <button className="btn sm" onClick={add}><Icon name="folder" size={13} />Adicionar pasta</button>
          </div>
        )}
        <GuardRow title="Acesso controlado a pastas do Windows" risk="medium" on={g.cfa === 'on'} disabled={!g.admin || g.cfa === 'unavailable'} onChange={v => set('cfa', v)}
          desc={g.cfa === 'unavailable' ? 'Indisponível: precisa do Microsoft Defender ativo.' : 'Bloqueio do próprio Windows (Defender): só programas confiáveis podem alterar as pastas protegidas. Alguns jogos e programas podem precisar de liberação em Segurança do Windows.'} />
        <GuardRow title={'"Verificar com GROBS" no clique direito'} risk="low" on={g.contextMenu} onChange={v => set('contextMenu', v)}
          desc="Adiciona a opção no menu de arquivos, pastas e unidades do Explorador (no Windows 11, em Mostrar mais opções)." />
      </div>
    </Card>
  );
}

function QuarantineCard() {
  const [g, setG] = useGuard();
  if (!g || g.quarantine.length === 0) return null;
  const act = async (m: string, id: string, ok: string) => { const r = await run(call<Guard>(m, { id }), ok); if (r) setG(r); };
  return (
    <Card className="pad-0">
      <div style={{ padding: '16px 18px 6px' }}><CardTitle icon="lock" color="#fbbf24">Quarentena ({g.quarantine.length})</CardTitle>
        <div className="small dim" style={{ marginTop: -6, marginBottom: 6 }}>Arquivos isolados e cifrados: não executam. Restaure só se tiver certeza de que o arquivo é seguro.</div></div>
      {g.quarantine.map(q => (
        <div key={q.id} className="li">
          <div className="grow"><div className="t ellipsis">{q.path.split('\\').pop()}</div><div className="s ellipsis selectable">{q.path}</div><div className="s">{q.reason} · {bytes(q.size)} · {q.date}</div></div>
          <button className="btn sm" onClick={() => act('avg.restore', q.id, 'Arquivo restaurado')}><Icon name="restore" size={13} />Restaurar</button>
          <button className="btn sm danger" onClick={() => act('avg.deleteQ', q.id, 'Apagado de vez')}><Icon name="trash" size={13} />Apagar</button>
        </div>
      ))}
    </Card>
  );
}

function RansomAlert() {
  const [g, setG] = useGuard();
  const a = g?.alert;
  const act = async (action: string) => { const r = await run(call<Guard>('avg.resolve', { action })); if (r) setG(r); };
  return (
    <Modal open={!!a} onClose={() => {}}>
      <h3 className="bad-t">Possível ransomware</h3>
      <p className="muted" style={{ marginTop: 0 }}>{a?.reason} ({a?.time}).</p>
      {a?.name ? (
        <p className="muted">O programa que mais escreveu no disco agora foi <b>{a.name}</b>{a.written ? ` (${bytes(a.written)} em 1,5 s)` : ''}{a.suspended ? ' e ele foi pausado.' : ', mas não deu para pausá-lo.'}<br /><span className="mono small selectable">{a.path}</span></p>
      ) : <p className="muted">Não deu para identificar o programa responsável. Se você estava copiando ou baixando muitos arquivos, pode ignorar.</p>}
      <div className="row wrap"><button className="btn ghost" onClick={() => act('resume')}>É seguro, continuar</button><span className="spacer" />
        {a?.name && <><button className="btn danger" onClick={() => act('kill')}>Encerrar</button><button className="btn danger" onClick={() => act('quarantine')}>Encerrar e pôr em quarentena</button></>}</div>
    </Modal>
  );
}

export default function Antivirus() {
  const [av, setAv] = useState<Av | null>(null);
  const [key, setKey] = useState('');
  const [keyOpen, setKeyOpen] = useState(false);
  const [confirm, setConfirm] = useState<Hit | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    call<Av>('av.status').then(setAv).catch(() => {});
    const off = on('av', (s: Av) => {
      setAv(prev => {
        if (prev?.running && !s.running) {
          if (s.error) toast(s.error, 'err');
          else if (s.finished === 'update') toast(`Base atualizada: ${s.sigs.toLocaleString('pt-BR')} assinaturas`);
          else if (s.finished === 'scan') toast(s.hits.length ? `${s.hits.length} item(ns) encontrado(s)` : 'Nenhuma ameaça encontrada', s.hits.length ? 'warn' : 'ok');
        }
        return s;
      });
    });
    return off;
  }, []);

  async function scan(type: string) {
    let path = '';
    if (type === 'custom') { path = await call<string>('shell.pickFolder'); if (!path) return; }
    const r = await run(call<Av>('av.scan', { type, path }));
    if (r) setAv(r);
  }
  async function update() { const r = await run(call<Av>('av.update')); if (r) setAv(r); }
  async function remove(h: Hit, force = false) {
    setConfirm(null); setBusy(h.path);
    const r = await run(call<Av>('av.remove', { path: h.path, force }), force ? 'Arquivo apagado' : 'Arquivo enviado para a Lixeira');
    setBusy(null); if (r) setAv(r);
  }
  async function ignore(h: Hit) { const r = await run(call<Av>('av.ignore', { path: h.path }), 'Não será mais apontado'); if (r) setAv(r); }
  async function saveKey(k = key) { const r = await run(call<Av>('av.setKey', { key: k }), k ? 'Chave salva' : 'Chave removida'); if (r) setAv(r); setKey(''); setKeyOpen(false); }

  const scanning = av?.running && av.type !== 'update';
  const pctDone = av && av.files ? Math.min(100, Math.round((av.done / av.files) * 100)) : 0;

  return (
    <div className="page">
      <PageHead title="Antivírus" sub="Motor próprio do GROBS: compara seus arquivos com milhões de assinaturas de malware conhecido e procura sinais típicos de vírus, sem usar o Defender." />

      <div className="grid g2">
        <Card>
          <CardTitle icon="shieldCheck" color="#4ade80">Verificar</CardTitle>
          {av?.running ? (
            <div>
              <div className="row" style={{ marginBottom: 8 }}><Spinner size={16} /><b>{av.phase}</b><span className="spacer" /><span className="mono">{fmt(av.elapsed)}</span></div>
              {scanning && av.files > 0 && <>
                <div className="bar" style={{ marginBottom: 6 }}><i style={{ ['--v' as any]: pctDone / 100 }} /></div>
                <div className="small dim ellipsis selectable">{av.done.toLocaleString('pt-BR')} de {av.files.toLocaleString('pt-BR')} arquivos · {av.current}</div>
              </>}
              <button className="btn sm" style={{ marginTop: 10 }} onClick={() => call('av.cancel')}><Icon name="stop" size={13} />Cancelar</button>
            </div>
          ) : (
            <>
              <div className="row wrap">
                <button className="btn primary" onClick={() => scan('quick')} disabled={!av}><Icon name="zap" size={15} />Rápida</button>
                <button className="btn" onClick={() => scan('full')} disabled={!av}><Icon name="disk" size={15} />Completa</button>
                <button className="btn" onClick={() => scan('custom')} disabled={!av}><Icon name="folder" size={15} />Escolher pasta</button>
              </div>
              <p className="small dim" style={{ marginBottom: 0 }}>A rápida olha o que inicia com o Windows, os programas abertos, Downloads, Área de Trabalho, pastas temporárias e AppData. A completa olha todos os discos e pode levar bastante tempo.</p>
            </>
          )}
          {av && !av.admin && <div style={{ marginTop: 10 }}><AdminLock text="Sem administrador, pastas de outros usuários e do sistema ficam de fora" /></div>}
        </Card>

        <Card>
          <CardTitle icon="layers" color="#a78bfa" right={<button className="btn sm" onClick={update} disabled={!av || av.running}><Icon name="download" size={13} />Atualizar</button>}>Base de assinaturas</CardTitle>
          {!av ? <Spinner /> : (
            <div className="small muted">
              <div className="stat-big" style={{ color: av.sigs ? 'var(--text)' : 'var(--warn)' }}>{av.sigs ? av.sigs.toLocaleString('pt-BR') : 'Vazia'}</div>
              <div style={{ marginTop: 6 }}>{av.sigs ? `Atualizada em ${av.sigDate}.` : 'Baixe a base antes da primeira verificação (cerca de 100 MB de download, 25 MB no disco). Sem ela, só a heurística funciona.'}</div>
              <div style={{ marginTop: 6 }}>Fontes: listas públicas da VirusShare (amostras mais recentes){av.bazaar ? ', MalwareBazaar (últimas 48 h)' : ''} e regras YARA da ReversingLabs (famílias de malware, licença MIT; atualizadas junto).{' '}
                <button className="btn ghost sm" style={{ padding: 0, height: 'auto' }} onClick={() => setKeyOpen(true)}>{av.bazaar ? 'Trocar chave da MalwareBazaar' : 'Adicionar MalwareBazaar (chave grátis)'}</button>
              </div>
              {av.ignored > 0 && <div style={{ marginTop: 6 }}>{av.ignored} item(ns) ignorado(s). <button className="btn ghost sm" style={{ padding: 0, height: 'auto' }} onClick={async () => { const r = await run(call<Av>('av.clearIgnored'), 'Lista de ignorados limpa'); if (r) setAv(r); }}>Limpar</button></div>}
            </div>
          )}
        </Card>
      </div>

      <h2 className="sec"><Icon name="alert" />Detecções</h2>
      <Card className="pad-0">
        {!av || av.hits.length === 0 ? (
          <div className="empty">{av?.finished === 'scan' ? 'Nenhuma ameaça encontrada na última verificação.' : av?.running ? 'Nada encontrado até agora…' : 'Faça uma verificação para ver os resultados aqui.'}</div>
        ) : av.hits.map(h => (
          <div key={h.path} className="li">
            <span className={`risk ${KIND[h.kind][1] === 'bad' ? 'high' : KIND[h.kind][1] === 'warn' ? 'medium' : 'low'}`}>{KIND[h.kind][0]}</span>
            <div className="grow">
              <div className="t ellipsis selectable" title={h.path}>{h.path.split('\\').pop()}</div>
              <div className="s ellipsis selectable" title={h.path}>{h.path}</div>
              <div className="s">{h.reason} · {bytes(h.size)} · {h.source}</div>
            </div>
            {h.md5 && <a className="btn sm ghost" href={`https://www.virustotal.com/gui/file/${h.md5}`} target="_blank" rel="noreferrer" title="Ver o que outros antivírus dizem deste arquivo (só consulta o hash, nada é enviado)"><Icon name="external" size={13} />VirusTotal</a>}
            <button className="btn sm ghost" onClick={() => call('shell.reveal', { path: h.path })} title="Abrir local"><Icon name="folder" size={13} /></button>
            <button className="btn sm" disabled={!!busy} onClick={() => ignore(h)}>Ignorar</button>
            <button className="btn sm" disabled={!!busy} onClick={async () => { const r = await run(call('avg.quarantine', { path: h.path }), 'Arquivo isolado na quarentena'); if (r) call<Av>('av.status').then(setAv); }}><Icon name="lock" size={13} />Quarentena</button>
            <button className="btn sm danger" disabled={!!busy} onClick={() => setConfirm(h)}>{busy === h.path ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Apagar</button>
          </div>
        ))}
      </Card>
      <p className="small dim">A heurística aponta comportamentos suspeitos, não certezas: confira no VirusTotal antes de apagar um programa que você conhece. "Malware conhecido" é um arquivo idêntico, byte a byte, a uma amostra catalogada.</p>

      <h2 className="sec"><Icon name="shieldCheck" />Proteção contínua</h2>
      <div className="grid" style={{ gap: 16 }}><GuardCard /><QuarantineCard /></div>
      <RansomAlert />

      <h2 className="sec"><Icon name="shield" />Proteção do Windows</h2>
      <DefenderCard />

      <Modal open={!!confirm} onClose={() => setConfirm(null)}>
        <h3>Apagar este arquivo?</h3>
        <p className="muted selectable" style={{ marginTop: 0, wordBreak: 'break-all' }}>{confirm?.path}</p>
        <p className="muted">Se o programa estiver aberto, ele é fechado. O arquivo vai para a Lixeira, de onde dá para recuperar. Use "Apagar à força" se ele estiver travado.</p>
        <div className="row"><button className="btn ghost" onClick={() => confirm && remove(confirm, true)}>Apagar à força</button><span className="spacer" /><button className="btn ghost" onClick={() => setConfirm(null)}>Cancelar</button><button className="btn danger" onClick={() => confirm && remove(confirm)}>Mandar para a Lixeira</button></div>
      </Modal>

      <Modal open={keyOpen} onClose={() => setKeyOpen(false)}>
        <h3>Chave da MalwareBazaar</h3>
        <p className="muted" style={{ marginTop: 0 }}>A MalwareBazaar (abuse.ch) publica malware novo a cada poucos minutos. A chave é grátis: crie em <a href="https://auth.abuse.ch/" target="_blank" rel="noreferrer">auth.abuse.ch</a> e cole aqui. Ela fica cifrada com a sua conta do Windows.</p>
        <input type="password" value={key} onChange={e => setKey(e.target.value)} placeholder="Auth-Key" style={{ width: '100%', marginBottom: 12 }} />
        <div className="row">{av?.bazaar && <button className="btn ghost" onClick={() => saveKey('')}>Remover chave</button>}<span className="spacer" /><button className="btn ghost" onClick={() => setKeyOpen(false)}>Cancelar</button><button className="btn primary" disabled={!key.trim()} onClick={() => saveKey()}>Salvar</button></div>
      </Modal>
    </div>
  );
}
