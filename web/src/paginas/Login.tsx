import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { enviar, salvarSessao } from '../api';
import { Campo, camposDoErro, mensagemDoErro } from '../comum';

export function Login() {
  const navegar = useNavigate();
  const [modo, setModo] = useState<'entrar' | 'cadastrar'>('entrar');
  const [nome, setNome] = useState('');
  const [email, setEmail] = useState('');
  const [senha, setSenha] = useState('');
  const [erro, setErro] = useState<string | null>(null);
  const [campos, setCampos] = useState<Record<string, string[]>>({});
  const [enviando, setEnviando] = useState(false);

  async function submeter(e: FormEvent) {
    e.preventDefault();
    setEnviando(true);
    setErro(null);
    setCampos({});
    try {
      if (modo === 'cadastrar') await enviar('POST', '/auth/registrar', { nome, email, senha });
      const r = await enviar<{ token: string; expiraEm: string }>('POST', '/auth/login', { email, senha });
      salvarSessao({ ...r, email: email.trim().toLowerCase() });
      navegar('/');
    } catch (ex) {
      setErro(mensagemDoErro(ex));
      setCampos(camposDoErro(ex));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="login">
      <form className="login-cartao" onSubmit={submeter}>
        <div className="marca marca-grande">
          <span className="marca-icone" aria-hidden>
            <svg viewBox="0 0 32 32"><path d="M9 21l5-6 4 3 5-7" /></svg>
          </span>
          Indexa
        </div>
        <p className="login-subtitulo">Reajuste de contratos pelo índice certo, na data certa.</p>

        {modo === 'cadastrar' && (
          <Campo rotulo="Nome" erros={campos.nome}>
            <input value={nome} onChange={(e) => setNome(e.target.value)} autoComplete="name" required />
          </Campo>
        )}
        <Campo rotulo="E-mail" erros={campos.email}>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required />
        </Campo>
        <Campo rotulo="Senha" erros={campos.senha} ajuda={modo === 'cadastrar' ? 'Mínimo de 10 caracteres.' : undefined}>
          <input
            type="password"
            value={senha}
            onChange={(e) => setSenha(e.target.value)}
            autoComplete={modo === 'entrar' ? 'current-password' : 'new-password'}
            required
          />
        </Campo>

        {erro && Object.keys(campos).length === 0 && <p className="alerta alerta-erro" role="alert">{erro}</p>}

        <button className="botao botao-primario botao-largo" disabled={enviando}>
          {enviando ? 'Aguarde…' : modo === 'entrar' ? 'Entrar' : 'Criar conta'}
        </button>
        <button
          type="button"
          className="botao botao-fantasma botao-largo"
          onClick={() => {
            setModo(modo === 'entrar' ? 'cadastrar' : 'entrar');
            setErro(null);
            setCampos({});
          }}
        >
          {modo === 'entrar' ? 'Criar uma conta (beta)' : 'Já tenho conta'}
        </button>
      </form>
    </div>
  );
}
