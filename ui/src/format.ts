export function bytes(n: number, digits = 1): string {
  if (!isFinite(n) || n <= 0) return '0 B';
  const u = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.min(u.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
  const v = n / 1024 ** i;
  return `${v.toFixed(i === 0 ? 0 : v >= 100 ? 0 : digits).replace('.', ',')} ${u[i]}`;
}

export function rate(n: number): string {
  return `${bytes(n)}/s`;
}

export function bits(n: number): string {
  const b = n * 8;
  if (b < 1000) return `${b.toFixed(0)} bps`;
  if (b < 1e6) return `${(b / 1e3).toFixed(0)} Kbps`;
  if (b < 1e9) return `${(b / 1e6).toFixed(1).replace('.', ',')} Mbps`;
  return `${(b / 1e9).toFixed(2).replace('.', ',')} Gbps`;
}

export function pct(n: number): string {
  return n < 0 ? '—' : `${Math.round(n)}%`;
}

export function temp(c: number, unit: 'C' | 'F' = 'C'): string {
  if (c < 0) return '—';
  return unit === 'F' ? `${Math.round(c * 9 / 5 + 32)}°F` : `${Math.round(c)}°C`;
}

export function uptime(s: number): string {
  const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60);
  return d > 0 ? `${d}d ${h}h ${m}min` : h > 0 ? `${h}h ${m}min` : `${m}min`;
}

export function mhz(n: number): string {
  return n <= 0 ? '—' : n >= 1000 ? `${(n / 1000).toFixed(2).replace('.', ',')} GHz` : `${n} MHz`;
}

export function level(v: number, warn: number, bad: number): 'ok' | 'warn' | 'bad' {
  return v >= bad ? 'bad' : v >= warn ? 'warn' : 'ok';
}
