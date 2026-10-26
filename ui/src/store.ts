import { useSyncExternalStore } from 'react';
import { call, on } from './bridge';

export type Stats = {
  cpu: number; cpuMhz: number; cores: number[];
  mem: number; memTotal: number; memUsed: number; commit?: number; commitLimit?: number; cached?: number;
  procs?: number; threads?: number; handles?: number;
  disk: number; diskRead: number; diskWrite: number; netDown: number; netUp: number;
  gpu: number; gpuMem: number; gpuShared: number; gpuFan?: number; gpuPower?: number;
  cpuTemp: number; cpuTempSrc: string; gpuTemp: number; gpuTempSrc: string;
  battery?: number; charging?: boolean; uptime: number;
};

export type AppInfo = { version: string; admin: boolean; proc64: boolean; os64: boolean; dev: boolean; settings: Settings };
export type Settings = { animations: 'full' | 'reduced' | 'off'; interval: number; trayOnClose: boolean; startWithWindows: boolean; tempUnit: 'C' | 'F'; boostApps: string[]; modules?: string[]; modulesUser?: string[]; killHotkey?: boolean; sensorsAuto?: boolean };

/** Store mínimo: cada componente assina só o pedaço que usa. */
function createStore<T>(initial: T) {
  let state = initial;
  const subs = new Set<() => void>();
  return {
    get: () => state,
    set(next: T | ((s: T) => T)) {
      state = typeof next === 'function' ? (next as (s: T) => T)(state) : next;
      subs.forEach(fn => fn());
    },
    subscribe(fn: () => void) { subs.add(fn); return () => { subs.delete(fn); }; },
  };
}

export const HISTORY = 60;
export type History = Record<'cpu' | 'mem' | 'gpu' | 'disk' | 'netDown' | 'netUp' | 'cpuTemp', number[]>;

export const statsStore = createStore<Stats | null>(null);
export const historyStore = createStore<History>({ cpu: [], mem: [], gpu: [], disk: [], netDown: [], netUp: [], cpuTemp: [] });
export const appStore = createStore<AppInfo | null>(null);
export const boostStore = createStore<boolean>(false);

on('stats', (d: Stats) => {
  statsStore.set(d);
  historyStore.set(h => {
    const n: any = {};
    for (const k of Object.keys(h) as (keyof History)[]) {
      const arr = h[k].length >= HISTORY ? h[k].slice(1) : h[k].slice();
      arr.push(Math.max(0, (d as any)[k] ?? 0));
      n[k] = arr;
    }
    return n;
  });
});
on('boost', (d: any) => boostStore.set(!!d?.active));

export function useStore<T, S = T>(store: { get: () => T; subscribe: (fn: () => void) => () => void }, select?: (s: T) => S): S {
  return useSyncExternalStore(store.subscribe, () => (select ? select(store.get()) : (store.get() as unknown as S)));
}

export async function loadApp() {
  const info = await call<AppInfo>('app.info');
  appStore.set(info);
  call('boost.status').then((b: any) => boostStore.set(!!b?.active)).catch(() => {});
  return info;
}

export async function setSetting<K extends keyof Settings>(key: K, value: Settings[K]) {
  appStore.set(s => (s ? { ...s, settings: { ...s.settings, [key]: value, ...(key === 'modulesUser' ? { modules: value as string[] } : {}) } } : s));
  await call('app.setting', { key, value });
}

// ---------------------------------------------------------------- toasts

export type Toast = { id: number; kind: 'ok' | 'warn' | 'err' | 'info'; text: string };
export const toastStore = createStore<Toast[]>([]);
let toastSeq = 0;
export function toast(text: string, kind: Toast['kind'] = 'ok') {
  const id = ++toastSeq;
  toastStore.set(list => [...list.slice(-3), { id, kind, text }]);
  setTimeout(() => toastStore.set(list => list.filter(t => t.id !== id)), 4200);
}

export async function run<T>(p: Promise<T>, okText?: string): Promise<T | undefined> {
  try {
    const r = await p;
    if (okText) toast(okText, 'ok');
    return r;
  } catch (e: any) {
    toast(e?.message ?? 'Falha', 'err');
    return undefined;
  }
}
