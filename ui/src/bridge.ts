// Ponte JS <-> host nativo (WebView2 postMessage). Fora do app usa dados de demonstração.

type Pending = { resolve: (v: any) => void; reject: (e: Error) => void };
type Listener = (data: any) => void;

const wv: any = (window as any).chrome?.webview;
export const isNative = !!wv;

let seq = 0;
const pending = new Map<number, Pending>();
const listeners = new Map<string, Set<Listener>>();

if (wv) {
  wv.addEventListener('message', (e: MessageEvent) => {
    const msg: any = e.data;
    if (msg && msg.ev) {
      listeners.get(msg.ev)?.forEach(fn => fn(msg.d));
      return;
    }
    const p = pending.get(msg?.id);
    if (!p) return;
    pending.delete(msg.id);
    if (msg.ok) p.resolve(msg.r);
    else p.reject(new Error(msg.e || 'Erro'));
  });
}

export function call<T = any>(m: string, p?: Record<string, unknown>): Promise<T> {
  if (!wv) return mock(m, p) as Promise<T>;
  const id = ++seq;
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve, reject });
    wv.postMessage({ id, m, p: p ?? {} });
  });
}

/** Comando de janela sem esperar resposta (arrastar precisa ser imediato). */
export function fire(m: string, p?: Record<string, unknown>) {
  wv?.postMessage({ m, p: p ?? {} });
}

export function on(ev: string, fn: Listener): () => void {
  if (!listeners.has(ev)) listeners.set(ev, new Set());
  listeners.get(ev)!.add(fn);
  return () => listeners.get(ev)?.delete(fn);
}

// ---------------------------------------------------------------- demonstração (navegador)

const GB = 1024 ** 3;
let t = 0;
function rnd(a: number, b: number) { return a + Math.random() * (b - a); }

if (!wv) {
  setInterval(() => {
    t++;
    const d = {
      cpu: Math.round(18 + 14 * Math.sin(t / 6) + rnd(0, 10)), cpuMhz: Math.round(rnd(3200, 3600)),
      cores: [0, 1, 2, 3].map(i => Math.round(rnd(5, 60) + i * 3)),
      mem: 61, memTotal: 8 * GB, memUsed: 4.9 * GB, commit: 6.1 * GB, commitLimit: 12 * GB, cached: 2.2 * GB,
      procs: 182, threads: 2410, handles: 81234,
      disk: Math.round(rnd(0, 18)), diskRead: rnd(0, 4e6), diskWrite: rnd(0, 2e6),
      netDown: rnd(1e3, 900e3), netUp: rnd(1e3, 120e3),
      gpu: Math.round(rnd(2, 30)), gpuMem: 0.4 * GB, gpuShared: 0.2 * GB,
      cpuTemp: Math.round(rnd(44, 52)), cpuTempSrc: 'ACPI', gpuTemp: -1, gpuTempSrc: '', uptime: 3600 * 5 + t,
    };
    listeners.get('stats')?.forEach(fn => fn(d));
  }, 1000);
}

const wait = (ms: number) => new Promise(r => setTimeout(r, ms));

async function mock(m: string, p: any): Promise<any> {
  await wait(m.startsWith('clean.') || m.startsWith('sec.audit') ? 900 : 120);
  switch (m) {
    case 'app.info': return { version: '1.0.0', admin: false, proc64: true, os64: true, dev: true, settings: { animations: 'full', interval: 1000, trayOnClose: false, startWithWindows: false, tempUnit: 'C', boostApps: [] } };
    case 'app.setting': return {};
    case 'sys.info': return { os: 'Windows 10 Pro', osVersion: '22H2', osBuild: '19045.6456', cpu: 'Intel(R) Core(TM) i5-3470 CPU @ 3.20GHz', cpuCores: 4, cpuThreads: 4, gpus: [{ name: 'Intel(R) HD Graphics', driver: '10.18.10.5161' }], ramTotal: 8 * GB, ramSticks: 2, ramSpeed: 1600, board: 'ASUSTeK P8H61-M', machine: 'DESKTOP', admin: false, arch64: true };
    case 'sys.disks': return { volumes: [{ name: 'C:', label: 'Sistema', fs: 'NTFS', total: 240 * GB, free: 61 * GB }, { name: 'D:', label: 'Dados', fs: 'NTFS', total: 1000 * GB, free: 420 * GB }], physical: [{ name: 'Kingston SA400', type: 'SSD', bus: 'SATA', health: 'ok', size: 240 * GB }, { name: 'WDC WD10EZEX', type: 'HDD', bus: 'SATA', health: 'ok', size: 1000 * GB }], smartAvailable: false, smartFailure: null };
    case 'sys.battery': return { present: false };
    case 'clean.scan': return [
      { id: 'usertemp', name: 'Arquivos temporários do usuário', group: 'Windows', desc: '%TEMP%', size: 812e6, count: 3121, default: true },
      { id: 'thumbs', name: 'Cache de miniaturas', group: 'Windows', desc: 'Explorer', size: 92e6, count: 8, default: true },
      { id: 'wupdate', name: 'Cache do Windows Update', group: 'Windows', desc: '', size: 0, count: 0, admin: true, locked: true, default: true },
      { id: 'recycle', name: 'Lixeira', group: 'Windows', desc: '', size: 1.2e9, count: 41, default: false },
      { id: 'chrome', name: 'Google Chrome', group: 'Navegadores', desc: 'Somente cache', size: 640e6, count: 5200, running: true, default: true },
      { id: 'edge', name: 'Microsoft Edge', group: 'Navegadores', desc: 'Somente cache', size: 210e6, count: 1900, default: true },
      { id: 'discord', name: 'Discord', group: 'Aplicativos', desc: 'Cache', size: 330e6, count: 900, default: true },
    ];
    case 'clean.run': return { freed: 1.9e9, files: 11021, skipped: 37 };
    case 'startup.list': return [
      { id: 'hkcu|Discord', name: 'Discord', desc: 'Discord', company: 'Discord Inc.', signer: 'Discord Inc.', enabled: true, location: 'Registro (usuário)', path: 'C:\\Users\\x\\AppData\\Local\\Discord\\Update.exe', exists: true },
      { id: 'hkcu|Steam', name: 'Steam', desc: 'Steam Client Bootstrapper', company: 'Valve', signer: 'Valve Corp.', enabled: true, location: 'Registro (usuário)', path: 'C:\\Program Files (x86)\\Steam\\steam.exe', exists: true },
      { id: 'hklm|SecurityHealth', name: 'SecurityHealth', desc: 'Windows Security notification icon', company: 'Microsoft Corporation', signer: null, enabled: true, location: 'Registro (todos os usuários)', admin: true, path: 'C:\\Windows\\system32\\SecurityHealthSystray.exe', exists: true },
      { id: 'hkcu|Spotify', name: 'Spotify', desc: 'Spotify', company: 'Spotify Ltd', signer: 'Spotify AB', enabled: false, location: 'Registro (usuário)', path: 'C:\\Users\\x\\AppData\\Roaming\\Spotify\\Spotify.exe', exists: true },
    ];
    case 'startup.set': return true;
    case 'tweaks.list': return [
      { id: 'gamemode', group: 'Desempenho', title: 'Modo de Jogo do Windows', desc: 'Prioriza o jogo em primeiro plano.', risk: 'low', on: true },
      { id: 'gamedvr', group: 'Desempenho', title: 'Desativar gravação em segundo plano (Game DVR)', desc: 'Libera GPU e disco em jogos.', risk: 'low', on: false },
      { id: 'hags', group: 'Desempenho', title: 'Agendamento de GPU acelerado por hardware', desc: 'Requer reiniciar.', risk: 'medium', admin: true, locked: true, restart: true, on: false },
      { id: 'adid', group: 'Privacidade', title: 'Desativar ID de publicidade', desc: 'Sem anúncios personalizados.', risk: 'low', on: false },
      { id: 'telemetry', group: 'Privacidade', title: 'Telemetria no mínimo', desc: 'Para o DiagTrack.', risk: 'medium', admin: true, locked: true, on: false },
    ];
    case 'tweaks.set': return true;
    case 'power.list': return [
      { id: '381b4222-f694-41f0-9685-ff5bb260df2e', name: 'Equilibrado', kind: 'balanced', active: true },
      { id: '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c', name: 'Alto desempenho', kind: 'high', active: false },
    ];
    case 'power.hibernation': return true;
    case 'boost.status': return { active: false };
    case 'boost.start': return { active: true, plan: 'Alto desempenho', closed: 0 };
    case 'boost.stop': return { active: false };
    case 'proc.list': return Array.from({ length: 40 }, (_, i) => ({ pid: 1000 + i, name: ['chrome.exe', 'explorer.exe', 'Discord.exe', 'svchost.exe', 'MsMpEng.exe'][i % 5], cpu: +rnd(0, 8).toFixed(1), mem: rnd(10e6, 600e6), threads: 20, prio: 8, path: '' }));
    case 'sec.status': return { defender: true, service: true, antivirus: true, realtime: true, behavior: true, tamper: true, sigVersion: '1.441.2.0', sigAge: 0, engine: '1.1.25090.3001', quickAge: 2, lastQuick: '24/10/2026 10:12', products: [{ name: 'Windows Defender', enabled: true, updated: true }], firewall: { domain: true, private: true, public: true }, uac: true, smartscreen: true, scan: { running: false } };
    case 'sec.audit': return [{ level: 'ok', title: 'Arquivo hosts limpo', detail: 'Nenhum redirecionamento' }, { level: 'ok', title: 'Sem proxy', detail: 'Conexão direta' }, { level: 'info', title: 'Inicialização sem assinatura digital', detail: 'SecurityHealth' }];
    case 'sec.threats': return { ok: true, items: [] };
    case 'sec.scan': return { running: true, type: p?.type, elapsed: 0 };
    case 'sec.scanStatus': return { running: false };
    case 'apps.list': return [
      { id: 'lm|1', name: '7-Zip 24.08 (x64)', publisher: 'Igor Pavlov', version: '24.08', size: 5.6e6, date: '12/03/2025', scope: 'Sistema' },
      { id: 'lm|2', name: 'Google Chrome', publisher: 'Google LLC', version: '141.0', size: 520e6, date: '01/10/2026', scope: 'Sistema' },
      { id: 'cu|3', name: 'Discord', publisher: 'Discord Inc.', version: '1.0.9200', size: 380e6, date: '05/09/2026', scope: 'Usuário' },
      { id: 'lm|4', name: 'Steam', publisher: 'Valve Corporation', version: '2.10.91.91', size: 0, date: '', scope: 'Sistema (32 bits)' },
    ];
    case 'disk.children': return { root: 'C:\\', path: p?.sub ?? '', size: 180 * GB, files: 412000, looseFiles: 9 * GB, children: [
      { name: 'Windows', size: 38 * GB, files: 180000, dirs: 90 }, { name: 'Users', size: 72 * GB, files: 150000, dirs: 6 }, { name: 'Program Files', size: 31 * GB, files: 50000, dirs: 80 },
      { name: 'Program Files (x86)', size: 22 * GB, files: 30000, dirs: 60 }, { name: 'ProgramData', size: 8 * GB, files: 2000, dirs: 40 } ], bigFiles: [{ path: 'C:\\pagefile.sys', size: 6 * GB }, { path: 'C:\\hiberfil.sys', size: 3.2 * GB }] };
    case 'disk.start': setTimeout(() => listeners.get('diskDone')?.forEach(fn => fn({})), 1500); return true;
    case 'disk.status': return { running: true, files: Math.round(rnd(1000, 90000)) };
    case 'tools.ping': return { host: p?.host, avg: Math.round(rnd(9, 30)), min: 8, max: 34, lost: 0 };
    case 'shell.pickFolder': return 'C:\\Users';
    default: return true;
  }
}
