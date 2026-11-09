import { useEffect, useMemo, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { AdminLock, Beam, Card, CardTitle, Check, Modal, PageHead, Spinner, Tabs, Toggle } from '../components/ui';
import { bytes } from '../format';
import { appStore, run, toast, useStore } from '../store';

type Upd = { id: string; title: string; maker: string; model: string; cls: string; date: string; size: number };
type St = { state: 'idle' | 'searching' | 'installing'; progress: string; error: string | null; items: Upd[] | null; when: string | null; lastInstall: { ok: number; total: number; reboot: boolean } | null };
type Gpu = { name: string; vendor: string; installed: string; date: string; latest?: string; url?: string; released?: string; outdated?: boolean; error?: string; page?: string; tool?: string; note?: string };
type Inst = { name: string; provider: string; version: string; date: string; cls: string; years: number };

function GpuCard() {
  const [gpus, setGpus] = useState<Gpu[] | null>(null);
  const [dl, setDl] = useState<any>(null);
  useEffect(() => {
    call<Gpu[]>('gpu.check').then(setGpus).catch(() => setGpus([]));
    return on('gpuDownload', setDl);
  }, []);
  useEffect(() => {
    if (dl?.state !== 'downloading') return;
    const t = window.setInterval(() => call('gpu.downloadStatus').then(setDl), 700);
    return () => clearInterval(t);
  }, [dl?.state]);

  async function download(url: string) {
    const r = await run(call('gpu.download', { url }));
    if (r) setDl(r);
  }
  const color: Record<string, string> = { nvidia: '#76b900', amd: '#ed1c24', intel: '#0071c5' };
  return (
    <Card>
      <CardTitle icon="gpu" color="#f472b6">Placa de vídeo — direto do fabricante</CardTitle>
      {!gpus ? <div className="row dim small"><Spinner size={14} />Consultando fabricantes…</div> : gpus.length === 0 ? <div className="small dim">Nenhuma GPU reconhecida.</div> : (
        <div className="list" style={{ gap: 10 }}>
          {gpus.map(g => (
            <div key={g.name} style={{ padding: 14, borderRadius: 12, background: 'var(--surface-2)', border: '1px solid var(--line)' }}>
              <div className="row" style={{ marginBottom: 6 }}>
                <span className="chip" style={{ color: color[g.vendor], borderColor: `${color[g.vendor]}55` }}>{g.vendor.toUpperCase()}</span>
                <b className="ellipsis" style={{ flex: 1 }}>{g.name}</b>
                {g.outdated === true && <span className="chip warn">atualização disponível</span>}
                {g.outdated === false && <span className="chip ok">atualizado</span>}
              </div>
              <div className="small muted">Instalado: <b>{g.installed}</b>{g.date && ` (${g.date})`}{g.latest && <> · Mais recente: <b>{g.latest}</b>{g.released && ` (${g.released})`}</>}</div>
              {g.note && <div className="small dim" style={{ marginTop: 4 }}>{g.note}</div>}
              {g.error && <div className="small warn-t" style={{ marginTop: 4 }}>{g.error}</div>}
              <div className="row wrap" style={{ marginTop: 10 }}>
                {g.url && <button className={`btn sm ${g.outdated ? 'primary' : ''}`} onClick={() => download(g.url!)} disabled={dl?.state === 'downloading'}><Icon name="arrowDown" size={13} />Baixar e instalar {g.latest}</button>}
                {g.tool && <button className="btn sm primary" onClick={() => download(g.tool!)} disabled={dl?.state === 'downloading'}><Icon name="arrowDown" size={13} />Baixar assistente da Intel</button>}
                {g.page && <a className="btn sm" href={g.page} target="_blank" rel="noreferrer"><Icon name="external" size={13} />Site oficial</a>}
              </div>
            </div>
          ))}
        </div>
      )}
      {dl?.state === 'downloading' && (
        <div style={{ marginTop: 12 }}>
          <div className="small muted" style={{ marginBottom: 6 }}>Baixando… {bytes(dl.done)}{dl.total > 0 ? ` de ${bytes(dl.total)}` : ''}</div>
          <div className="bar"><i style={{ ['--v' as any]: dl.total > 0 ? dl.done / dl.total : 0.1 }} /></div>
        </div>
      )}
      {dl?.state === 'done' && <div className="small ok-t" style={{ marginTop: 10 }}><Icon name="check" size={12} /> Instalador aberto — siga as etapas na janela do fabricante.</div>}
      {dl?.state?.startsWith('error') && <div className="small bad-t" style={{ marginTop: 10 }}>{dl.state}</div>}
    </Card>
  );
}

type Missing = { name: string; code: number; hwid: string; search: string; cls: string; catalog: string; reason: string };

function MissingCard() {
  const [list, setList] = useState<Missing[] | null>(null);
  useEffect(() => { call<Missing[]>('drv.missing').then(setList).catch(() => setList([])); }, []);
  return (
    <Card style={{ borderColor: list && list.length > 0 ? 'rgba(251,191,36,.35)' : undefined }}>
      <CardTitle icon="alert" color="#fbbf24">Dispositivos que precisam de driver</CardTitle>
      {!list ? <div className="row dim small"><Spinner size={14} />Verificando o Gerenciador de Dispositivos…</div>
        : list.length === 0 ? <div className="row ok-t small"><Icon name="check" size={14} />Todos os dispositivos têm driver funcionando.</div>
          : (
            <>
              <div className="small muted" style={{ marginBottom: 8 }}>A busca abaixo (“Buscar drivers”) já procura no Windows Update os drivers destes dispositivos. Se não achar, use o Catálogo da Microsoft com o ID de hardware.</div>
              <div className="list">
                {list.map((d, i) => (
                  <div key={i} className="li" style={{ padding: '9px 0' }}>
                    <div className="grow">
                      <div className="t ellipsis">{d.name}</div>
                      <div className="s"><span className="warn-t">{d.reason}</span> · <span className="mono selectable">{d.search}</span>{d.cls && ` · ${d.cls}`}</div>
                    </div>
                    <a className="btn sm" href={d.catalog} target="_blank" rel="noreferrer"><Icon name="external" size={13} />Catálogo Microsoft</a>
                  </div>
                ))}
              </div>
            </>
          )}
    </Card>
  );
}

function CatalogTab() {
  const [st, setSt] = useState<St | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const admin = useStore(appStore, a => a?.admin);
  useEffect(() => {
    call<St>('drv.status').then(setSt);
    return on('drivers', (s: St) => { setSt(s); if (s.lastInstall && s.state === 'idle') toast(`${s.lastInstall.ok} de ${s.lastInstall.total} driver(s) instalados${s.lastInstall.reboot ? ' — reinicie o PC' : ''}`, s.lastInstall.ok === s.lastInstall.total ? 'ok' : 'warn'); });
  }, []);
  useEffect(() => {
    if (st?.state === 'idle') return;
    const t = window.setInterval(() => call<St>('drv.status').then(setSt), 1500);
    return () => clearInterval(t);
  }, [st?.state]);

  async function search() { const r = await run(call<St>('drv.search')); if (r) { setSt(r); setSel(new Set()); } }
  async function install() { const r = await run(call<St>('drv.install', { ids: [...sel] })); if (r) setSt(r); }

  if (!st) return <div className="empty"><Spinner /></div>;
  const busy = st.state !== 'idle';
  return (
    <>
      <Card style={{ padding: 22, marginBottom: 16, overflow: 'hidden' }}>
        {busy && <Beam size={180} duration={2.5} />}
        <div className="row" style={{ gap: 20 }}>
          <div style={{ width: 60, height: 60, borderRadius: 18, display: 'grid', placeItems: 'center', background: 'var(--surface-3)' }}>{busy ? <div className="tri-loader" style={{ transform: 'scale(.65)' }} /> : <Icon name="cpu" size={28} style={{ color: '#a78bfa' }} />}</div>
          <div style={{ flex: 1 }}>
            <div className="stat-big" style={{ fontSize: 22 }}>{busy ? (st.state === 'installing' ? 'Instalando drivers…' : 'Procurando drivers…') : st.items ? `${st.items.length} atualização(ões) de driver` : 'Catálogo oficial do Windows Update'}</div>
            <div className="small muted" style={{ marginTop: 4 }}>{busy ? st.progress : st.items ? `Última busca: ${st.when}` : 'Drivers testados e assinados pela Microsoft para o seu hardware exato — sem sites de terceiros.'}</div>
            {st.error && <div className="small bad-t" style={{ marginTop: 6 }}>{st.error}</div>}
          </div>
          <button className="btn primary" onClick={search} disabled={busy}><Icon name="search" size={15} />{st.items ? 'Buscar de novo' : 'Buscar drivers'}</button>
        </div>
      </Card>
      {st.items && st.items.length > 0 && (
        <Card className="pad-0">
          <div className="row" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}>
            <Check on={sel.size === st.items.length} onChange={v => setSel(v ? new Set(st.items!.map(i => i.id)) : new Set())} />
            <b>Selecionar todos</b><span className="spacer" />
            {!admin && <span className="chip warn"><Icon name="lock" size={11} />Instalar requer admin</span>}
            <button className="btn sm primary" disabled={!admin || sel.size === 0 || busy} onClick={install}><Icon name="arrowDown" size={13} />Instalar {sel.size || ''}</button>
          </div>
          <div className="list">
            {st.items.map(u => (
              <label key={u.id} className="li" style={{ cursor: 'pointer' }}>
                <Check on={sel.has(u.id)} onChange={v => setSel(s => { const n = new Set(s); v ? n.add(u.id) : n.delete(u.id); return n; })} />
                <div className="grow"><div className="t ellipsis">{u.model || u.title}</div><div className="s ellipsis">{[u.maker, u.cls, u.date && `versão de ${u.date}`].filter(Boolean).join(' · ')}</div></div>
                <span className="small muted">{u.size > 0 ? bytes(u.size) : ''}</span>
              </label>
            ))}
          </div>
        </Card>
      )}
      {st.items && st.items.length === 0 && <Card><div className="row ok-t"><Icon name="check" />Nenhuma atualização de driver pendente no catálogo da Microsoft.</div></Card>}
    </>
  );
}

function InstalledTab() {
  const [list, setList] = useState<Inst[] | null>(null);
  const [q, setQ] = useState('');
  useEffect(() => { call<Inst[]>('drv.installed').then(setList).catch(() => setList([])); }, []);
  const rows = useMemo(() => (list ?? []).filter(d => !q || (d.name + d.provider).toLowerCase().includes(q.toLowerCase())), [list, q]);
  return (
    <Card className="pad-0">
      <div className="row" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}>
        <b>Drivers de terceiros instalados</b><span className="small dim">{list ? `${list.length}` : ''} · mais antigos primeiro</span><span className="spacer" />
        <input type="search" placeholder="Filtrar" value={q} onChange={e => setQ(e.target.value)} style={{ width: 200 }} />
      </div>
      {!list ? <div className="empty"><Spinner /></div> : (
        <div style={{ maxHeight: 'calc(100vh - 300px)', overflow: 'auto' }}>
          <table className="table">
            <thead><tr><th>Dispositivo</th><th>Fabricante</th><th>Versão</th><th className="num">Data</th></tr></thead>
            <tbody>
              {rows.map((d, i) => (
                <tr key={i}><td className="ellipsis" style={{ maxWidth: 320 }}>{d.name}</td><td className="dim">{d.provider}</td><td className="mono small">{d.version}</td>
                  <td className={`num ${d.years > 5 ? 'warn-t' : ''}`}>{d.date}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

// ------------------------------------------------------------------ fontes extras, vídeo, limpeza e rede (DriverPlus)
type XItem = { id: string; title: string; version: string; date: string; size: number; cls: string; missing: boolean; source: string; devices: { name: string; current: string }[] };
type XSt = { state: string; progress: string; error: string | null; message: string | null; items: XItem[] | null; when: string | null; sessions: { id: string; date: string; titles: string[] }[]; admin: boolean };

function useX() {
  const [st, setSt] = useState<XSt | null>(null);
  useEffect(() => {
    call<XSt>('drvx.status').then(setSt).catch(() => {});
    return on('driverPlus', (s: XSt) => setSt(prev => {
      if (prev && prev.state !== 'idle' && s.state === 'idle') { if (s.error) toast(s.error, 'err'); else if (s.message) toast(s.message, 'ok'); }
      return s;
    }));
  }, []);
  return [st, setSt] as const;
}

function XProgress({ st }: { st: XSt }) {
  if (st.state === 'idle') return st.message ? <div className="small ok-t" style={{ marginTop: 10 }}>{st.message}</div> : st.error ? <div className="small bad-t" style={{ marginTop: 10 }}>{st.error}</div> : null;
  return <div className="row small muted" style={{ marginTop: 10 }}><Spinner size={14} />{st.progress || 'Trabalhando…'}</div>;
}

function ExtraCard() {
  const [st, setSt] = useX();
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [rt, setRt] = useState<any>(null);
  useEffect(() => { call('drvx.realtek').then(setRt).catch(() => {}); }, []);
  if (!st) return <Card><Spinner /></Card>;
  const busy = st.state !== 'idle';
  async function scan() { const r = await run(call<XSt>('drvx.scan')); if (r) { setSt(r); setSel(new Set()); } }
  async function install() { const r = await run(call<XSt>('drvx.install', { ids: [...sel] })); if (r) { setSt(r); setSel(new Set()); } }
  async function undo(id: string) { const r = await run(call<XSt>('drvx.undo', { id })); if (r) setSt(r); }
  const toggle = (id: string) => setSel(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  return (
    <>
      <Card>
        <CardTitle icon="search" color="#22d3ee" right={<button className="btn primary" onClick={scan} disabled={busy}><Icon name="search" size={14} />{st.items ? 'Buscar de novo' : 'Buscar em mais fontes'}</button>}>Além do Windows Update</CardTitle>
        <div className="small muted">Busca cada dispositivo (rede, vídeo, áudio, Bluetooth, armazenamento, câmera) e os que estão sem driver no <b>Catálogo completo do Microsoft Update</b>: drivers assinados de todas as marcas, inclusive versões novas que o Windows Update não oferece sozinho. Antes de instalar, o GROBS cria um ponto de restauração e copia os drivers atuais; cada instalação pode ser desfeita.</div>
        <XProgress st={st} />
        {st.items && st.items.length > 0 && (
          <div style={{ marginTop: 12 }}>
            <div className="row" style={{ marginBottom: 6 }}>
              <Check on={sel.size === st.items.length} onChange={v => setSel(v ? new Set(st.items!.map(i => i.id)) : new Set())} /><b>Selecionar todos</b><span className="spacer" />
              {!st.admin && <AdminLock />}
              <button className="btn sm primary" disabled={!st.admin || sel.size === 0 || busy} onClick={install}><Icon name="download" size={13} />Instalar {sel.size || ''}</button>
            </div>
            <div className="list">
              {st.items.map(i => (
                <div key={i.id} className="li" style={{ cursor: 'pointer' }} onClick={() => toggle(i.id)}>
                  <Check on={sel.has(i.id)} onChange={() => {}} />
                  <div className="grow">
                    <div className="t ellipsis">{i.title}</div>
                    <div className="s ellipsis">{i.devices.map(d => `${d.name} (atual: ${d.current})`).join(' · ')}</div>
                    <div className="s">{[i.cls, i.date && `de ${i.date}`, i.size > 0 && bytes(i.size)].filter(Boolean).join(' · ')}</div>
                  </div>
                  {i.missing ? <span className="risk high">sem driver</span> : <span className="risk low">mais novo</span>}
                </div>
              ))}
            </div>
          </div>
        )}
      </Card>
      {rt?.device && (
        <Card>
          <CardTitle icon="activity" color="#f472b6">Áudio Realtek genérico (GitHub)</CardTitle>
          <div className="small muted">Pacote da comunidade <span className="mono">pal1000/Realtek-UAD-generic</span> (1,4 mil estrelas), montado com peças assinadas da Realtek. Seu áudio: <b>{rt.device}</b> {rt.current}{rt.latest && <> · no GitHub: <b>{rt.latest}</b></>}.</div>
          <div className="small warn-t" style={{ marginTop: 6 }}>A troca é manual: remover o driver Realtek atual e rodar o instalador do pacote. O GROBS baixa, extrai e abre o passo a passo; feito errado, o PC pode ficar sem som até reinstalar o driver.</div>
          {rt.error && <div className="small bad-t">{rt.error}</div>}
          <div className="row" style={{ marginTop: 10 }}><span className="risk medium">moderado</span><span className="spacer" /><button className="btn sm" disabled={busy || !rt.url} onClick={async () => { const r = await run(call<XSt>('drvx.realtekDownload')); if (r) setSt(r); }}><Icon name="download" size={13} />Baixar e abrir o passo a passo</button></div>
        </Card>
      )}
      {st.sessions.length > 0 && (
        <Card>
          <CardTitle icon="restore" color="#fbbf24">Desfazer instalações de driver</CardTitle>
          <div className="list">
            {st.sessions.map(s => (
              <div key={s.id} className="li">
                <div className="grow"><div className="t ellipsis">{s.titles.join(' · ')}</div><div className="s">{s.date}</div></div>
                <button className="btn sm" disabled={busy || !st.admin} onClick={() => undo(s.id)}><Icon name="restore" size={13} />Desfazer</button>
              </div>
            ))}
          </div>
        </Card>
      )}
    </>
  );
}

function VideoTab() {
  const [st, setSt] = useX();
  const [lock, setLock] = useState<any>(null);
  const [ask, setAsk] = useState(false);
  useEffect(() => { call('drvx.gpuLock').then(setLock).catch(() => {}); }, []);
  if (!st || !lock) return <Card><Spinner /></Card>;
  const busy = st.state !== 'idle';
  return (
    <div className="grid" style={{ gap: 16 }}>
      <GpuCard />
      <Card>
        <CardTitle icon="refresh" color="#a78bfa">Instalação limpa do driver de vídeo</CardTitle>
        <div className="small muted">Como o DDU: baixa o driver novo primeiro, cria ponto de restauração e backup, remove <b>todos</b> os drivers de vídeo antigos e os caches de shader, e instala o novo do zero. NVIDIA: pacote oficial em modo silencioso e limpo. Intel e AMD: driver mais novo do Catálogo do Microsoft Update.</div>
        <div className="small dim" style={{ marginTop: 6 }}>Placa: {lock.gpus.map((g: any) => `${g.name} ${g.version}`).join(' · ') || 'nenhuma'}</div>
        <XProgress st={st} />
        <div className="row" style={{ marginTop: 10 }}>
          <span className="risk medium">moderado</span>
          {!st.admin && <AdminLock />}
          <span className="spacer" />
          <button className="btn primary" disabled={busy || !st.admin || lock.gpus.length === 0} onClick={() => setAsk(true)}><Icon name="refresh" size={14} />Fazer instalação limpa</button>
        </div>
      </Card>
      <Card>
        <CardTitle icon="lock" color="#fbbf24" right={<Toggle on={lock.locked} disabled={!lock.admin || busy} onChange={async v => { const r = await run(call('drvx.gpuLock', { on: v }), v ? 'Driver de vídeo travado' : 'Driver de vídeo destravado'); if (r) setLock(r); }} />}>Travar o driver de vídeo</CardTitle>
        <div className="small muted">Impede o Windows Update (e qualquer instalador) de trocar o driver da placa de vídeo. A instalação limpa do GROBS destrava e trava de novo sozinha. Para atualizar por outro programa, destrave antes.</div>
        <div style={{ marginTop: 8 }}><span className="risk medium">moderado</span></div>
      </Card>
      <Modal open={ask} onClose={() => setAsk(false)}>
        <h3>Instalação limpa do vídeo</h3>
        <p className="muted" style={{ marginTop: 0 }}>A tela vai piscar e pode ficar em baixa resolução por alguns minutos. Salve o que estiver aberto. No fim, reinicie o PC. Se algo der errado, use "Desfazer" na aba Atualizar ou o ponto de restauração.</p>
        <div className="row"><span className="spacer" /><button className="btn ghost" onClick={() => setAsk(false)}>Cancelar</button><button className="btn primary" onClick={async () => { setAsk(false); const r = await run(call<XSt>('drvx.gpuClean')); if (r) setSt(r); }}>Começar</button></div>
      </Modal>
    </div>
  );
}

type OldPkg = { inf: string; name: string; provider: string; cls: string; version: string; date: string; size: number };
function OldTab() {
  const admin = useStore(appStore, a => !!a?.admin);
  const [list, setList] = useState<OldPkg[] | null>(null);
  const [sel, setSel] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const load = () => { setList(null); call<OldPkg[]>('drvx.old').then(l => { setList(l); setSel(new Set(l.map(x => x.inf))); }).catch((e: Error) => { toast(e.message, 'err'); setList([]); }); };
  useEffect(() => { if (admin) load(); }, [admin]);
  if (!admin) return <Card><AdminLock text="Abra o GROBS como administrador para ver os drivers guardados pelo Windows" /></Card>;
  const total = (list ?? []).filter(p => sel.has(p.inf)).reduce((a, p) => a + p.size, 0);
  async function remove() {
    setBusy(true);
    const r = await run(call<{ done: number; freed: number }>('drvx.deleteOld', { infs: [...sel] }));
    setBusy(false);
    if (r) { toast(`${r.done} pacote(s) removido(s), ${bytes(r.freed)} liberados`); load(); }
  }
  const toggle = (inf: string) => setSel(s => { const n = new Set(s); if (n.has(inf)) n.delete(inf); else n.add(inf); return n; });
  return (
    <Card className="pad-0">
      <div style={{ padding: '16px 18px 8px' }}>
        <CardTitle icon="trash" color="#f472b6" right={list && list.length > 0 && <button className="btn sm danger" disabled={busy || sel.size === 0} onClick={remove}>{busy ? <Spinner size={12} /> : <Icon name="trash" size={13} />}Remover {bytes(total)}</button>}>Drivers antigos guardados pelo Windows</CardTitle>
        <div className="small muted">Versões velhas e duplicadas que ficam no repositório de drivers. O GROBS nunca mostra o driver em uso nem a versão mais nova de cada um, e o Windows recusa remover o que estiver em uso. <span className="risk low">tranquilo</span></div>
      </div>
      {!list ? <div className="empty"><Spinner /> <div className="small dim" style={{ marginTop: 8 }}>Lendo o repositório de drivers (até 1 minuto)…</div></div>
        : list.length === 0 ? <div className="empty">Nenhum driver antigo sobrando.</div>
          : list.map(p => (
            <div key={p.inf} className="li" style={{ cursor: 'pointer' }} onClick={() => toggle(p.inf)}>
              <Check on={sel.has(p.inf)} onChange={() => {}} />
              <div className="grow"><div className="t ellipsis">{p.provider} · {p.name}</div><div className="s">{p.cls} · versão {p.version}{p.date && ` de ${p.date}`} · {p.inf}</div></div>
              <span className="small muted">{bytes(p.size)}</span>
            </div>
          ))}
    </Card>
  );
}

function NetTab() {
  const [st, setSt] = useX();
  if (!st) return <Card><Spinner /></Card>;
  const busy = st.state !== 'idle';
  async function pack() { const path = await call<string>('shell.pickFolder'); if (!path) return; const r = await run(call<XSt>('drvx.netPack', { path })); if (r) setSt(r); }
  async function install() { const path = await call<string>('shell.pickFolder'); if (!path) return; const r = await run(call<XSt>('drvx.netInstall', { path })); if (r) setSt(r); }
  return (
    <div className="grid g2">
      <Card>
        <CardTitle icon="network" color="#34d399">Preparar antes de formatar</CardTitle>
        <div className="small muted">Baixa do Catálogo do Microsoft Update os drivers de rede cabeada e Wi-Fi mais comuns (Realtek, Intel, Qualcomm Atheros, MediaTek, Killer) para uma pasta, de preferência um pendrive. Deixe a pasta <span className="mono">GROBS-Drivers-Rede</span> junto com o instalador do GROBS: depois de formatar, o instalador oferece "Instalar os drivers de rede do pendrive", sem precisar de internet.</div>
        <XProgress st={st} />
        <div className="row" style={{ marginTop: 10 }}><span className="risk low">tranquilo</span><span className="spacer" /><button className="btn primary" disabled={busy} onClick={pack}><Icon name="download" size={14} />Escolher pendrive e baixar</button></div>
      </Card>
      <Card>
        <CardTitle icon="upload" color="#60a5fa">Instalar drivers de rede salvos</CardTitle>
        <div className="small muted">Se você já formatou e está sem internet, escolha a pasta ou o pendrive com <span className="mono">GROBS-Drivers-Rede</span>. O Windows instala só os que servem para a sua placa de rede.</div>
        <div className="row" style={{ marginTop: 10 }}>{!st.admin && <AdminLock />}<span className="spacer" /><button className="btn" disabled={busy || !st.admin} onClick={install}><Icon name="folder" size={14} />Escolher pasta e instalar</button></div>
      </Card>
    </div>
  );
}

export default function Drivers() {
  const [tab, setTab] = useState<'update' | 'video' | 'old' | 'net' | 'installed'>('update');
  return (
    <div className="page">
      <PageHead title="Drivers" sub="Drivers oficiais do Windows Update, do Catálogo completo da Microsoft e direto da NVIDIA, Intel e AMD, com backup e desfazer."
        right={<Tabs layoutId="drvtab" value={tab} onChange={setTab} items={[{ id: 'update', label: 'Atualizar' }, { id: 'video', label: 'Vídeo' }, { id: 'old', label: 'Limpar antigos' }, { id: 'net', label: 'Rede p/ formatar' }, { id: 'installed', label: 'Instalados' }]} />} />
      {tab === 'update' ? <div className="grid" style={{ gap: 16 }}><MissingCard /><CatalogTab /><ExtraCard /></div>
        : tab === 'video' ? <VideoTab /> : tab === 'old' ? <OldTab /> : tab === 'net' ? <NetTab /> : <InstalledTab />}
    </div>
  );
}
