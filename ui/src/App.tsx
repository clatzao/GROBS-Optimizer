import { motion, MotionConfig } from 'motion/react';
import { lazy, Suspense, useEffect, useState } from 'react';
import { call, fire, isNative, on } from './bridge';
import { Icon } from './components/Icon';
import ShinyText from './components/rb/ShinyText';
import ClickSpark from './components/rb/ClickSpark';
import { Spinner, Toasts } from './components/ui';
import { appStore, boostStore, statsStore, toast, useStore } from './store';
import { pct } from './format';
import Dashboard from './pages/Dashboard';
import logo from './assets/logo.png';
import ErrorBoundary from './components/ErrorBoundary';

// Páginas carregadas sob demanda: a inicial abre mais rápido.
const MonitorPage = lazy(() => import('./pages/Monitor'));
const Cleaner = lazy(() => import('./pages/Cleaner'));
const Optimize = lazy(() => import('./pages/Optimize'));
const Security = lazy(() => import('./pages/Security'));
const Apps = lazy(() => import('./pages/Apps'));
const Disk = lazy(() => import('./pages/Disk'));
const Tools = lazy(() => import('./pages/Tools'));
const SettingsPage = lazy(() => import('./pages/Settings'));

const Diagnose = lazy(() => import('./pages/Diagnose'));
const Services = lazy(() => import('./pages/Services'));
const Visual = lazy(() => import('./pages/Visual'));
const Update = lazy(() => import('./pages/Update'));
const GameOverlay = lazy(() => import('./pages/GameOverlay'));
const Drivers = lazy(() => import('./pages/Drivers'));
const Overclock = lazy(() => import('./pages/Overclock'));
const Beautify = lazy(() => import('./pages/Beautify'));
const Debloat = lazy(() => import('./pages/Debloat'));
const Gamer = lazy(() => import('./pages/Gamer'));
const Perf = lazy(() => import('./pages/Perf'));
const Disks = lazy(() => import('./pages/Disks'));
const Repair = lazy(() => import('./pages/Repair'));
const Bench = lazy(() => import('./pages/Bench'));
const Profiles = lazy(() => import('./pages/Profiles'));

export type PageId = 'dash' | 'monitor' | 'diagnose' | 'clean' | 'optimize' | 'services' | 'debloat' | 'visual' | 'update' | 'security'
  | 'gamer' | 'overlay' | 'oc' | 'drivers' | 'apps' | 'disk' | 'beautify' | 'tools' | 'settings' | 'perf' | 'disks' | 'repair' | 'bench' | 'profiles';

// mod = módulo escolhido no instalador (sem lista de módulos = tudo instalado)
const NAV: { id: PageId; label: string; icon: string; section?: string; mod?: string }[] = [
  { id: 'dash', label: 'Painel', icon: 'dashboard' },
  { id: 'monitor', label: 'Monitor', icon: 'activity', mod: 'monitor' },
  { id: 'diagnose', label: 'Diagnóstico', icon: 'heart', mod: 'repair' },
  { id: 'repair', label: 'Reparar o Windows', icon: 'wrench', mod: 'repair' },
  { id: 'clean', label: 'Limpeza', icon: 'broom', section: 'Manutenção', mod: 'clean' },
  { id: 'optimize', label: 'Otimização', icon: 'rocket', mod: 'optimize' },
  { id: 'profiles', label: 'Perfis 1 clique', icon: 'layers', mod: 'optimize' },
  { id: 'perf', label: 'Memória e latência', icon: 'memory', mod: 'optimize' },
  { id: 'services', label: 'Serviços', icon: 'list', mod: 'optimize' },
  { id: 'debloat', label: 'Recursos do Windows', icon: 'package', mod: 'optimize' },
  { id: 'visual', label: 'Efeitos visuais', icon: 'eye', mod: 'optimize' },
  { id: 'update', label: 'Windows Update', icon: 'refresh', mod: 'update' },
  { id: 'security', label: 'Segurança', icon: 'shield', mod: 'security' },
  { id: 'gamer', label: 'Gamer', icon: 'rocket', section: 'Jogos', mod: 'gamer' },
  { id: 'bench', label: 'Benchmark', icon: 'gauge', mod: 'gamer' },
  { id: 'overlay', label: 'Overlay de FPS', icon: 'gamepad', mod: 'overlay' },
  { id: 'oc', label: 'Overclock (GPU)', icon: 'zap', mod: 'oc' },
  { id: 'drivers', label: 'Drivers', icon: 'cpu', section: 'Ferramentas', mod: 'drivers' },
  { id: 'apps', label: 'Programas', icon: 'package', mod: 'tools' },
  { id: 'disks', label: 'Discos', icon: 'disk', mod: 'tools' },
  { id: 'disk', label: 'Espaço em disco', icon: 'pie', mod: 'tools' },
  { id: 'beautify', label: 'Embelezar', icon: 'sparkles', mod: 'beautify' },
  { id: 'tools', label: 'Utilitários', icon: 'wrench', mod: 'tools' },
  { id: 'settings', label: 'Configurações', icon: 'settings', section: '' },
];

function TitleBar({ maximized, update, go }: { maximized: boolean; update: string | null; go: (p: PageId) => void }) {
  const app = useStore(appStore);
  const boost = useStore(boostStore);
  return (
    <header className="titlebar">
      <div className="drag"
        onMouseDown={e => { if (e.button === 0 && e.detail === 1) fire('win.drag'); }}
        onDoubleClick={() => call('win.maximize')}>
        <div className="brand">
          <img className="logo-img" src={logo} width={24} height={24} alt="" />
          <ShinyText text="GROBS Optimizer" color="#e9e7f5" shineColor="#ffffff" speed={1.6} glow={0.25} trigger="hover" />
        </div>
        {app && <span className="chip">v{app.version} · {app.proc64 ? 'x64' : 'x86'}</span>}
        {app && (app.admin ? <span className="chip ok"><Icon name="shieldCheck" size={11} />Administrador</span> : <span className="chip">Usuário padrão</span>)}
        {boost && <span className="chip accent pulse"><Icon name="rocket" size={11} />Turbo ativo</span>}
        {update && <button className="chip ok" style={{ cursor: 'pointer' }} onMouseDown={e => e.stopPropagation()} onClick={() => go('settings')}><Icon name="download" size={11} />Atualização {update} disponível</button>}
      </div>
      <div className="winbtns">
        <button title="Minimizar" onClick={() => call('win.minimize')}><Icon name="minus" size={15} /></button>
        <button title={maximized ? 'Restaurar' : 'Maximizar'} onClick={() => call('win.maximize')}><Icon name={maximized ? 'copy' : 'square'} size={12} /></button>
        <button className="close" title="Fechar" onClick={() => call('win.close')}><Icon name="x" size={15} /></button>
      </div>
    </header>
  );
}

function SideStats() {
  const cpu = useStore(statsStore, s => (s ? Math.round(s.cpu) : -1));
  const mem = useStore(statsStore, s => (s ? Math.round(s.mem) : -1));
  return (
    <div className="sidecard">
      <div className="row" style={{ justifyContent: 'space-between', marginBottom: 6 }}><span>CPU</span><b style={{ color: 'var(--text)' }}>{pct(cpu)}</b></div>
      <div className="bar" style={{ marginBottom: 10 }}><i style={{ ["--v" as any]: Math.max(0, cpu) / 100 }} /></div>
      <div className="row" style={{ justifyContent: 'space-between', marginBottom: 6 }}><span>Memória</span><b style={{ color: 'var(--text)' }}>{pct(mem)}</b></div>
      <div className="bar"><i style={{ ["--v" as any]: Math.max(0, mem) / 100 }} /></div>
    </div>
  );
}

/** Itens visíveis conforme os módulos escolhidos na instalação; cada item herda a seção anterior. */
function visibleNav(modules?: string[]) {
  let sec = '__top';
  const out: { item: (typeof NAV)[number]; sec: string }[] = [];
  for (const n of NAV) {
    if (n.section !== undefined) sec = n.section;
    if (!n.mod || !modules || modules.length === 0 || modules.includes(n.mod)) out.push({ item: n, sec });
  }
  return out;
}

function Sidebar({ page, go }: { page: PageId; go: (p: PageId) => void }) {
  const modules = useStore(appStore, a => (a?.settings as any)?.modules as string[] | undefined);
  const items = visibleNav(modules);
  return (
    <nav className="sidebar">
      {items.map(({ item: n, sec }, i) => (
        <div key={n.id} style={{ display: 'contents' }}>
          {(i === 0 ? sec !== '__top' : items[i - 1].sec !== sec) && (sec ? <div className="navlabel">{sec}</div> : <div className="grow" />)}
          <button className={`navitem ${page === n.id ? 'active' : ''}`} onClick={() => go(n.id)}>
            {page === n.id && <motion.i className="navbg" layoutId="navbg" transition={{ type: 'spring', stiffness: 480, damping: 36 }} />}
            <i className="navbg-hover" />
            <span className="ico"><Icon name={n.icon} size={18} /></span>
            <span>{n.label}</span>
          </button>
        </div>
      ))}
      <SideStats />
    </nav>
  );
}

export default function App() {
  const app = useStore(appStore);
  const [page, setPage] = useState<PageId>('dash');
  const [maximized, setMaximized] = useState(false);
  const [update, setUpdate] = useState<string | null>(null);
  useEffect(() => on('update', (u: { version: string; available: boolean }) => { if (u?.available) { setUpdate(u.version); toast(`GROBS Optimizer ${u.version} disponível — atualize em Configurações`, 'info'); } }), []);
  const anim = app?.settings.animations ?? 'full';

  useEffect(() => on('maximized', (m: boolean) => setMaximized(!!m)), []);
  useEffect(() => { document.documentElement.dataset.anim = anim; }, [anim]);
  useEffect(() => {
    // bloqueia F5/Ctrl+R e o menu de contexto do navegador no app final
    if (!isNative || app?.dev) return;
    const k = (e: KeyboardEvent) => { if (e.key === 'F5' || (e.ctrlKey && ['r', 'p', 'f', 'u'].includes(e.key.toLowerCase()))) e.preventDefault(); };
    window.addEventListener('keydown', k);
    return () => window.removeEventListener('keydown', k);
  }, [app?.dev]);

  const pageEl = (() => {
    switch (page) {
      case 'dash': return <Dashboard go={setPage} />;
      case 'monitor': return <MonitorPage />;
      case 'clean': return <Cleaner />;
      case 'optimize': return <Optimize />;
      case 'security': return <Security />;
      case 'apps': return <Apps />;
      case 'disk': return <Disk />;
      case 'tools': return <Tools />;
      case 'settings': return <SettingsPage />;
      case 'diagnose': return <Diagnose go={setPage} />;
      case 'services': return <Services />;
      case 'debloat': return <Debloat />;
      case 'visual': return <Visual />;
      case 'update': return <Update />;
      case 'overlay': return <GameOverlay />;
      case 'gamer': return <Gamer />;
      case 'oc': return <Overclock />;
      case 'drivers': return <Drivers />;
      case 'beautify': return <Beautify />;
      case 'perf': return <Perf />;
      case 'disks': return <Disks />;
      case 'repair': return <Repair />;
      case 'bench': return <Bench />;
      case 'profiles': return <Profiles />;
    }
  })();

  const content = (
    <div key={anim} className={`app ${maximized ? "max" : ""}`}>
      <div className="aurora"><i /><i /><i /></div>
      <TitleBar maximized={maximized} update={update} go={setPage} />
      <div className="body">
        <Sidebar page={page} go={setPage} />
        <main className="main">
          <>
            <motion.div key={page}
              initial={{ opacity: 0, y: 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}>
              <ErrorBoundary><Suspense fallback={<div className="empty"><Spinner size={26} color="var(--accent)" /></div>}>
                {pageEl}
              </Suspense></ErrorBoundary>
            </motion.div>
          </>
        </main>
      </div>
      <Toasts />
    </div>
  );

  return (
    <MotionConfig reducedMotion={anim === 'full' ? 'never' : 'always'}>
      {anim === 'full'
        ? <ClickSpark sparkColor={['#a78bfa', '#22d3ee', '#f472b6']} sparkSize={9} sparkRadius={18} sparkCount={8} duration={420} style={{ height: '100%' }}>{content}</ClickSpark>
        : content}
    </MotionConfig>
  );
}
