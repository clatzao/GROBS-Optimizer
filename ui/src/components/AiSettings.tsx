import { useState } from 'react';
import { call } from '../bridge';
import { Icon } from './Icon';
import { Modal, Toggle } from './ui';
import { run } from '../store';

export type Prov = { id: string; title: string; needsKey: boolean; local: boolean; keyUrl?: string; model: string; hasKey: boolean };
export type AiCfg = { provider: string; consent: boolean; model: string; providers: Prov[] };

/** Escolha do provedor de IA grátis e autorização de envio. Usado na Análise inteligente e na Limpeza com IA. */
export default function AiSettings({ open, onClose, cfg, setCfg }: { open: boolean; onClose: () => void; cfg: AiCfg | null; setCfg: (c: AiCfg) => void }) {
  const [key, setKey] = useState('');
  const provider = cfg?.providers.find(p => p.id === cfg.provider);
  async function save(next: Partial<AiCfg>) {
    if (!cfg) return;
    const merged = { ...cfg, ...next };
    const r = await run(call<AiCfg>('ai.set', { provider: merged.provider, model: merged.model, key, consent: merged.consent }));
    if (r) { setCfg(r); setKey(''); }
  }
  return (
    <Modal open={open} onClose={onClose}>
      <h3>IA grátis</h3>
      {cfg && <>
        <div className="list" style={{ border: '1px solid var(--line)', borderRadius: 12, marginBottom: 12 }}>
          {cfg.providers.map(p => (
            <label key={p.id} className="li" style={{ padding: '9px 12px', cursor: 'pointer' }}>
              <input type="radio" name="prov" checked={cfg.provider === p.id} onChange={() => save({ provider: p.id, model: '' })} style={{ accentColor: '#8b5cf6' }} />
              <div className="grow"><div className="t" style={{ fontSize: 13 }}>{p.title}</div><div className="s">{p.local ? 'Nada sai do PC, mas precisa do Ollama e de um modelo instalados; é lento sem placa de vídeo.' : p.needsKey ? (p.hasKey ? 'Chave salva, cifrada com a sua conta do Windows.' : 'Precisa de uma chave grátis do site deles.') : 'Funciona na hora. Sem garantia: pode ficar indisponível ou limitar o uso.'}</div></div>
            </label>
          ))}
        </div>
        {provider?.needsKey && (
          <div className="row" style={{ marginBottom: 10 }}>
            <input type="text" placeholder={provider.hasKey ? 'Chave salva (cole outra para trocar)' : 'Cole a chave aqui'} value={key} onChange={e => setKey(e.target.value)} style={{ flex: 1 }} />
            <button className="btn sm" onClick={() => save({})} disabled={!key.trim()}>Salvar</button>
            {provider.keyUrl && <a className="btn sm ghost" href={provider.keyUrl} target="_blank" rel="noreferrer"><Icon name="external" size={13} />Pegar chave</a>}
          </div>
        )}
        {provider && !provider.local && (
          <label className="row small" style={{ gap: 10, marginBottom: 12, alignItems: 'flex-start' }}>
            <Toggle on={cfg.consent} onChange={v => save({ consent: v })} />
            <span>Autorizo enviar a este serviço um resumo: modelo do hardware, medições, nomes dos programas abertos e de inicialização e, na limpeza, o tipo e o tamanho dos itens. <b>Não são enviados</b> arquivos, senhas, caminhos, nomes de arquivos ou projetos, nem o nome de usuário. O serviço é de terceiros e tem a própria política de privacidade.</span>
          </label>
        )}
        <div className="row"><span className="spacer" /><button className="btn primary" onClick={onClose}>Pronto</button></div>
      </>}
    </Modal>
  );
}
