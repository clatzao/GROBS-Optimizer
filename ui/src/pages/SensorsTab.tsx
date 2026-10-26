import { useEffect, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Card, Spinner } from '../components/ui';
import { appStore, run, useStore } from '../store';

type Sensor = { name: string; type: string; unit: string; value: number; min: number; max: number };
type Hw = { name: string; type: string; sensors: Sensor[]; sub: Hw[] };
type Tree = { open: boolean; opening: boolean; error: string | null; admin: boolean; pawnio: string | null; hardware: Hw[] };

const TYPE_LABEL: Record<string, string> = { Temperature: 'Temperaturas', Fan: 'Ventoinhas', Voltage: 'Voltagens', Clock: 'Clocks', Load: 'Uso', Power: 'Consumo', Control: 'Controle', Data: 'Dados', SmallData: 'Memória', Throughput: 'Transferência', Level: 'Nível', Current: 'Corrente', Energy: 'Energia', Frequency: 'Frequência' };
const HW_ICON: Record<string, [string, string]> = {
  Cpu: ['cpu', '#a78bfa'], GpuNvidia: ['gpu', '#76b900'], GpuAmd: ['gpu', '#ed1c24'], GpuIntel: ['gpu', '#0071c5'], Memory: ['memory', '#22d3ee'],
  Motherboard: ['dashboard', '#fbbf24'], SuperIO: ['thermo', '#fb7185'], Storage: ['disk', '#fbbf24'], Battery: ['battery', '#34d399'], Psu: ['power', '#f472b6'],
};

function fmt(s: Sensor, v: number) {
  if (s.unit === 'B/s') return v > 1e6 ? `${(v / 1e6).toFixed(1)} MB/s` : `${(v / 1e3).toFixed(0)} KB/s`;
  const d = s.unit === 'V' ? 3 : s.unit === 'W' || s.unit === 'GB' ? 1 : 0;
  return `${v.toFixed(d).replace('.', ',')} ${s.unit}`;
}

function HwCard({ hw }: { hw: Hw }) {
  const all = [hw, ...hw.sub];
  const sensors = all.flatMap(h => h.sensors);
  const groups = [...new Set(sensors.map(s => s.type))];
  const [ic, color] = HW_ICON[hw.type] ?? ['settings', '#a3a9bb'];
  if (sensors.length === 0) return null;
  return (
    <Card className="pad-0">
      <div className="row" style={{ padding: '12px 16px', borderBottom: '1px solid var(--line)' }}>
        <span style={{ width: 30, height: 30, borderRadius: 9, display: 'grid', placeItems: 'center', color, background: `${color}1f` }}><Icon name={ic} size={16} /></span>
        <b className="ellipsis">{hw.name}</b>
      </div>
      <table className="table" style={{ fontSize: 12.5 }}>
        <thead><tr><th>Sensor</th><th className="num">Atual</th><th className="num">Mín</th><th className="num">Máx</th></tr></thead>
        <tbody>
          {groups.map(g => [
            <tr key={g}><td colSpan={4} style={{ color: 'var(--text-3)', fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.8, paddingTop: 10 }}>{TYPE_LABEL[g] ?? g}</td></tr>,
            ...sensors.filter(s => s.type === g).map((s, i) => (
              <tr key={g + i}>
                <td className="ellipsis" style={{ maxWidth: 220 }}>{s.name}</td>
                <td className={`num ${s.type === 'Temperature' ? (s.value >= 90 ? 'bad-t' : s.value >= 75 ? 'warn-t' : '') : ''}`}><b>{fmt(s, s.value)}</b></td>
                <td className="num dim">{fmt(s, s.min)}</td>
                <td className="num dim">{fmt(s, s.max)}</td>
              </tr>
            )),
          ])}
        </tbody>
      </table>
    </Card>
  );
}

export default function SensorsTab() {
  const [tree, setTree] = useState<Tree | null>(null);
  const [pawn, setPawn] = useState<string>('');
  const admin = useStore(appStore, a => a?.admin);
  useEffect(() => {
    const load = () => call<Tree>('sensors.tree').then(setTree).catch(() => {});
    load();
    const t = window.setInterval(load, 2000);
    const off = on('pawnio', (s: any) => setPawn(s.state));
    return () => { clearInterval(t); off(); };
  }, []);

  async function install() {
    setPawn('working');
    const r = await run(call<any>('sensors.pawnio'));
    if (r) setPawn(r.state);
  }

  if (!tree || (!tree.open && tree.opening)) return <div className="empty"><Spinner size={24} /><div className="small dim" style={{ marginTop: 8 }}>Detectando sensores do hardware…</div></div>;
  return (
    <>
      {(!admin || !tree.pawnio) && (
        <Card style={{ marginBottom: 16, borderColor: 'rgba(139,92,246,.35)' }}>
          <div className="row" style={{ alignItems: 'flex-start' }}>
            <Icon name="info" size={18} style={{ color: '#a78bfa', marginTop: 2 }} />
            <div style={{ flex: 1 }}>
              <b>Para ler todos os sensores (temperatura por núcleo, placa-mãe, ventoinhas e voltagens)</b>
              <div className="small muted">
                {!admin ? 'Abra o GROBS como administrador. ' : ''}
                {!tree.pawnio ? 'Também é preciso o driver PawnIO — open source, assinado e aceito pelo Defender (substitui o antigo WinRing0, bloqueado desde 2025).' : ''}
              </div>
            </div>
            {admin && !tree.pawnio && (
              <button className="btn sm primary" onClick={install} disabled={pawn === 'working'}>{pawn === 'working' ? <Spinner size={12} /> : <Icon name="arrowDown" size={13} />}Instalar PawnIO</button>
            )}
          </div>
          {pawn.startsWith('error') && <div className="small bad-t" style={{ marginTop: 6 }}>{pawn}</div>}
        </Card>
      )}
      {tree.error && <Card style={{ marginBottom: 16 }}><div className="small bad-t">{tree.error}</div></Card>}
      <div className="grid g2" style={{ alignItems: 'start' }}>
        {tree.hardware.map((h, i) => <HwCard key={i} hw={h} />)}
      </div>
    </>
  );
}
