import { motion, AnimatePresence } from 'motion/react';
import { Children, ReactNode, useId, useMemo } from 'react';
import { Icon } from './Icon';
import SpotlightCard from './rb/SpotlightCard';
import { toastStore, useStore } from '../store';

export function Card({ children, className = '', spotlight = false, color = 'rgba(139, 92, 246, 0.22)', style }: { children: ReactNode; className?: string; spotlight?: boolean; color?: string; style?: React.CSSProperties }) {
  if (spotlight)
    return <SpotlightCard className={`card ${className}`} spotlightColor={color} intensity={0.9} spotlightSize={380} borderGlow={0.6} grain={0} style={style}>{children}</SpotlightCard>;
  return <div className={`card ${className}`} style={style}>{children}</div>;
}

export function CardTitle({ icon, children, right, color }: { icon?: string; children: ReactNode; right?: ReactNode; color?: string }) {
  return (
    <div className="card-title">
      {icon && <span className="ico" style={color ? { color, background: `color-mix(in srgb, ${color} 14%, transparent)` } : undefined}><Icon name={icon} size={16} /></span>}
      <span className="ellipsis">{children}</span>
      {right && <><span className="spacer" />{right}</>}
    </div>
  );
}

export function PageHead({ title, sub, right }: { title: ReactNode; sub?: ReactNode; right?: ReactNode }) {
  return (
    <div className="page-head">
      <div>
        <h1>{title}</h1>
        {sub && <p>{sub}</p>}
      </div>
      {right && <div className="row wrap">{right}</div>}
    </div>
  );
}

export function Toggle({ on, onChange, disabled, title }: { on: boolean; onChange: (v: boolean) => void; disabled?: boolean; title?: string }) {
  return <button type="button" role="switch" aria-checked={on} title={title} className={`switch ${on ? 'on' : ''}`} disabled={disabled} onClick={() => onChange(!on)} />;
}

export function Check({ on, onChange, disabled }: { on: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <button type="button" role="checkbox" aria-checked={on} className={`check ${on ? 'on' : ''}`} disabled={disabled} onClick={() => onChange(!on)}>
      <svg viewBox="0 0 24 24"><path d="M6 12.5l4 4 8-9" /></svg>
    </button>
  );
}

export function Spinner({ size = 18, color }: { size?: number; color?: string }) {
  return <span className="spinner" style={{ ['--s' as any]: `${size}px`, color: color ?? 'currentColor' }} />;
}

export function Beam({ size = 120, duration = 6, c1, c2, width = 1.5 }: { size?: number; duration?: number; c1?: string; c2?: string; width?: number }) {
  return <span className="beam" style={{ ['--size' as any]: `${size}px`, ['--dur' as any]: `${duration}s`, ['--c1' as any]: c1, ['--c2' as any]: c2, ['--bw' as any]: `${width}px` }} />;
}

/** Medidor circular com gradiente. */
export function Gauge({ value, size = 120, stroke = 10, children, colors, track = 'rgba(255,255,255,0.06)' }: { value: number; size?: number; stroke?: number; children?: ReactNode; colors?: [string, string]; track?: string }) {
  const id = useId().replace(/:/g, '');
  const r = 50 - stroke / 2;
  const c = 2 * Math.PI * r;
  const v = Math.max(0, Math.min(100, value));
  const [a, b] = colors ?? ['#8b5cf6', '#22d3ee'];
  return (
    <div className="gauge" style={{ width: size, height: size }}>
      <svg viewBox="0 0 100 100">
        <defs>
          <linearGradient id={`g${id}`} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor={a} />
            <stop offset="100%" stopColor={b} />
          </linearGradient>
        </defs>
        <circle cx="50" cy="50" r={r} strokeWidth={stroke} style={{ stroke: track }} />
        <circle cx="50" cy="50" r={r} strokeWidth={stroke} style={{ stroke: `url(#g${id})`, strokeDasharray: `${(v / 100) * c} ${c}`, opacity: v > 0 ? 1 : 0, filter: `drop-shadow(0 0 6px ${a}66)` }} />
      </svg>
      <div className="val">{children}</div>
    </div>
  );
}

/** Gráfico de área leve em SVG (sem biblioteca). */
export function Spark({ data, max, height = 48, color = '#8b5cf6', fill = true, points = 60 }: { data: number[]; max?: number; height?: number; color?: string; fill?: boolean; points?: number }) {
  const id = useId().replace(/:/g, '');
  const path = useMemo(() => {
    if (data.length < 2) return { line: '', area: '' };
    const m = max ?? Math.max(1, ...data) * 1.15;
    const w = 100 / (points - 1);
    const off = points - data.length;
    const pts = data.map((v, i) => [(off + i) * w, height - (Math.min(v, m) / m) * (height - 2) - 1]);
    let line = `M${pts[0][0].toFixed(2)},${pts[0][1].toFixed(2)}`;
    for (let i = 1; i < pts.length; i++) {
      const [x0, y0] = pts[i - 1], [x1, y1] = pts[i];
      const cx = (x0 + x1) / 2;
      line += ` C${cx.toFixed(2)},${y0.toFixed(2)} ${cx.toFixed(2)},${y1.toFixed(2)} ${x1.toFixed(2)},${y1.toFixed(2)}`;
    }
    const area = `${line} L${pts[pts.length - 1][0].toFixed(2)},${height} L${pts[0][0].toFixed(2)},${height} Z`;
    return { line, area };
  }, [data, max, height, points]);
  return (
    <svg viewBox={`0 0 100 ${height}`} preserveAspectRatio="none" width="100%" height={height} style={{ display: 'block', overflow: 'visible' }}>
      <defs>
        <linearGradient id={`s${id}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.35" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      {fill && <path d={path.area} fill={`url(#s${id})`} />}
      <path d={path.line} fill="none" stroke={color} strokeWidth="1.6" vectorEffect="non-scaling-stroke" strokeLinejoin="round" />
    </svg>
  );
}

export function Bar({ value, kind }: { value: number; kind?: 'ok' | 'warn' | 'bad' }) {
  return <div className={`bar ${kind ?? ''}`}><i style={{ ["--v" as any]: Math.max(0, Math.min(100, value)) / 100 }} /></div>;
}

export function Tabs<T extends string>({ value, onChange, items, layoutId }: { value: T; onChange: (v: T) => void; items: { id: T; label: string }[]; layoutId: string }) {
  return (
    <div className="tabs">
      {items.map(it => (
        <button key={it.id} className={`tab ${value === it.id ? 'active' : ''}`} onClick={() => onChange(it.id)}>
          {value === it.id && <motion.i className="tabbg" layoutId={layoutId} transition={{ type: 'spring', stiffness: 500, damping: 38 }} />}
          <span>{it.label}</span>
        </button>
      ))}
    </div>
  );
}

export function Modal({ open, onClose, children }: { open: boolean; onClose: () => void; children: ReactNode }) {
  return (
    <AnimatePresence>
      {open && (
        <motion.div className="overlay" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onMouseDown={e => e.target === e.currentTarget && onClose()}>
          <motion.div className="modal" initial={{ opacity: 0, y: 16, scale: 0.97 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: 10, scale: 0.98 }} transition={{ type: 'spring', stiffness: 420, damping: 32 }}>
            {children}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

export function Toasts() {
  const list = useStore(toastStore);
  const icon = { ok: 'check', err: 'x', warn: 'alert', info: 'info' } as const;
  return (
    <div className="toasts">
      <AnimatePresence initial={false}>
        {list.map(t => (
          <motion.div key={t.id} layout className={`toast ${t.kind}`} initial={{ opacity: 0, x: 40, scale: 0.96 }} animate={{ opacity: 1, x: 0, scale: 1 }} exit={{ opacity: 0, x: 40 }} transition={{ type: 'spring', stiffness: 400, damping: 30 }}>
            <Icon name={icon[t.kind]} size={18} />
            <span>{t.text}</span>
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  );
}

export function AdminLock({ text = 'Requer administrador' }: { text?: string }) {
  return <span className="chip warn" title={text}><Icon name="lock" size={11} />Admin</span>;
}

export function Stagger({ children, className = '', delay = 0.04 }: { children: ReactNode[] | ReactNode; className?: string; delay?: number }) {
  const arr = Children.toArray(children);
  return (
    <div className={className}>
      {arr.map((c, i) => (
        <motion.div key={(c as any)?.key ?? i} initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * delay, duration: 0.45, ease: [0.22, 1, 0.36, 1] }} style={{ minWidth: 0 }}>
          {c}
        </motion.div>
      ))}
    </div>
  );
}
