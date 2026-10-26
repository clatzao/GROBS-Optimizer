import { useEffect, useRef, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Beam, Card, CardTitle, Gauge, PageHead, Spinner, Stagger } from '../components/ui';
import { run, toast } from '../store';

type Status = any;

function fmtTime(s: number) { const m = Math.floor(s / 60); return `${m}:${String(s % 60).padStart(2, '0')}`; }

export default function Security() {
  const [st, setSt] = useState<Status | null>(null);
  const [audit, setAudit] = useState<any[] | null>(null);
  const [threats, setThreats] = useState<any | null>(null);
  const [scan, setScan] = useState<any>(null);
  const [updating, setUpdating] = useState(false);
  const tick = useRef<number | undefined>(undefined);

  const load = () => call('sec.status').then((s: any) => { setSt(s); setScan(s.scan); }).catch(() => setSt({ defender: false }));
  useEffect(() => {
    load();
    call<any[]>('sec.audit').then(setAudit).catch(() => setAudit([]));
    call('sec.threats').then(setThreats).catch(() => {});
    const off = on('scanDone', (r: any) => {
      setScan({ running: false, last: r });
      toast(r.threats ? 'Ameaças encontradas! O Defender já tratou os itens.' : r.ok ? 'Verificação concluída: nenhuma ameaça.' : 'Verificação encerrada.', r.threats ? 'warn' : r.ok ? 'ok' : 'info');
      load(); call('sec.threats').then(setThreats).catch(() => {});
    });
    return () => { off(); clearInterval(tick.current); };
  }, []);

  useEffect(() => {
    clearInterval(tick.current);
    if (scan?.running) tick.current = window.setInterval(() => setScan((s: any) => (s?.running ? { ...s, elapsed: (s.elapsed ?? 0) + 1 } : s)), 1000);
  }, [scan?.running]);

  async function start(type: string) {
    let path = '';
    if (type === 'custom') { path = await call<string>('shell.pickFolder'); if (!path) return; }
    const r = await run(call('sec.scan', { type, path }));
    if (r) setScan(r);
  }
  async function update() {
    setUpdating(true);
    await run(call('sec.update'), 'Definições atualizadas');
    setUpdating(false); load();
  }

  if (!st) return <div className="page"><div className="empty"><Spinner size={26} /></div></div>;
  const other = (st.products ?? []).filter((p: any) => !/defender/i.test(p.name) && p.enabled);
  const protectedOk = st.realtime || other.length > 0;
  const fw = st.firewall ?? {};
  const fwOk = fw.domain && fw.private && fw.public;
  const checks = [
    { ok: protectedOk, label: 'Antivírus em tempo real', detail: other.length ? other.map((p: any) => p.name).join(', ') : st.realtime ? 'Microsoft Defender' : 'Desativado' },
    { ok: st.sigAge !== undefined ? st.sigAge <= 3 : true, label: 'Definições de vírus', detail: st.sigVersion ? `${st.sigVersion} · ${st.sigAge === 0 ? 'hoje' : `há ${st.sigAge} dia(s)`}` : 'Gerenciadas pelo antivírus ativo' },
    { ok: fwOk, label: 'Firewall do Windows', detail: fwOk ? 'Ativo em todas as redes' : 'Desativado em algum perfil' },
    { ok: st.tamper !== false, label: 'Proteção contra adulteração', detail: st.tamper ? 'Ativa' : 'Desativada' },
    { ok: st.behavior !== false, label: 'Monitoramento de comportamento', detail: st.behavior ? 'Ativo' : 'Desativado' },
    { ok: st.uac, label: 'Controle de Conta de Usuário', detail: st.uac ? 'Ativo' : 'Desativado' },
  ];
  const okCount = checks.filter(c => c.ok).length;
  const score = Math.round((okCount / checks.length) * 100);

  return (
    <div className="page">
      <PageHead title="Segurança" sub="Usa o motor do Microsoft Defender (99% de proteção nos testes independentes) — sem um segundo antivírus pesando o PC."
        right={<button className="btn" onClick={update} disabled={updating || !st.defender}>{updating ? <Spinner size={14} /> : <Icon name="refresh" size={15} />}Atualizar definições</button>} />

      <div className="grid" style={{ gridTemplateColumns: '1.1fr 1fr', gap: 16 }}>
        <Card style={{ padding: 24, overflow: 'hidden' }}>
          {scan?.running && <Beam size={180} duration={3} c1="#34d399" c2="#22d3ee" />}
          <div className="row" style={{ gap: 22 }}>
            <Gauge value={scan?.running ? 100 : score} size={140} stroke={8} colors={protectedOk && fwOk ? ['#34d399', '#22d3ee'] : ['#fbbf24', '#f87171']}>
              {scan?.running ? <div className="tri-loader" /> : <Icon name={protectedOk && fwOk ? 'shieldCheck' : 'shield'} size={46} stroke={1.5} style={{ color: protectedOk && fwOk ? 'var(--ok)' : 'var(--warn)' }} />}
            </Gauge>
            <div style={{ flex: 1 }}>
              <div className="stat-big" style={{ fontSize: 24 }}>{scan?.running ? `Verificação ${scan.type === 'full' ? 'completa' : scan.type === 'custom' ? 'personalizada' : 'rápida'} em andamento` : protectedOk && fwOk ? 'Você está protegido' : 'Proteção incompleta'}</div>
              <div className="muted" style={{ margin: '6px 0 14px' }}>
                {scan?.running ? `Tempo decorrido: ${fmtTime(scan.elapsed ?? 0)} — você pode continuar usando o PC.` : st.lastQuick ? `Última verificação rápida: ${st.lastQuick}` : 'Nenhuma verificação registrada.'}
              </div>
              {!st.defender ? <div className="small warn-t">Microsoft Defender indisponível neste PC{other.length ? ' (outro antivírus está no controle)' : ''}.</div> : scan?.running ? (
                <button className="btn danger" onClick={() => call('sec.cancel')}><Icon name="stop" size={14} />Cancelar</button>
              ) : (
                <div className="row wrap">
                  <button className="btn primary" onClick={() => start('quick')}><Icon name="zap" size={15} />Verificação rápida</button>
                  <button className="btn" onClick={() => start('full')}><Icon name="shield" size={15} />Completa</button>
                  <button className="btn" onClick={() => start('custom')}><Icon name="folder" size={15} />Pasta…</button>
                </div>
              )}
              {scan?.last && !scan.running && (
                <div className={`small ${scan.last.threats ? 'warn-t' : 'ok-t'}`} style={{ marginTop: 10 }}>
                  Última verificação pelo GROBS ({scan.last.when}): {scan.last.threats ? 'ameaças encontradas' : scan.last.ok ? 'nenhuma ameaça' : `encerrada (código ${scan.last.code})`}
                </div>
              )}
            </div>
          </div>
        </Card>
        <Card>
          <CardTitle icon="shieldCheck" color="#34d399" right={<span className="small dim">{okCount}/{checks.length}</span>}>Proteções</CardTitle>
          <div className="list">
            {checks.map(c => (
              <div key={c.label} className="row" style={{ padding: '7px 0', borderBottom: '1px solid var(--line)' }}>
                <span className={`dot ${c.ok ? 'ok' : 'bad'}`} />
                <span style={{ flex: 1 }}>{c.label}</span>
                <span className="small dim ellipsis" style={{ maxWidth: 200 }}>{c.detail}</span>
              </div>
            ))}
          </div>
        </Card>
      </div>

      <div className="grid g2" style={{ marginTop: 16 }}>
        <Card>
          <CardTitle icon="search" color="#a78bfa">Verificação de integridade</CardTitle>
          {!audit ? <div className="empty" style={{ padding: 20 }}><Spinner /></div> : (
            <Stagger className="list">
              {audit.map((a, i) => (
                <div key={i} className="row" style={{ alignItems: 'flex-start', padding: '8px 0', borderBottom: '1px solid var(--line)' }}>
                  <Icon name={a.level === 'ok' ? 'check' : a.level === 'info' ? 'info' : 'alert'} size={16} className={a.level === 'ok' ? 'ok-t' : a.level === 'info' ? 'info-t' : a.level === 'warn' ? 'warn-t' : 'bad-t'} style={{ marginTop: 2 }} />
                  <div style={{ flex: 1, minWidth: 0 }}><div>{a.title}</div>{a.detail && <div className="small dim selectable" style={{ whiteSpace: 'pre-line', wordBreak: 'break-word' }}>{a.detail}</div>}</div>
                </div>
              ))}
            </Stagger>
          )}
        </Card>
        <Card>
          <CardTitle icon="alert" color="#fbbf24">Histórico de ameaças</CardTitle>
          {!threats ? <div className="empty" style={{ padding: 20 }}><Spinner /></div>
            : !threats.ok ? <div className="small dim">O histórico do Defender só pode ser lido como administrador.</div>
              : threats.items.length === 0 ? <div className="row ok-t" style={{ padding: '8px 0' }}><Icon name="check" />Nenhuma ameaça registrada.</div>
                : (
                  <div className="list">
                    {threats.items.map((t: any, i: number) => (
                      <div key={i} style={{ padding: '8px 0', borderBottom: '1px solid var(--line)' }}>
                        <div className="row"><b className="ellipsis" style={{ flex: 1 }}>{t.name}</b><span className={`chip ${t.handled ? 'ok' : 'bad'}`}>{t.handled ? 'tratada' : 'pendente'}</span></div>
                        <div className="small dim selectable" style={{ wordBreak: 'break-all' }}>{t.when} · {t.resources}</div>
                      </div>
                    ))}
                  </div>
                )}
        </Card>
      </div>
    </div>
  );
}
