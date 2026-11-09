// Gera a fonte pixel do GROBS (src/assets/grobs-pixel.ttf) a partir dos desenhos abaixo.
// É TrueType com tabela gasp "só grid-fit, sem cinza": assim o Chrome/WebView2 desenha a fonte sem
// suavização (cada pixel aceso ou apagado). Fonte vetorial comum ganha um borrão horizontal no Windows.
// Rodar: node scripts/build-font.mjs
// Cada letra: linhas separadas por "/", "#" = pixel aceso. Linha 0 = topo das maiúsculas (7 de altura),
// linha 6 = assenta na base; linhas 7 e 8 = descendentes (g, j, p, q, y). Em 12 px cada pixel do desenho
// vira exatamente 1 pixel da tela (24 px = 2x, 36 px = 3x).
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const U = 100;            // unidades por pixel
const EM = 12 * U;        // 12 px = 1 pixel por unidade
const ASC = 10 * U, DESC = 2 * U;

const G = {
  A: '.###./#...#/#...#/#####/#...#/#...#/#...#', B: '####./#...#/#...#/####./#...#/#...#/####.', C: '.###./#...#/#..../#..../#..../#...#/.###.',
  D: '####./#...#/#...#/#...#/#...#/#...#/####.', E: '#####/#..../#..../####./#..../#..../#####', F: '#####/#..../#..../####./#..../#..../#....',
  G: '.###./#...#/#..../#.###/#...#/#...#/.####', H: '#...#/#...#/#...#/#####/#...#/#...#/#...#', I: '###/.#./.#./.#./.#./.#./###',
  J: '..###/...#./...#./...#./...#./#..#./.##..', K: '#...#/#..#./#.#../##.../#.#../#..#./#...#', L: '#..../#..../#..../#..../#..../#..../#####',
  M: '#...#/##.##/#.#.#/#.#.#/#...#/#...#/#...#', N: '#...#/##..#/#.#.#/#..##/#...#/#...#/#...#', O: '.###./#...#/#...#/#...#/#...#/#...#/.###.',
  P: '####./#...#/#...#/####./#..../#..../#....', Q: '.###./#...#/#...#/#...#/#.#.#/#..#./.##.#', R: '####./#...#/#...#/####./#.#../#..#./#...#',
  S: '.####/#..../#..../.###./....#/....#/####.', T: '#####/..#../..#../..#../..#../..#../..#..', U: '#...#/#...#/#...#/#...#/#...#/#...#/.###.',
  V: '#...#/#...#/#...#/#...#/#...#/.#.#./..#..', W: '#...#/#...#/#...#/#.#.#/#.#.#/##.##/#...#', X: '#...#/#...#/.#.#./..#../.#.#./#...#/#...#',
  Y: '#...#/#...#/.#.#./..#../..#../..#../..#..', Z: '#####/....#/...#./..#../.#.../#..../#####',

  a: '...../...../.###./....#/.####/#...#/.####', b: '#..../#..../####./#...#/#...#/#...#/####.', c: '..../..../.###/#.../#.../#.../.###',
  d: '....#/....#/.####/#...#/#...#/#...#/.####', e: '...../...../.###./#...#/#####/#..../.###.', f: '..##/.#../###./.#../.#../.#../.#..',
  g: '...../...../.####/#...#/#...#/#...#/.####/....#/.###.', h: '#..../#..../####./#...#/#...#/#...#/#...#', i: '#/./#/#/#/#/#',
  j: '..#/.../..#/..#/..#/..#/..#/..#/##.', k: '#.../#.../#..#/#.#./##../#.#./#..#', l: '#./#./#./#./#./#./.#',
  m: '...../...../##.#./#.#.#/#.#.#/#.#.#/#.#.#', n: '...../...../####./#...#/#...#/#...#/#...#', o: '...../...../.###./#...#/#...#/#...#/.###.',
  p: '...../...../####./#...#/#...#/#...#/####./#..../#....', q: '...../...../.####/#...#/#...#/#...#/.####/....#/....#',
  r: '..../..../#.##/##../#.../#.../#...', s: '...../...../.####/#..../.###./....#/####.', t: '..../.#../###./.#../.#../.#../..##',
  u: '...../...../#...#/#...#/#...#/#..##/.##.#', v: '...../...../#...#/#...#/#...#/.#.#./..#..', w: '...../...../#...#/#...#/#.#.#/#.#.#/.#.#.',
  x: '...../...../#...#/.#.#./..#../.#.#./#...#', y: '...../...../#...#/#...#/#...#/#...#/.####/....#/.###.', z: '...../...../#####/...#./..#../.#.../#####',

  0: '.###./#...#/#..##/#.#.#/##..#/#...#/.###.', 1: '.#./##./.#./.#./.#./.#./###', 2: '.###./#...#/....#/...#./..#../.#.../#####',
  3: '####./....#/....#/.###./....#/....#/####.', 4: '...#./..##./.#.#./#..#./#####/...#./...#.', 5: '#####/#..../####./....#/....#/#...#/.###.',
  6: '.###./#..../#..../####./#...#/#...#/.###.', 7: '#####/....#/...#./..#../.#.../.#.../.#...', 8: '.###./#...#/#...#/.###./#...#/#...#/.###.',
  9: '.###./#...#/#...#/.####/....#/....#/.###.',

  '.': '././././././#', ',': '../../../../../../.#/#.', ':': '././#/././#', ';': '../../.#/../../.#/#.',
  '!': '#/#/#/#/#/./#', '?': '.###./#...#/....#/...#./..#../...../..#..', '-': '.../.../.../###', '–': '..../..../..../####', '—': '....../....../....../######',
  '+': '...../..#../..#../#####/..#../..#..', '=': '..../..../####/..../####', '/': '....#/...#./...#./..#../.#.../.#.../#....',
  '\\': '#..../.#.../.#.../..#../...#./...#./....#', '(': '.#/#./#./#./#./#./.#', ')': '#./.#/.#/.#/.#/.#/#.', '[': '##/#./#./#./#./#./##', ']': '##/.#/.#/.#/.#/.#/##',
  '%': '##..#/##.#./...#./..#../.#.../.#.##/#..##', '·': './././#', '…': '...../...../...../...../...../...../#.#.#', '"': '#.#/#.#', "'": '#/#',
  '“': '#.#/#.#', '”': '#.#/#.#', '‘': '#/#', '’': '#/#', '°': '.#./#.#/.#.', '_': '...../...../...../...../...../...../...../#####',
  '&': '.##../#..#./#.#../.#.../#.#.#/#..#./.##.#', '#': '.#.#./#####/.#.#./.#.#./#####/.#.#.', '*': '...../..#../#.#.#/.###./#.#.#/..#..',
  '<': '...#/..#./.#../#.../.#../..#./...#', '>': '#.../.#../..#./...#/..#./.#../#...', '@': '.###./#...#/#.###/#.#.#/#.###/#..../.###.',
  '|': '#/#/#/#/#/#/#/#/#', '~': '...../...../...../.##.#/#.##.', '$': '..#../.####/#.#../.###./..#.#/####./..#..', '^': '..#../.#.#./#...#',
  '→': '...../...../...#./#####/...#.', '←': '...../...../.#.../#####/.#...', '↑': '..#../.###./#.#.#/..#../..#../..#../..#..', '↓': '..#../..#../..#../..#../#.#.#/.###./..#..',
  '×': '...../...../#...#/.#.#./..#../.#.#./#...#', 'º': '###/#.#/###/.../###', 'ª': '.##/#.#/.##/.../###', '{': '.##/.#./.#./#../.#./.#./.##', '}': '##./.#./.#./..#/.#./.#./##.',
};

// acentos: compostos sobre a letra base (minúsculas: linhas 0-1; maiúsculas: linhas -3 e -2)
const MARK = { acute: ['...#.', '..#..'], grave: ['.#...', '..#..'], circ: ['..#..', '.#.#.'], tilde: ['.##.#', '#..#.'], diaer: ['.#.#.', '.....'] };
const ACC = {
  á: ['a', 'acute'], à: ['a', 'grave'], â: ['a', 'circ'], ã: ['a', 'tilde'], ä: ['a', 'diaer'], é: ['e', 'acute'], è: ['e', 'grave'], ê: ['e', 'circ'], ë: ['e', 'diaer'],
  í: ['i', 'acute'], ì: ['i', 'grave'], î: ['i', 'circ'], ï: ['i', 'diaer'], ó: ['o', 'acute'], ò: ['o', 'grave'], ô: ['o', 'circ'], õ: ['o', 'tilde'], ö: ['o', 'diaer'],
  ú: ['u', 'acute'], ù: ['u', 'grave'], û: ['u', 'circ'], ü: ['u', 'diaer'], ñ: ['n', 'tilde'],
  Á: ['A', 'acute'], À: ['A', 'grave'], Â: ['A', 'circ'], Ã: ['A', 'tilde'], Ä: ['A', 'diaer'], É: ['E', 'acute'], È: ['E', 'grave'], Ê: ['E', 'circ'], Ë: ['E', 'diaer'],
  Í: ['I', 'acute'], Ì: ['I', 'grave'], Î: ['I', 'circ'], Ï: ['I', 'diaer'], Ó: ['O', 'acute'], Ò: ['O', 'grave'], Ô: ['O', 'circ'], Õ: ['O', 'tilde'], Ö: ['O', 'diaer'],
  Ú: ['U', 'acute'], Ù: ['U', 'grave'], Û: ['U', 'circ'], Ü: ['U', 'diaer'], Ñ: ['N', 'tilde'],
};

// grade: mapa linha -> string (linhas podem ser negativas para acentos de maiúsculas)
function grid(ch) {
  if (G[ch] !== undefined) { const m = new Map(); G[ch].split('/').forEach((r, i) => m.set(i, r)); return m; }
  if (ACC[ch]) {
    const [base, mk] = ACC[ch];
    const m = grid(base);
    const w = Math.max(...[...m.values()].map(r => r.length));
    let marks = MARK[mk].map(r => r.slice(Math.max(0, Math.floor((5 - w) / 2)), Math.max(0, Math.floor((5 - w) / 2)) + Math.max(w, 3)));
    if (w < 3) marks = MARK[mk].map(r => r.slice(1, 4)); // i: acento de 3 px centralizado
    const upper = base === base.toUpperCase();
    const top = upper ? -3 : 0;
    if (!upper && base === 'i') m.set(0, '.'.repeat(w)); // tira o pingo do i
    marks.forEach((r, i) => { if (r.includes('#')) m.set(top + i, r); });
    // i acentuado fica com 3 de largura: recentraliza a haste
    if (w < 3) for (const [k, r] of m) if (k >= 2) m.set(k, '.' + r + '.');
    return m;
  }
  if (ch === 'ç' || ch === 'Ç') { const m = grid(ch === 'ç' ? 'c' : 'C'); const w = m.get(6).length; m.set(7, '..#..'.slice(0, w)); m.set(8, '.##..'.slice(0, w)); return m; }
  return null;
}

// ------------------------------------------------------------------ glifos
const list = [];
for (const ch of new Set([...Object.keys(G), ...Object.keys(ACC), 'ç', 'Ç'])) {
  const m = grid(ch);
  if (!m) continue;
  const w = Math.max(...[...m.values()].map(r => r.length));
  const rects = [];
  for (const [row, r] of m)
    for (let x = 0; x < r.length; x++) {
      if (r[x] !== '#') continue;
      let n = 1; while (r[x + n] === '#') n++;
      rects.push([x * U, (6 - row) * U, (x + n) * U, (7 - row) * U]);
      x += n - 1;
    }
  list.push({ cp: ch.codePointAt(0), adv: (w + 1) * U, rects });
}
list.push({ cp: 32, adv: 4 * U, rects: [] }, { cp: 160, adv: 4 * U, rects: [] });
list.sort((a, b) => a.cp - b.cp);
const glyphs = [{ cp: -1, adv: 6 * U, rects: [] }, ...list]; // 0 = .notdef

// ------------------------------------------------------------------ TrueType
const B = n => Buffer.alloc(n);
const u16 = v => { const b = B(2); b.writeUInt16BE(v & 0xffff); return b; };
const i16 = v => { const b = B(2); b.writeInt16BE(v); return b; };
const u32 = v => { const b = B(4); b.writeUInt32BE(v >>> 0); return b; };
const cat = a => Buffer.concat(a);

const glyf = [], loca = [0], hmtx = [];
let maxPts = 0, maxCont = 0, xMinAll = 0, yMinAll = 0, xMaxAll = 0, yMaxAll = 0, advMax = 0;
for (const g of glyphs) {
  advMax = Math.max(advMax, g.adv);
  if (!g.rects.length) { hmtx.push(u16(g.adv), i16(0)); loca.push(loca.at(-1)); continue; }
  const xMin = Math.min(...g.rects.map(r => r[0])), yMin = Math.min(...g.rects.map(r => r[1]));
  const xMax = Math.max(...g.rects.map(r => r[2])), yMax = Math.max(...g.rects.map(r => r[3]));
  xMinAll = Math.min(xMinAll, xMin); yMinAll = Math.min(yMinAll, yMin); xMaxAll = Math.max(xMaxAll, xMax); yMaxAll = Math.max(yMaxAll, yMax);
  const pts = [];
  for (const [x0, y0, x1, y1] of g.rects) pts.push([x0, y0], [x0, y1], [x1, y1], [x1, y0]); // sentido horário
  maxPts = Math.max(maxPts, pts.length); maxCont = Math.max(maxCont, g.rects.length);
  const parts = [i16(g.rects.length), i16(xMin), i16(yMin), i16(xMax), i16(yMax)];
  g.rects.forEach((_, k) => parts.push(u16(k * 4 + 3)));
  parts.push(u16(0)); // sem instruções
  parts.push(Buffer.from(pts.map(() => 1))); // todos os pontos na curva
  let px = 0; for (const [x] of pts) { parts.push(i16(x - px)); px = x; }
  let py = 0; for (const [, y] of pts) { parts.push(i16(y - py)); py = y; }
  let data = cat(parts); if (data.length % 4) data = cat([data, B(4 - data.length % 4)]);
  glyf.push(data); loca.push(loca.at(-1) + data.length);
  hmtx.push(u16(g.adv), i16(xMin));
}

const n = glyphs.length;
const head = cat([u32(0x00010000), u32(0x00010000), u32(0), u32(0x5F0F3CF5), u16(0x000B), u16(EM), B(8), B(8),
  i16(xMinAll), i16(yMinAll), i16(xMaxAll), i16(yMaxAll), u16(0), u16(8), i16(2), i16(1), i16(0)]);
const hhea = cat([u32(0x00010000), i16(ASC), i16(-DESC), i16(0), u16(advMax), i16(0), i16(0), i16(xMaxAll), i16(1), i16(0), i16(0), B(8), i16(0), u16(n)]);
const maxp = cat([u32(0x00010000), u16(n), u16(maxPts), u16(maxCont), u16(0), u16(0), u16(2), u16(0), u16(0), u16(0), u16(0), u16(1), u16(3), u16(0), u16(0)]);
// prep mínimo (PUSHB 0; POP): o Chrome só respeita a gasp em fonte que tem instruções de hinting
const prep = Buffer.from([0xB0, 0x00, 0x21]);
const cps = list.map(g => g.cp);
const os2 = cat([u16(4), i16(5 * U), u16(400), u16(5), u16(0),
  i16(6 * U), i16(6 * U), i16(0), i16(U), i16(6 * U), i16(6 * U), i16(0), i16(4 * U), i16(U), i16(2 * U), i16(0),
  B(10), u32(0b11), u32(0), u32(0), u32(0), Buffer.from('GROB'), u16(0x40 | 0x80), u16(Math.min(...cps)), u16(Math.min(0xffff, Math.max(...cps))),
  i16(ASC), i16(-DESC), i16(0), u16(ASC), u16(DESC), u32(1), u32(0), i16(5 * U), i16(7 * U), u16(0), u16(32), u16(1)]);
// gasp: em qualquer tamanho, só grid-fit e nenhum tom de cinza -> texto sem suavização
const gasp = cat([u16(1), u16(1), u16(0xffff), u16(0x0001)]);
const post = cat([u32(0x00030000), u32(0), i16(-U), i16(U), u32(0), u32(0), u32(0), u32(0), u32(0)]);

// cmap formato 4: segmentos de códigos seguidos -> glifos seguidos
const segs = [];
list.forEach((g, k) => { const gid = k + 1; const s = segs.at(-1); if (s && g.cp === s.end + 1 && gid === s.gid + (g.cp - s.start)) s.end = g.cp; else segs.push({ start: g.cp, end: g.cp, gid }); });
segs.push({ start: 0xffff, end: 0xffff, gid: 1 - 0xffff + 0xffff * 0 });
const sc = segs.length, sr = 2 * 2 ** Math.floor(Math.log2(sc));
const f4 = cat([u16(4), u16(16 + sc * 8), u16(0), u16(sc * 2), u16(sr), u16(Math.log2(sr / 2)), u16(sc * 2 - sr),
  ...segs.map(s => u16(s.end)), u16(0), ...segs.map(s => u16(s.start)),
  ...segs.map(s => u16(s.start === 0xffff ? 1 : (s.gid - s.start + 0x10000) & 0xffff)), ...segs.map(() => u16(0))]);
const cmap = cat([u16(0), u16(1), u16(3), u16(1), u32(12), f4]);

const names = { 1: 'GROBS Pixel', 2: 'Regular', 3: 'GROBS Pixel Regular 1.0', 4: 'GROBS Pixel Regular', 5: 'Version 1.0', 6: 'GROBSPixel-Regular' };
const strs = Object.entries(names).map(([id, t]) => ({ id: +id, b: Buffer.from(t, 'utf16le').swap16() }));
let off = 0; const recs = strs.map(s => { const r = cat([u16(3), u16(1), u16(0x409), u16(s.id), u16(s.b.length), u16(off)]); off += s.b.length; return r; });
const name = cat([u16(0), u16(strs.length), u16(6 + strs.length * 12), ...recs, ...strs.map(s => s.b)]);

const tables = { 'OS/2': os2, cmap, gasp, prep, glyf: cat(glyf), head, hhea, hmtx: cat(hmtx), loca: cat(loca.map(u32)), maxp, name, post };
const tags = Object.keys(tables).sort();
const sum = b => { const p = cat([b, B((4 - b.length % 4) % 4)]); let s = 0; for (let k = 0; k < p.length; k += 4) s = (s + p.readUInt32BE(k)) >>> 0; return s; };
const nt = tags.length, esr = 16 * 2 ** Math.floor(Math.log2(nt));
const dir = [u32(0x00010000), u16(nt), u16(esr), u16(Math.log2(esr / 16)), u16(nt * 16 - esr)];
let pos = 12 + nt * 16; const body = [];
for (const t of tags) {
  const b = tables[t];
  dir.push(Buffer.from(t.padEnd(4)), u32(sum(b)), u32(pos), u32(b.length));
  const p = cat([b, B((4 - b.length % 4) % 4)]); body.push(p); pos += p.length;
}
const font = cat([...dir, ...body]);
const adj = (0xB1B0AFBA - sum(font)) >>> 0;
font.writeUInt32BE(adj, 12 + nt * 16 + tags.slice(0, tags.indexOf('head')).reduce((a, t) => a + Math.ceil(tables[t].length / 4) * 4, 0) + 8);
const out = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'src', 'assets', 'grobs-pixel.ttf');
fs.writeFileSync(out, font);
console.log('ok', n, 'glifos,', font.length, 'bytes ->', out);
