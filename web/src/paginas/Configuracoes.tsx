import { useEffect, useState, type FormEvent } from 'react';
import { api, enviar } from '../api';
import { Campo, Estado, camposDoErro, mensagemDoErro, useCarregar } from '../comum';
import type { Configuracoes as Dados } from '../tipos';

export function Configuracoes() {
  const { dados, erro, carregando } = useCarregar(() => api<Dados>('/configuracoes'), []);
  const [antecedencia, setAntecedencia] = useState('15');
  const [emailAviso, setEmailAviso] = useState('');
  const [ativo, setAtivo] = useState(true);
  const [destinatario, setDestinatario] = useState('');
  const [campos, setCampos] = useState<Record<string, string[]>>({});
  const [mensagem, setMensagem] = useState<{ tipo: 'ok' | 'erro'; texto: string } | null>(null);

  useEffect(() => {
    if (!dados) return;
    setAntecedencia(String(dados.antecedenciaAvisoDias));
    setEmailAviso(dados.emailAviso ?? '');
    setAtivo(dados.avisoEmailAtivo);
    setDestinatario(dados.destinatarioEfetivo);
  }, [dados]);

  async function salvar(e: FormEvent) {
    e.preventDefault();
    setCampos({});
    setMensagem(null);
    try {
      const salvo = await enviar<Dados>('PUT', '/configuracoes', {
        antecedenciaAvisoDias: Number(antecedencia),
        emailAviso: emailAviso.trim() || null,
        avisoEmailAtivo: ativo,
      });
      setDestinatario(salvo.destinatarioEfetivo);
      setMensagem({ tipo: 'ok', texto: 'Configurações salvas. Valem a partir do próximo processamento diário.' });
    } catch (ex) {
      setCampos(camposDoErro(ex));
      setMensagem({ tipo: 'erro', texto: mensagemDoErro(ex) });
    }
  }

  return (
    <>
      <header className="cabecalho">
        <div>
          <h1>Configurações</h1>
          <p className="subtitulo">Quando e para onde vão os avisos de reajuste.</p>
        </div>
      </header>

      <Estado carregando={carregando} erro={erro}>
        <form className="formulario" onSubmit={salvar} noValidate>
          {mensagem && <p className={`alerta alerta-${mensagem.tipo}`} role="status">{mensagem.texto}</p>}

          <fieldset className="grupo">
            <legend>Canais de aviso</legend>
            <label className="opcao">
              <input type="checkbox" checked={ativo} onChange={(e) => setAtivo(e.target.checked)} />
              <span>
                <strong>E-mail</strong>
                <span className="campo-ajuda">Envia o novo valor e a carta de reajuste em PDF. Hoje vai para {destinatario}.</span>
              </span>
            </label>
            <label className="opcao opcao-desativada">
              <input type="checkbox" disabled />
              <span>
                <strong>Telegram</strong>
                <span className="campo-ajuda">Fora do MVP.</span>
              </span>
            </label>
          </fieldset>

          <fieldset className="grupo">
            <legend>Antecedência e destinatário</legend>
            <div className="grade">
              <Campo rotulo="Avisar com quantos dias de antecedência" erros={campos.antecedenciaAvisoDias} ajuda="Entre 1 e 90 dias.">
                <input inputMode="numeric" value={antecedencia} onChange={(e) => setAntecedencia(e.target.value)} />
              </Campo>
              <Campo rotulo="E-mail para avisos" erros={campos.emailAviso} ajuda="Em branco: usa o e-mail de login.">
                <input type="email" value={emailAviso} onChange={(e) => setEmailAviso(e.target.value)} placeholder={dados?.destinatarioEfetivo} />
              </Campo>
            </div>
            <p className="campo-ajuda">
              Se o índice do período ainda não saiu na data do aviso, o reajuste fica aguardando e o aviso sai assim que ele for publicado.
            </p>
          </fieldset>

          <div className="acoes">
            <button className="botao botao-primario">Salvar</button>
          </div>
        </form>
      </Estado>
    </>
  );
}
