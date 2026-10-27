import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { abrirArquivo, api, enviar } from '../api';
import { Estado, Selo, mensagemDoErro, useCarregar } from '../comum';
import { data, mes, moeda, nomeIndice, nomeRegra, percentual } from '../formato';
import type { ContratoDetalhe as Detalhe, Reajuste, Simulacao } from '../tipos';

function ItemLinhaDoTempo({ r }: { r: Reajuste }) {
  const [erro, setErro] = useState<string | null>(null);
  const apagado = r.status === 'Substituido';

  return (
    <li className={`marco${apagado ? ' marco-apagado' : ''}`}>
      <span className={`marco-ponto marco-${r.status}`} aria-hidden />
      <div className="marco-corpo">
        <div className="marco-topo">
          <strong>{data(r.dataAniversario)}</strong>
          <span className="secundario">ciclo {r.ciclo}{r.revisao > 0 ? ` · revisão ${r.revisao}` : ''}</span>
          <Selo status={r.status} />
          {r.retroativo && <span className="etiqueta">retroativo</span>}
        </div>
        {r.valorNovo === null ? (
          <p className="secundario">
            Aguardando o índice de {mes(r.janelaFim)} ser divulgado. Nada é calculado com dado incompleto.
          </p>
        ) : (
          <>
            <p className="marco-valores">
              {moeda(r.valorAntigo)} <span aria-hidden>→</span> <strong>{moeda(r.valorNovo)}</strong>
              <span className="secundario"> {percentual(r.percentualAplicado ?? 0, 4)}</span>
            </p>
            <p className="secundario">
              {r.regraAplicada && nomeRegra[r.regraAplicada]}
              {r.janelaInicio && ` · janela ${mes(r.janelaInicio)} a ${mes(r.janelaFim)}`}
              {r.percentualAcumulado !== null && r.percentualAcumulado !== r.percentualAplicado &&
                ` · acumulado bruto ${percentual(r.percentualAcumulado, 4)}`}
            </p>
            {!apagado && (
              <button
                className="botao botao-pequeno"
                onClick={() => abrirArquivo(`/reajustes/${r.id}/carta.pdf`).catch((e) => setErro(mensagemDoErro(e)))}
              >
                Carta de reajuste (PDF)
              </button>
            )}
            {erro && <p className="campo-erro">{erro}</p>}
          </>
        )}
      </div>
    </li>
  );
}

export function ContratoDetalhe() {
  const { id } = useParams();
  const detalhe = useCarregar(() => api<Detalhe>(`/contratos/${id}`), [id]);
  const simulacao = useCarregar(() => api<Simulacao>(`/contratos/${id}/simulacao`), [id]);
  const [erroAcao, setErroAcao] = useState<string | null>(null);

  async function encerrar() {
    if (!confirm('Encerrar o contrato? Avisos pendentes serão cancelados e nenhum reajuste novo será gerado.')) return;
    try {
      await enviar('POST', `/contratos/${id}/encerrar`);
      await detalhe.recarregar();
    } catch (e) {
      setErroAcao(mensagemDoErro(e));
    }
  }

  const c = detalhe.dados?.contrato;
  const proximo = simulacao.dados?.ciclos.at(-1);
  const linhaDoTempo = [...(detalhe.dados?.reajustes ?? [])].reverse();

  return (
    <Estado carregando={detalhe.carregando} erro={detalhe.erro}>
      {c && (
        <>
          <header className="cabecalho">
            <div>
              <Link to="/contratos" className="voltar">← Contratos</Link>
              <h1>{c.descricao}</h1>
              <p className="subtitulo">{c.clienteNome}</p>
            </div>
            {c.status === 'Ativo' && (
              <div className="acoes">
                <button className="botao" onClick={encerrar}>Encerrar</button>
                <Link to={`/contratos/${c.id}/editar`} className="botao botao-primario">Editar</Link>
              </div>
            )}
          </header>
          {erroAcao && <p className="alerta alerta-erro">{erroAcao}</p>}

          <section className="metricas">
            <div className="cartao-metrica">
              <span className="metrica-rotulo">Valor atual</span>
              <span className="metrica-valor">{moeda(c.valorAtual)}</span>
              <span className="metrica-detalhe">inicial {moeda(c.valorInicial)}</span>
            </div>
            <div className="cartao-metrica">
              <span className="metrica-rotulo">Próximo reajuste</span>
              <span className="metrica-valor">{c.status === 'Ativo' ? data(c.proximoAniversario) : 'Encerrado'}</span>
              {proximo && c.status === 'Ativo' && (
                <span className="metrica-detalhe">
                  {proximo.valorNovo !== null
                    ? `estimado ${moeda(proximo.valorNovo)}`
                    : `aguarda ${proximo.mesesFaltantes.length} mês(es) de índice`}
                </span>
              )}
            </div>
            <div className="cartao-metrica">
              <span className="metrica-rotulo">Índice</span>
              <span className="metrica-valor">
                {c.tipoIndice === 'PercentualFixo' ? `${c.percentualFixo}% fixo` : nomeIndice[c.tipoIndice]}
              </span>
              <span className="metrica-detalhe">
                a cada {c.periodicidadeMeses} meses{c.tipoIndice !== 'PercentualFixo' && ` · defasagem ${c.defasagemMeses}`}
              </span>
            </div>
            <div className="cartao-metrica">
              <span className="metrica-rotulo">Cláusulas</span>
              <span className="metrica-valor metrica-texto">
                {c.teto === null && c.piso === null && !c.naoReduzir && 'Sem limites'}
                {c.teto !== null && `Teto ${c.unidadeLimite === 'Valor' ? moeda(c.teto) : percentual(c.teto)} `}
                {c.piso !== null && `Piso ${c.unidadeLimite === 'Valor' ? moeda(c.piso) : percentual(c.piso)} `}
                {c.naoReduzir && 'Não reduz'}
              </span>
              <span className="metrica-detalhe">data-base {data(c.dataBase)}</span>
            </div>
          </section>

          <section className="bloco">
            <h2>Linha do tempo</h2>
            {linhaDoTempo.length === 0 ? (
              <p className="estado">
                Nenhum reajuste ainda. O ciclo de {data(c.proximoAniversario)} será calculado quando entrar na antecedência de aviso.
              </p>
            ) : (
              <ol className="linha-do-tempo">
                {linhaDoTempo.map((r) => <ItemLinhaDoTempo key={r.id} r={r} />)}
              </ol>
            )}
          </section>
        </>
      )}
    </Estado>
  );
}
