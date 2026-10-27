import { useEffect, useRef, useState } from 'react';
import { call, on } from '../bridge';
import { Icon } from '../components/Icon';
import { Card, CardTitle, Check, PageHead, Spinner } from '../components/ui';
import CountUp from '../components/rb/CountUp';
import { appStore, run, setSetting, toast, useStore } from '../store';

type Res = { cpuSingle?: number; cpuMulti?: number; threads?: number; ramCopy?: number; ramLatency?: number; diskSeqRead?: number; diskSeqWrite?: number; disk4kRead?: number; disk4kWrite?: number; gpu?: number; gpuName?: string; date?: string };
type St = { running: boolean; phase: string; result: Res | null; error: string | null };

// Referências (estimativa): CPU = pontos por núcleo de um i5-3470 (1000); GPU = pontuação de uma placa de entrada (GTX 1050 / RX 560) neste teste.
const CPU_REF = 1000, GPU_REF = 1300;

const FRAG = `precision highp float;
uniform vec2 r; uniform float t;
float sd(vec3 p){ vec3 q=p; q.xz=mod(q.xz+2.,4.)-2.; float s=length(q-vec3(0.,.9+.3*sin(t+p.x),0.))-.8;
  vec2 tq=vec2(length(q.xz)-1.3,q.y-.2); float tor=length(tq)-.18; return min(min(s,tor),p.y+.2+.05*sin(p.x*2.+t)*sin(p.z*2.)); }
vec3 nrm(vec3 p){ vec2 e=vec2(.002,0.); return normalize(vec3(sd(p+e.xyy)-sd(p-e.xyy),sd(p+e.yxy)-sd(p-e.yxy),sd(p+e.yyx)-sd(p-e.yyx))); }
float sh(vec3 ro,vec3 rd){ float k=1.,d=.05; for(int i=0;i<32;i++){ float h=sd(ro+rd*d); k=min(k,10.*h/d); d+=clamp(h,.02,.5); if(h<.001||d>20.)break; } return clamp(k,0.,1.); }
void main(){ vec2 uv=(gl_FragCoord.xy*2.-r)/r.y; vec3 ro=vec3(3.*sin(t*.3),2.2,3.*cos(t*.3)+t*.8), ta=ro+vec3(-sin(t*.3),-.45,-cos(t*.3));
  vec3 f=normalize(ta-ro), s=normalize(cross(f,vec3(0,1,0))), u=cross(s,f), rd=normalize(uv.x*s+uv.y*u+1.6*f);
  float d=0.; vec3 col=vec3(.03,.03,.06); for(int i=0;i<96;i++){ float h=sd(ro+rd*d); if(h<.0008){ vec3 p=ro+rd*d, n=nrm(p), l=normalize(vec3(.6,.8,.3));
    float dif=max(dot(n,l),0.)*sh(p+n*.01,l), sp=pow(max(dot(reflect(-l,n),-rd),0.),32.); vec3 base=mix(vec3(.55,.36,.96),vec3(.13,.83,.93),.5+.5*sin(p.x+p.z));
    col=base*(.08+dif)+sp*.6; col=mix(col,vec3(.03,.03,.06),1.-exp(-.03*d*d)); break; } d+=h; if(d>40.)break; }
  gl_FragColor=vec4(pow(col,vec3(.4545)),1.); }`;

/** Teste 3D em WebGL: mede quantas vezes por segundo a GPU renderiza a cena em 1280×720 (sem limite de V-Sync). */
function gpuTest(canvas: HTMLCanvasElement, seconds: number, onTick: (score: number, p: number) => void): Promise<{ score: number; name: string }> {
  return new Promise((resolve, reject) => {
    const gl = canvas.getContext('webgl', { antialias: false, preserveDrawingBuffer: false, powerPreference: 'high-performance' }) as WebGLRenderingContext | null;
    if (!gl) { reject(new Error('WebGL indisponível neste PC')); return; }
    const dbg = gl.getExtension('WEBGL_debug_renderer_info');
    const name = dbg ? String(gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL)) : 'GPU';
    const sh = (type: number, src: string) => { const s = gl.createShader(type)!; gl.shaderSource(s, src); gl.compileShader(s); if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) ?? 'shader'); return s; };
    const prog = gl.createProgram()!;
    try { gl.attachShader(prog, sh(gl.VERTEX_SHADER, 'attribute vec2 p;void main(){gl_Position=vec4(p,0.,1.);}')); gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, FRAG)); }
    catch (e) { reject(e); return; }
    gl.linkProgram(prog); gl.useProgram(prog);
    const buf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const loc = gl.getAttribLocation(prog, 'p'); gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
    const W = 1280, H = 720;
    const tex = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, tex); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, W, H, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    const fbo = gl.createFramebuffer(); gl.bindFramebuffer(gl.FRAMEBUFFER, fbo); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    const uR = gl.getUniformLocation(prog, 'r'), uT = gl.getUniformLocation(prog, 't');
    let frames = 0; const t0 = performance.now(); let warm = true; let tStart = t0; let batch = 1;
    const px = new Uint8Array(4);
    const step = () => {
      if (gl.isContextLost()) { reject(new Error('A GPU reiniciou durante o teste (driver)')); return; }
      const now = performance.now();
      if (warm && now - t0 > 1500) { warm = false; frames = 0; tStart = now; }
      // lote de renderizações + readPixels (espera a GPU terminar de verdade; gl.finish não bloqueia no ANGLE).
      // O lote cresce até ~40 ms por quadro: mede a GPU e não o V-Sync, sem enfileirar trabalho demais.
      gl.bindFramebuffer(gl.FRAMEBUFFER, fbo); gl.viewport(0, 0, W, H); gl.uniform2f(uR, W, H);
      const b0 = performance.now();
      for (let i = 0; i < batch; i++) { gl.uniform1f(uT, (performance.now() - t0) / 1000); gl.drawArrays(gl.TRIANGLES, 0, 3); }
      gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
      frames += batch;
      if (performance.now() - b0 < 25 && batch < 512) batch *= 2;
      gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, canvas.width, canvas.height); gl.uniform2f(uR, canvas.width, canvas.height); gl.drawArrays(gl.TRIANGLES, 0, 3);
      const el = (performance.now() - tStart) / 1000;
      if (!warm) onTick(frames / el, Math.min(1, el / seconds));
      if (!warm && el >= seconds) { resolve({ score: frames / el, name }); return; }
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  });
}

export default function Bench() {
  const app = useStore(appStore);
  const history: Res[] = ((app?.settings as any)?.benchHistory as Res[]) ?? [];
  const [sel, setSel] = useState({ cpu: true, ram: true, disk: true, gpu: true });
  const [st, setSt] = useState<St>({ running: false, phase: '', result: null, error: null });
  const [gpu, setGpu] = useState<{ score: number; p: number; name?: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const canvas = useRef<HTMLCanvasElement>(null);
  const pending = useRef<Res | null>(null);

  useEffect(() => on('bench', (s: St) => setSt(s)), []);

  async function start() {
    setBusy(true); setGpu(null); pending.current = null;
    const res: Res = {};
    try {
      if (sel.cpu || sel.ram || sel.disk) {
        await call('bench.start', { cpu: sel.cpu, ram: sel.ram, disk: sel.disk });
        const done = await new Promise<St>(resolve => { const off = on('bench', (s: St) => { if (!s.running) { off(); resolve(s); } }); });
        if (done.error) toast(done.error, 'err');
        Object.assign(res, done.result ?? {});
      }
      if (sel.gpu && canvas.current) {
        setSt(s => ({ ...s, running: true, phase: 'GPU — teste 3D' }));
        const g = await gpuTest(canvas.current, 15, (score, p) => setGpu({ score, p }));
        res.gpu = Math.round(g.score * 10); res.gpuName = g.name;
        setGpu({ score: g.score, p: 1, name: g.name });
      }
    } catch (e: any) { toast(e?.message ?? 'Falha no teste', 'err'); }
    res.date = new Date().toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
    setSt({ running: false, phase: '', result: res, error: null });
    setBusy(false);
    if (Object.keys(res).length > 1) await setSetting('benchHistory' as any, [res, ...history].slice(0, 10) as any);
  }

  const r = st.result ?? history[0] ?? null;
  const prev = st.result ? history[1] : history[1];
  const delta = (k: keyof Res, lowerBetter = false) => {
    if (!r || !prev || typeof r[k] !== 'number' || typeof prev[k] !== 'number' || !(prev[k] as number)) return null;
    const d = (((r[k] as number) - (prev[k] as number)) / (prev[k] as number)) * 100 * (lowerBetter ? -1 : 1);
    return Math.abs(d) < 1 ? null : <span className={`small ${d > 0 ? 'ok-t' : 'bad-t'}`}>{d > 0 ? '+' : ''}{d.toFixed(0)}%</span>;
  };
  const tile = (title: string, icon: string, color: string, v: number | undefined, unit: string, k: keyof Res, lowerBetter = false, digits = 0) => (
    <Card>
      <div className="row small muted" style={{ gap: 6 }}><Icon name={icon} size={14} style={{ color }} />{title}<span className="spacer" />{delta(k, lowerBetter)}</div>
      <div className="stat-big" style={{ marginTop: 8, fontSize: 28 }}>{v === undefined ? <span className="dim">—</span> : <CountUp to={v} duration={1} separator="." decimals={digits} />}<span className="small dim" style={{ marginLeft: 6, fontWeight: 400 }}>{unit}</span></div>
    </Card>
  );

  // equilíbrio CPU × GPU (estimativa a partir das pontuações)
  let balance: { v: number; text: string } | null = null;
  if (r?.cpuSingle && r?.gpu) {
    const v = Math.log2((r.gpu / GPU_REF) / (r.cpuSingle / CPU_REF));
    balance = { v, text: v > 1.3 ? 'A placa de vídeo é bem mais forte que o processador: em jogos competitivos/CPU-pesados (e em FPS alto) o processador tende a limitar. Priorize um upgrade de CPU/RAM.' :
      v < -1.3 ? 'O processador é bem mais forte que a placa de vídeo: em jogos pesados a GPU vai limitar. Baixe resolução/qualidade gráfica ou use DLSS/FSR; o próximo upgrade é a placa de vídeo.' :
        'CPU e GPU estão equilibradas para o seu nível de desempenho.' };
  }

  return (
    <div className="page">
      <PageHead title="Benchmark" sub="Teste rápido para comparar antes e depois das otimizações — e ver quem limita os seus jogos."
        right={<>
          {(['cpu', 'ram', 'disk', 'gpu'] as const).map(k => (
            <label key={k} className="row small" style={{ gap: 6, cursor: 'pointer' }}><Check on={sel[k]} onChange={v => setSel(s => ({ ...s, [k]: v }))} disabled={busy} />{{ cpu: 'CPU', ram: 'RAM', disk: 'Disco', gpu: 'GPU' }[k]}</label>
          ))}
          <button className="btn primary lg" onClick={start} disabled={busy || !Object.values(sel).some(Boolean)}>{busy ? <Spinner size={16} /> : <Icon name="play" size={16} />}Iniciar (~1 min)</button>
        </>} />
      {busy && (
        <Card style={{ marginBottom: 16 }}>
          <div className="row"><Spinner color="var(--accent)" /><b>{st.phase || 'Preparando…'}</b><span className="spacer" /><span className="small dim">Feche jogos e programas pesados durante o teste para um resultado justo.</span></div>
        </Card>
      )}
      <div className="grid g4">
        {tile('CPU — 1 núcleo', 'cpu', '#a78bfa', r?.cpuSingle, 'pontos', 'cpuSingle')}
        {tile(`CPU — ${r?.threads ?? 'todos os'} núcleos`, 'cpu', '#a78bfa', r?.cpuMulti, 'pontos', 'cpuMulti')}
        {tile('RAM — cópia', 'memory', '#22d3ee', r?.ramCopy, 'GB/s', 'ramCopy', false, 1)}
        {tile('RAM — latência', 'memory', '#22d3ee', r?.ramLatency, 'ns', 'ramLatency', true, 1)}
        {tile('Disco — sequencial (leitura)', 'disk', '#fbbf24', r?.diskSeqRead, 'MB/s', 'diskSeqRead')}
        {tile('Disco — sequencial (gravação)', 'disk', '#fbbf24', r?.diskSeqWrite, 'MB/s', 'diskSeqWrite')}
        {tile('Disco — 4K (leitura)', 'disk', '#fbbf24', r?.disk4kRead, 'MB/s', 'disk4kRead', false, 1)}
        {tile('GPU — teste 3D (720p)', 'gpu', '#f472b6', r?.gpu, 'pontos', 'gpu')}
      </div>
      <div className="grid g2" style={{ marginTop: 16 }}>
        <Card>
          <CardTitle icon="gpu" color="#f472b6" right={gpu?.name || r?.gpuName ? <span className="chip ellipsis" style={{ maxWidth: 260 }}>{gpu?.name ?? r?.gpuName}</span> : undefined}>Teste 3D</CardTitle>
          <canvas ref={canvas} width={480} height={270} style={{ width: '100%', aspectRatio: '16 / 9', borderRadius: 12, background: '#07080c', border: '1px solid var(--line)', display: 'block' }} />
          {gpu && gpu.p < 1 && <div className="small dim" style={{ marginTop: 6 }}>{(gpu.score * 10).toFixed(0)} pontos · {Math.round(gpu.p * 100)}%</div>}
        </Card>
        <Card>
          <CardTitle icon="activity" color="#34d399">CPU × GPU</CardTitle>
          {!balance ? <div className="small muted">Rode o teste de CPU e de GPU para ver o equilíbrio do seu PC.</div> : (
            <>
              <div className="row small muted" style={{ justifyContent: 'space-between' }}><span>GPU limita</span><span>equilibrado</span><span>CPU limita</span></div>
              <div style={{ position: 'relative', height: 12, borderRadius: 8, margin: '6px 0 14px', background: 'linear-gradient(90deg, #f472b6, #34d399 40%, #34d399 60%, #a78bfa)' }}>
                <i style={{ position: 'absolute', top: -4, left: `calc(${Math.max(2, Math.min(98, 50 + balance.v * 15))}% - 2px)`, width: 4, height: 20, borderRadius: 2, background: '#fff', boxShadow: '0 0 10px #fff' }} />
              </div>
              <p style={{ margin: 0 }}>{balance.text}</p>
              <p className="small dim" style={{ marginBottom: 0 }}>Estimativa a partir das pontuações. A medida real é a <b>análise de gargalo</b> durante o jogo (aba Gamer → Sessões).</p>
            </>
          )}
        </Card>
      </div>
      {history.length > 1 && (
        <>
          <h2 className="sec"><Icon name="clock" size={16} />Histórico</h2>
          <Card className="pad-0">
            <table className="table">
              <thead><tr><th>Data</th><th className="num">CPU 1 núcleo</th><th className="num">CPU todos</th><th className="num">RAM GB/s</th><th className="num">Latência</th><th className="num">Disco seq.</th><th className="num">Disco 4K</th><th className="num">GPU</th></tr></thead>
              <tbody>{history.map((h, i) => (
                <tr key={i}><td>{h.date}</td><td className="num">{h.cpuSingle ?? '—'}</td><td className="num">{h.cpuMulti ?? '—'}</td><td className="num">{h.ramCopy ?? '—'}</td><td className="num">{h.ramLatency ? `${h.ramLatency} ns` : '—'}</td>
                  <td className="num">{h.diskSeqRead ? Math.round(h.diskSeqRead) : '—'}</td><td className="num">{h.disk4kRead ?? '—'}</td><td className="num">{h.gpu ?? '—'}</td></tr>
              ))}</tbody>
            </table>
          </Card>
        </>
      )}
    </div>
  );
}
