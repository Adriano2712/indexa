import { Link } from 'react-router-dom';
import { api } from '../api';
import { Estado, Selo, useCarregar } from '../comum';
import { data, dias, mes, moeda, nomeIndice, nomeRegra, percentual } from '../formato';
import type { Painel as DadosPainel } from '../tipos';

function Cartao({ rotulo, valor, detalhe, alerta }: { rotulo: string; valor: string; detalhe?: string; alerta?: boolean }) {
  return (
    <div className={`cartao-metrica${alerta ? ' cartao-alerta' : ''}`}>
      <span className="metrica-rotulo">{rotulo}</span>
      <span className="metrica-valor">{valor}</span>
      {detalhe && <span className="metrica-detalhe">{detalhe}</span>}
    </div>
  );
}

export function Painel() {
  const { dados, erro, carregando } = useCarregar(() => api<DadosPainel>('/painel'), []);

  return (
    <>
      <header className="cabecalho">
        <div>
          <h1>Painel</h1>
          {dados && (
            <p className="subtitulo">
              Índices disponíveis: IPCA até {mes(dados.ultimoIpca)} · IGP-M até {mes(dados.ultimoIgpM)}
            </p>
          )}
        </div>
        <Link to="/contratos/novo" className="botao botao-primario">Novo contrato</Link>
      </header>

      <Estado carregando={carregando} erro={erro}>
        {dados && (
          <>
            <section className="metricas">
              <Cartao rotulo="Contratos ativos" valor={String(dados.contratosAtivos)} />
              <Cartao rotulo="Valor mensal sob gestão" valor={moeda(dados.valorMensalAtivo)} />
              <Cartao rotulo="Reajustes em 30 dias" valor={String(dados.reajustesProximos30Dias)} />
              <Cartao
                rotulo="Aguardando índice"
                valor={String(dados.aguardandoIndice)}
                detalhe="Calculam assim que a fonte publicar"
              />
              <Cartao
                rotulo="Avisos com falha"
                valor={String(dados.avisosComFalha)}
                detalhe={dados.avisosComFalha ? 'Verifique o e-mail de aviso' : 'Nenhuma falha'}
                alerta={dados.avisosComFalha > 0}
              />
            </section>

            <section className="bloco">
              <h2>Próximos reajustes</h2>
              {dados.proximos.length === 0 ? (
                <p className="estado">Nenhum contrato ativo.</p>
              ) : (
                <div className="tabela-rolagem">
                  <table className="tabela">
                    <thead>
                      <tr>
                        <th>Contrato</th>
                        <th>Data</th>
                        <th>Índice</th>
                        <th className="num">Valor atual</th>
                        <th className="num">Novo valor</th>
                        <th>Situação</th>
                      </tr>
                    </thead>
                    <tbody>
                      {dados.proximos.map((p) => (
                        <tr key={p.contratoId}>
                          <td>
                            <Link to={`/contratos/${p.contratoId}`} className="link-forte">{p.descricao}</Link>
                            <span className="secundario">{p.cliente}</span>
                          </td>
                          <td>
                            {data(p.aniversario)}
                            <span className="secundario">{dias(p.diasRestantes)}</span>
                          </td>
                          <td>{nomeIndice[p.tipoIndice]}</td>
                          <td className="num">{moeda(p.valorAtual)}</td>
                          <td className="num">{p.valorNovo === null ? '—' : moeda(p.valorNovo)}</td>
                          <td>{p.status ? <Selo status={p.status} /> : <span className="secundario">Fora da antecedência</span>}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="bloco">
              <h2>Histórico recente</h2>
              {dados.historico.length === 0 ? (
                <p className="estado">Nenhum reajuste aplicado ainda.</p>
              ) : (
                <div className="tabela-rolagem">
                  <table className="tabela">
                    <thead>
                      <tr>
                        <th>Contrato</th>
                        <th>Aniversário</th>
                        <th className="num">De</th>
                        <th className="num">Para</th>
                        <th className="num">Aplicado</th>
                        <th>Regra</th>
                      </tr>
                    </thead>
                    <tbody>
                      {dados.historico.map((h) => (
                        <tr key={h.reajusteId}>
                          <td>
                            <Link to={`/contratos/${h.contratoId}`} className="link-forte">{h.descricao}</Link>
                            <span className="secundario">ciclo {h.ciclo}{h.retroativo ? ' · retroativo' : ''}</span>
                          </td>
                          <td>{data(h.aniversario)}</td>
                          <td className="num">{moeda(h.valorAntigo)}</td>
                          <td className="num">{moeda(h.valorNovo)}</td>
                          <td className="num">{percentual(h.percentualAplicado)}</td>
                          <td>{nomeRegra[h.regra]}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          </>
        )}
      </Estado>
    </>
  );
}
