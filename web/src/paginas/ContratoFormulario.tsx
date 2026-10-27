import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, enviar } from '../api';
import { Campo, camposDoErro, mensagemDoErro, useCarregar } from '../comum';
import type { Cliente, Contrato, ContratoDetalhe, Pagina, TipoIndice, UnidadeLimite } from '../tipos';

interface Formulario {
  clienteId: string;
  descricao: string;
  valorInicial: string;
  dataBase: string;
  tipoIndice: TipoIndice;
  percentualFixo: string;
  periodicidadeMeses: string;
  defasagemMeses: string;
  teto: string;
  piso: string;
  unidadeLimite: UnidadeLimite;
  naoReduzir: boolean;
}

const VAZIO: Formulario = {
  clienteId: '',
  descricao: '',
  valorInicial: '',
  dataBase: '',
  tipoIndice: 'Ipca',
  percentualFixo: '',
  periodicidadeMeses: '12',
  defasagemMeses: '1',
  teto: '',
  piso: '',
  unidadeLimite: 'Percentual',
  naoReduzir: false,
};

// Aceita "2.500,00" (pt-BR) e "2500.00". Com vírgula, pontos são separador de milhar.
function numero(v: string) {
  const limpo = v.trim();
  if (limpo === '') return null;
  return Number(limpo.includes(',') ? limpo.replace(/\./g, '').replace(',', '.') : limpo);
}
const texto = (v: number | null) => (v === null ? '' : String(v));

function doContrato(c: Contrato): Formulario {
  return {
    clienteId: c.clienteId,
    descricao: c.descricao,
    valorInicial: String(c.valorInicial),
    dataBase: c.dataBase,
    tipoIndice: c.tipoIndice,
    percentualFixo: texto(c.percentualFixo),
    periodicidadeMeses: String(c.periodicidadeMeses),
    defasagemMeses: String(c.defasagemMeses),
    teto: texto(c.teto),
    piso: texto(c.piso),
    unidadeLimite: c.unidadeLimite ?? 'Percentual',
    naoReduzir: c.naoReduzir,
  };
}

export function ContratoFormulario() {
  const { id } = useParams();
  const navegar = useNavigate();
  const edicao = Boolean(id);
  const [f, setF] = useState<Formulario>(VAZIO);
  const [possuiHistorico, setPossuiHistorico] = useState(false);
  const [novoCliente, setNovoCliente] = useState<{ nome: string; documento: string; email: string } | null>(null);
  const [campos, setCampos] = useState<Record<string, string[]>>({});
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const clientes = useCarregar(() => api<Pagina<Cliente>>('/clientes?tamanhoPagina=100'), []);

  useEffect(() => {
    if (!id) return;
    api<ContratoDetalhe>(`/contratos/${id}`).then((d) => {
      setF(doContrato(d.contrato));
      setPossuiHistorico(d.reajustes.some((r) => r.status !== 'Substituido'));
    }, (e) => setErro(mensagemDoErro(e)));
  }, [id]);

  useEffect(() => {
    if (!edicao && clientes.dados?.itens.length === 0) setNovoCliente({ nome: '', documento: '', email: '' });
  }, [edicao, clientes.dados]);

  const muda = <K extends keyof Formulario>(chave: K, valor: Formulario[K]) => setF((atual) => ({ ...atual, [chave]: valor }));
  const temLimite = f.teto.trim() !== '' || f.piso.trim() !== '';

  async function submeter(e: FormEvent) {
    e.preventDefault();
    setEnviando(true);
    setErro(null);
    setCampos({});
    try {
      let clienteId = f.clienteId;
      if (novoCliente) {
        const criado = await enviar<Cliente>('POST', '/clientes', {
          nome: novoCliente.nome,
          documento: novoCliente.documento || null,
          email: novoCliente.email || null,
        });
        clienteId = criado.id;
        setNovoCliente(null);
        muda('clienteId', clienteId);
        await clientes.recarregar();
      }

      const corpo = {
        clienteId,
        descricao: f.descricao,
        valorInicial: numero(f.valorInicial) ?? 0,
        dataBase: f.dataBase,
        tipoIndice: f.tipoIndice,
        percentualFixo: f.tipoIndice === 'PercentualFixo' ? numero(f.percentualFixo) : null,
        periodicidadeMeses: numero(f.periodicidadeMeses),
        defasagemMeses: numero(f.defasagemMeses),
        teto: numero(f.teto),
        piso: numero(f.piso),
        unidadeLimite: temLimite ? f.unidadeLimite : null,
        naoReduzir: f.naoReduzir,
      };
      const salvo = edicao
        ? await enviar<Contrato>('PUT', `/contratos/${id}`, corpo)
        : await enviar<Contrato>('POST', '/contratos', corpo);
      navegar(`/contratos/${salvo.id}`);
    } catch (ex) {
      setErro(mensagemDoErro(ex));
      setCampos(camposDoErro(ex));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <>
      <header className="cabecalho">
        <div>
          <h1>{edicao ? 'Editar contrato' : 'Novo contrato'}</h1>
          <p className="subtitulo">
            {edicao
              ? 'Mudanças de cláusula valem para os próximos ciclos; o histórico guarda a cláusula usada em cada reajuste.'
              : 'Contratos com data-base no passado têm os reajustes vencidos calculados como retroativos, sem aviso.'}
          </p>
        </div>
      </header>

      <form className="formulario" onSubmit={submeter} noValidate>
        {erro && <p className="alerta alerta-erro" role="alert">{Object.keys(campos).length ? 'Revise os campos destacados.' : erro}</p>}

        <fieldset className="grupo">
          <legend>Cliente e contrato</legend>
          {novoCliente ? (
            <div className="grade">
              <Campo rotulo="Nome do cliente" erros={campos.nome}>
                <input value={novoCliente.nome} onChange={(e) => setNovoCliente({ ...novoCliente, nome: e.target.value })} required />
              </Campo>
              <Campo rotulo="CPF ou CNPJ (opcional)" erros={campos.documento} ajuda="CNPJ alfanumérico aceito.">
                <input value={novoCliente.documento} onChange={(e) => setNovoCliente({ ...novoCliente, documento: e.target.value })} />
              </Campo>
              <Campo rotulo="E-mail do cliente (opcional)" erros={campos.email}>
                <input type="email" value={novoCliente.email} onChange={(e) => setNovoCliente({ ...novoCliente, email: e.target.value })} />
              </Campo>
              {(clientes.dados?.itens.length ?? 0) > 0 && (
                <button type="button" className="botao botao-fantasma alinhado" onClick={() => setNovoCliente(null)}>Escolher cliente existente</button>
              )}
            </div>
          ) : (
            <div className="grade">
              <Campo rotulo="Cliente" erros={campos.clienteId}>
                <select value={f.clienteId} onChange={(e) => muda('clienteId', e.target.value)} required>
                  <option value="">Selecione…</option>
                  {clientes.dados?.itens.map((c) => (
                    <option key={c.id} value={c.id}>{c.nome}</option>
                  ))}
                </select>
              </Campo>
              <button type="button" className="botao botao-fantasma alinhado" onClick={() => setNovoCliente({ nome: '', documento: '', email: '' })}>
                + Novo cliente
              </button>
            </div>
          )}
          <div className="grade">
            <Campo rotulo="Descrição" erros={campos.descricao} ajuda="Ex.: Aluguel sala 101, manutenção de elevadores.">
              <input value={f.descricao} onChange={(e) => muda('descricao', e.target.value)} required />
            </Campo>
            <Campo rotulo="Valor inicial (R$)" erros={campos.valorInicial}>
              <input inputMode="decimal" value={f.valorInicial} onChange={(e) => muda('valorInicial', e.target.value)} placeholder="2500,00" required />
            </Campo>
            <Campo
              rotulo="Data-base"
              erros={campos.dataBase}
              ajuda={possuiHistorico ? 'Só pode mudar enquanto nenhum aviso foi enviado.' : 'Início da vigência; os aniversários contam a partir dela.'}
            >
              <input type="date" value={f.dataBase} onChange={(e) => muda('dataBase', e.target.value)} required />
            </Campo>
          </div>
        </fieldset>

        <fieldset className="grupo">
          <legend>Índice e periodicidade</legend>
          <div className="grade">
            <Campo rotulo="Índice" erros={campos.tipoIndice}>
              <select value={f.tipoIndice} onChange={(e) => muda('tipoIndice', e.target.value as TipoIndice)}>
                <option value="Ipca">IPCA (IBGE)</option>
                <option value="IgpM">IGP-M (FGV)</option>
                <option value="PercentualFixo">Percentual fixo</option>
              </select>
            </Campo>
            {f.tipoIndice === 'PercentualFixo' && (
              <Campo rotulo="Percentual fixo (%)" erros={campos.percentualFixo}>
                <input inputMode="decimal" value={f.percentualFixo} onChange={(e) => muda('percentualFixo', e.target.value)} placeholder="5" />
              </Campo>
            )}
            <Campo rotulo="Periodicidade (meses)" erros={campos.periodicidadeMeses}>
              <input inputMode="numeric" value={f.periodicidadeMeses} onChange={(e) => muda('periodicidadeMeses', e.target.value)} />
            </Campo>
            {f.tipoIndice !== 'PercentualFixo' && (
              <Campo
                rotulo="Defasagem (meses)"
                erros={campos.defasagemMeses}
                ajuda="1 = aniversário em novembro usa o acumulado até setembro."
              >
                <input inputMode="numeric" value={f.defasagemMeses} onChange={(e) => muda('defasagemMeses', e.target.value)} />
              </Campo>
            )}
          </div>
        </fieldset>

        <fieldset className="grupo">
          <legend>Cláusulas de limite</legend>
          <div className="grade">
            <Campo rotulo="Teto" erros={campos.teto} ajuda="Deixe em branco se o contrato não prevê.">
              <input inputMode="decimal" value={f.teto} onChange={(e) => muda('teto', e.target.value)} />
            </Campo>
            <Campo rotulo="Piso" erros={campos.piso} ajuda="Pode ser negativo (limita a redução).">
              <input inputMode="decimal" value={f.piso} onChange={(e) => muda('piso', e.target.value)} />
            </Campo>
            <Campo rotulo="Teto e piso em" erros={campos.unidadeLimite}>
              <select value={f.unidadeLimite} onChange={(e) => muda('unidadeLimite', e.target.value as UnidadeLimite)} disabled={!temLimite}>
                <option value="Percentual">% sobre o acumulado</option>
                <option value="Valor">R$ de aumento</option>
              </select>
            </Campo>
          </div>
          <label className="opcao">
            <input type="checkbox" checked={f.naoReduzir} onChange={(e) => muda('naoReduzir', e.target.checked)} />
            <span>
              <strong>Não reduzir o valor</strong>
              <span className="campo-ajuda">Com índice negativo no período, mantém o valor atual. Confirme no texto do contrato.</span>
            </span>
          </label>
        </fieldset>

        <div className="acoes">
          <Link to={edicao ? `/contratos/${id}` : '/contratos'} className="botao">Cancelar</Link>
          <button className="botao botao-primario" disabled={enviando}>{enviando ? 'Salvando…' : 'Salvar contrato'}</button>
        </div>
      </form>
    </>
  );
}
