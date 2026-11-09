import { useEffect, useRef, useState } from 'react';
import { call } from '../bridge';
import { bytes } from '../format';
import off from '../assets/robo_off.png';
import ant from '../assets/robo_ant.png';
import on from '../assets/logo.png';

type Sys = { os?: string; osVersion?: string; osBuild?: string; cpu?: string; cpuCores?: number; cpuThreads?: number; gpus?: { name: string }[]; ramTotal?: number; ramSpeed?: number; board?: string };

/** Abertura: tela de boot retrô com os dados reais do PC e depois o robô ligando. Clique ou tecla pula. */
export default function Intro({ version, onDone }: { version: string; onDone: () => void }) {
  const [lines, setLines] = useState<string[]>([]);
  const [stage, setStage] = useState<'boot' | 'off' | 'ant' | 'on' | 'out'>('boot');
  const [typed, setTyped] = useState(0);
  const done = useRef(false);
  const finish = () => { if (!done.current) { done.current = true; onDone(); } };

  useEffect(() => {
    const timers: number[] = [];
    const at = (ms: number, fn: () => void) => timers.push(window.setTimeout(fn, ms));
    let cancelled = false;
    // espera os dados do PC no máximo 700 ms; sem eles, mostra só o essencial
    Promise.race([call<Sys>('sys.info').catch(() => ({} as Sys)), new Promise<Sys>(r => setTimeout(() => r({}), 700))]).then(s => {
      if (cancelled) return;
      const gpu = s.gpus?.[0]?.name;
      const all = [
        `GROBS BIOS v${version}`,
        '',
        s.cpu ? `CPU ...... ${s.cpu.replace(/\s+/g, ' ').trim()}${s.cpuCores ? ` (${s.cpuCores}N/${s.cpuThreads}T)` : ''}  OK` : 'CPU ...... OK',
        s.ramTotal ? `RAM ...... ${bytes(s.ramTotal, 0)}${s.ramSpeed ? ` ${s.ramSpeed} MHz` : ''}  OK` : 'RAM ...... OK',
        gpu ? `VIDEO .... ${gpu}  OK` : '',
        s.board ? `PLACA .... ${s.board}` : '',
        s.os ? `SISTEMA .. ${s.os}${s.osVersion ? ' ' + s.osVersion : ''}${s.osBuild ? ` (build ${s.osBuild})` : ''}` : '',
        '',
        'Iniciando GROBS Optimizer',
      ].filter((l, i, a) => l !== '' || (a[i - 1] ?? '') !== '');
      all.forEach((l, i) => at(110 * i, () => setLines(x => [...x, l])));
      const t = 110 * all.length + 350;
      at(t, () => setStage('off'));
      at(t + 300, () => setStage('ant'));
      at(t + 600, () => setStage('on'));
      'GROBS'.split('').forEach((_, i) => at(t + 650 + i * 90, () => setTyped(i + 1)));
      at(t + 1700, () => setStage('out'));
      at(t + 2000, finish);
    });
    const skip = () => finish();
    window.addEventListener('keydown', skip);
    return () => { cancelled = true; timers.forEach(clearTimeout); window.removeEventListener('keydown', skip); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className={`intro ${stage === 'out' ? 'out' : ''}`} onMouseDown={finish} title="Clique para pular">
      {stage === 'boot' ? (
        <div className="intro-boot">
          {lines.map((l, i) => <div key={i} className={l.endsWith('OK') ? 'ok' : i === 0 ? 'head' : ''}>{l || ' '}</div>)}
          <span className="intro-cursor">_</span>
        </div>
      ) : (
        <div className="intro-logo">
          <img src={stage === 'off' ? off : stage === 'ant' ? ant : on} width={128} height={128} alt="" />
          <div className="intro-name">{'GROBS'.slice(0, typed)}<span className="intro-cursor">{typed < 5 ? '_' : ''}</span></div>
          <div className={`intro-sub ${typed >= 5 ? 'show' : ''}`}>OPTIMIZER</div>
        </div>
      )}
      <div className="intro-skip">clique ou tecle para pular</div>
    </div>
  );
}
