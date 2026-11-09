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
              <div style={{ marginTop: 6 }}>Fontes: listas públicas da VirusShare (amostras mais recentes){av.bazaar ? ' e MalwareBazaar (últimas 48 h).' : '.'}{' '}
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
            <button className="btn sm danger" disabled={!!busy} onClick={() => setConfirm(h)}>{busy === h.path ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Apagar</button>
          </div>
        ))}
      </Card>
      <p className="small dim">A heurística aponta comportamentos suspeitos, não certezas: confira no VirusTotal antes de apagar um programa que você conhece. "Malware conhecido" é um arquivo idêntico, byte a byte, a uma amostra catalogada.</p>

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
