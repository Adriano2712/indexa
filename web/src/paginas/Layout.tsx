import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { encerrarSessao, sessaoAtual } from '../api';

const ITENS = [
  { para: '/', rotulo: 'Painel', fim: true },
  { para: '/contratos', rotulo: 'Contratos', fim: false },
  { para: '/configuracoes', rotulo: 'Configurações', fim: false },
];

export function Layout() {
  const navegar = useNavigate();
  const email = sessaoAtual()?.email;

  return (
    <div className="app">
      <aside className="lateral">
        <div className="marca">
          <span className="marca-icone" aria-hidden>
            <svg viewBox="0 0 32 32"><path d="M9 21l5-6 4 3 5-7" /></svg>
          </span>
          Indexa
        </div>
        <nav className="menu">
          {ITENS.map((i) => (
            <NavLink key={i.para} to={i.para} end={i.fim} className={({ isActive }) => `menu-item${isActive ? ' ativo' : ''}`}>
              {i.rotulo}
            </NavLink>
          ))}
        </nav>
        <div className="lateral-rodape">
          <span className="usuario" title={email}>{email}</span>
          <button
            className="botao botao-fantasma"
            onClick={() => {
              encerrarSessao();
              navegar('/login');
            }}
          >
            Sair
          </button>
        </div>
      </aside>
      <main className="conteudo">
        <Outlet />
      </main>
    </div>
  );
}
