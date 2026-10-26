import { Component, ReactNode } from 'react';
import { Icon } from './Icon';

/** Uma falha numa página não derruba o app inteiro. */
export default class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: Error) { return { error }; }
  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="page"><div className="card" style={{ textAlign: 'center', padding: 36 }}>
        <Icon name="alert" size={34} className="warn-t" />
        <h3 style={{ margin: '10px 0 6px' }}>Algo deu errado nesta tela</h3>
        <p className="muted small selectable">{this.state.error.message}</p>
        <button className="btn" onClick={() => this.setState({ error: null })}><Icon name="refresh" size={14} />Tentar de novo</button>
      </div></div>
    );
  }
}
