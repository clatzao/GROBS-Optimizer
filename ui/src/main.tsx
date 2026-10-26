import { createRoot } from 'react-dom/client';
import './styles.css';
import App from './App';
import { appStore, loadApp } from './store';

// Os componentes do React Bits respeitam "prefers-reduced-motion"; a preferência
// de animação do app é aplicada a eles respondendo a essa consulta.
const nativeMatch = window.matchMedia.bind(window);
window.matchMedia = (q: string) => {
  if (q.includes('prefers-reduced-motion')) {
    const reduce = (appStore.get()?.settings.animations ?? 'full') !== 'full';
    const real = nativeMatch(q);
    return { ...real, matches: reduce || real.matches, media: q, addEventListener: real.addEventListener.bind(real), removeEventListener: real.removeEventListener.bind(real) } as MediaQueryList;
  }
  return nativeMatch(q);
};

loadApp().finally(() => createRoot(document.getElementById('root')!).render(<App />));
