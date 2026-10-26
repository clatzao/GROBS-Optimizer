import { useState } from 'react';
import { call } from '../bridge';
import { Icon } from '../components/Icon';
import DecryptedText from '../components/rb/DecryptedText';
import { Beam, Card, Modal, PageHead, Spinner, Stagger } from '../components/ui';
import { run, toast } from '../store';
import type { PageId } from '../App';

type Issue = { id: string; level: 'bad' | 'warn' | 'info'; area: string; title: string; detail: string; advice: string; fix?: string; fixLabel?: string };
type Result = { issues: Issue[]; bad: number; warn: number; when: string };

let cache: Result | null = null;

export default function Diagnose({ go }: { go: (p: PageId) => void }) {
  const [res, setRes] = useState<Result | null>(cache);
  const [busy, setBusy] = useState(false);
  const [fixing, setFixing] = useState<string | null>(null);

  async function scan() {
    setBusy(true);
    const r = await run(call<Result>('diag.scan'));
    if (r) { cache = r; setRes(r); }
    setBusy(false);
  }

  const [done, setDone] = useState<Set<string>>(new Set());
  const [confirmRestart, setConfirmRestart] = useState<Issue | null>(null);
  // correções silenciosas e seguras, usadas pelo "Resolver tudo" (as que abrem janelas ou reiniciam ficam de fora)
  const AUTO = (f?: string) => !!f && (f === 'flushDns' || f === 'cleanWu' || f === 'fastStartup' || f.startsWith('enableDevice:'));
  const OPENS = ['sfc', 'dism', 'chkdsk', 'memtest', 'reliability'];

  async function fix(i: Issue, quiet = false) {
    if (!i.fix) return false;
    if (i.fix === 'drivers') { go('drivers'); return false; }
    if (i.fix === 'cleaner') { go('clean'); return false; }
    if (i.fix === 'restart' && !quiet) { setConfirmRestart(i); return false; }
    setFixing(i.id);
    const r = await run(call('diag.fix', { fix: i.fix }));
    setFixing(null);
    if (r === undefined) return false;
    setDone(d => new Set(d).add(i.id));
    if (!quiet) toast(OPENS.includes(i.fix) ? 'A correção foi aberta numa nova janela.' : i.fix === 'restart' ? 'O PC vai reiniciar em 1 minuto.' : 'Problema resolvido.');
    return true;
  }
  async function fixAll() {
    let n = 0;
    for (const i of (res?.issues ?? []).filter(x => AUTO(x.fix) && !done.has(x.id))) if (await fix(i, true)) n++;
    toast(n > 0 ? `${n} problema(s) resolvido(s) automaticamente` : 'Nada a resolver automaticamente — veja as recomendações de cada item.', n > 0 ? 'ok' : 'info');
  }
  const autoCount = (res?.issues ?? []).filter(x => AUTO(x.fix) && !done.has(x.id)).length;

  const color = { bad: '#f87171', warn: '#fbbf24', info: '#60a5fa' };
  return (
    <div className="page">
      <PageHead title="Diagnóstico" sub="Lê os registros do Windows, o Gerenciador de Dispositivos e o sistema para achar erros — e resolve."
        right={<>
          {autoCount > 0 && <button className="btn primary" onClick={fixAll} disabled={busy || !!fixing}><Icon name="wrench" size={15} />Resolver tudo ({autoCount})</button>}
          <button className={`btn ${autoCount > 0 ? '' : 'primary'}`} onClick={() => { setDone(new Set()); scan(); }} disabled={busy}>{busy ? <Spinner size={14} /> : <Icon name="search" size={15} />}{res ? 'Analisar de novo' : 'Analisar agora'}</button>
        </>} />

      <Card style={{ padding: 24, marginBottom: 16, overflow: 'hidden' }}>
        {busy && <Beam size={180} duration={2.5} />}
        <div className="row" style={{ gap: 22 }}>
          <div style={{ width: 70, height: 70, borderRadius: 20, display: 'grid', placeItems: 'center', background: 'var(--surface-3)' }}>
            {busy ? <div className="tri-loader" style={{ transform: 'scale(.7)' }} /> : <Icon name={!res ? 'search' : res.bad ? 'alert' : res.warn ? 'info' : 'check'} size={30} style={{ color: !res ? '#a78bfa' : res.bad ? color.bad : res.warn ? color.warn : 'var(--ok)' }} />}
          </div>
          <div style={{ flex: 1 }}>
            <div className="stat-big" style={{ fontSize: 24 }}>
              {busy ? <DecryptedText text="Lendo registros do sistema…" animateOn="view" speed={40} sequential />
                : !res ? 'Pronto para analisar' : res.issues.length === 0 ? 'Nenhum problema encontrado' : `${res.issues.length} ponto(s) de atenção`}
            </div>
            <div className="muted small" style={{ marginTop: 4 }}>
              {busy ? 'Telas azuis, desligamentos, erros de disco e hardware, dispositivos com falha, travamentos de programas, Windows Update e rede.'
                : res ? `${res.bad} grave(s) · ${res.warn} aviso(s) · análise de ${res.when}` : 'A análise só lê informações. Nenhuma correção é aplicada sem você clicar.'}
            </div>
          </div>
        </div>
      </Card>

      {res && (
        <Stagger className="list" delay={0.04}>
          {res.issues.map(i => (
            <Card key={i.id} style={{ marginBottom: 10, borderLeft: `3px solid ${color[i.level]}` }}>
              <div className="row" style={{ alignItems: 'flex-start', gap: 14 }}>
                <Icon name={i.level === 'info' ? 'info' : 'alert'} size={18} style={{ color: color[i.level], marginTop: 2 }} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}><b>{i.title}</b><span className="chip">{i.area}</span></div>
                  <div className="small dim selectable" style={{ marginTop: 3 }}>{i.detail}</div>
                  <div className="small" style={{ marginTop: 6 }}><Icon name="sparkles" size={12} style={{ color: '#a78bfa' }} /> {i.advice}</div>
                </div>
                {done.has(i.id) ? <span className="chip ok"><Icon name="check" size={11} />{i.fix && OPENS.includes(i.fix) ? 'aberto' : 'resolvido'}</span>
                  : i.fix && <button className="btn sm primary" disabled={!!fixing} onClick={() => fix(i)}>{fixing === i.id ? <Spinner size={12} /> : <Icon name="wrench" size={13} />}Resolver: {i.fixLabel}</button>}
              </div>
            </Card>
          ))}
        </Stagger>
      )}
      <Modal open={!!confirmRestart} onClose={() => setConfirmRestart(null)}>
        <h3>Reiniciar o PC?</h3>
        <p className="muted" style={{ marginTop: 0 }}>Salve seus trabalhos. O Windows reinicia em 1 minuto para concluir as atualizações pendentes.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setConfirmRestart(null)}>Agora não</button>
          <button className="btn primary" onClick={() => { const i = confirmRestart!; setConfirmRestart(null); fix(i, true).then(ok => ok && toast("O PC vai reiniciar em 1 minuto. Para cancelar, execute: shutdown /a", "info")); }}>Reiniciar em 1 min</button></div>
      </Modal>
    </div>
  );
}
