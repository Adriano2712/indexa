import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { Estado, useCarregar } from '../comum';
import { data, moeda, nomeIndice } from '../formato';
import type { Contrato, Pagina } from '../tipos';

const TAMANHO = 15;

export function Contratos() {
  const [parametros, setParametros] = useSearchParams();
  const busca = parametros.get('busca') ?? '';
  const tipoIndice = parametros.get('tipoIndice') ?? '';
  const status = parametros.get('status') ?? 'Ativo';
  const pagina = Number(parametros.get('pagina') ?? '1');
  const [termo, setTermo] = useState(busca);

  function alterar(chave: string, valor: string) {
    const novos = new URLSearchParams(parametros);
    if (valor) novos.set(chave, valor);
    else novos.delete(chave);
    if (chave !== 'pagina') novos.delete('pagina');
    setParametros(novos, { replace: true });
  }

  // Busca com atraso curto para não disparar uma requisição por tecla.
  useEffect(() => {
    const t = setTimeout(() => termo !== busca && alterar('busca', termo.trim()), 300);
    return () => clearTimeout(t);
  }, [termo]);

  const consulta = new URLSearchParams({ pagina: String(pagina), tamanhoPagina: String(TAMANHO) });
  if (busca) consulta.set('busca', busca);
  if (tipoIndice) consulta.set('tipoIndice', tipoIndice);
  if (status) consulta.set('status', status);
  const { dados, erro, carregando } = useCarregar(() => api<Pagina<Contrato>>(`/contratos?${consulta}`), [consulta.toString()]);
  const paginas = dados ? Math.max(1, Math.ceil(dados.total / TAMANHO)) : 1;

  return (
    <>
      <header className="cabecalho">
        <div>
          <h1>Contratos</h1>
          {dados && <p className="subtitulo">{dados.total} contrato{dados.total === 1 ? '' : 's'}</p>}
        </div>
        <Link to="/contratos/novo" className="botao botao-primario">Novo contrato</Link>
      </header>

      <div className="filtros">
        <input
          type="search"
          className="filtro-busca"
          placeholder="Buscar por descrição ou cliente"
          value={termo}
          onChange={(e) => setTermo(e.target.value)}
          aria-label="Buscar contratos"
        />
        <select value={tipoIndice} onChange={(e) => alterar('tipoIndice', e.target.value)} aria-label="Índice">
          <option value="">Todos os índices</option>
          <option value="Ipca">IPCA</option>
          <option value="IgpM">IGP-M</option>
          <option value="PercentualFixo">Percentual fixo</option>
        </select>
        <select value={status} onChange={(e) => alterar('status', e.target.value)} aria-label="Situação">
          <option value="Ativo">Ativos</option>
          <option value="Encerrado">Encerrados</option>
          <option value="">Todos</option>
        </select>
      </div>

      <Estado carregando={carregando} erro={erro} vazio={dados?.itens.length === 0}>
        <div className="bloco">
          <div className="tabela-rolagem">
            <table className="tabela">
              <thead>
                <tr>
                  <th>Contrato</th>
                  <th>Índice</th>
                  <th className="num">Valor atual</th>
                  <th>Próximo reajuste</th>
                  <th>Cláusulas</th>
                </tr>
              </thead>
              <tbody>
                {dados?.itens.map((c) => (
                  <tr key={c.id}>
                    <td>
                      <Link to={`/contratos/${c.id}`} className="link-forte">{c.descricao}</Link>
                      <span className="secundario">{c.clienteNome}</span>
                    </td>
                    <td>{c.tipoIndice === 'PercentualFixo' ? `Fixo ${c.percentualFixo}%` : nomeIndice[c.tipoIndice]}</td>
                    <td className="num">{moeda(c.valorAtual)}</td>
                    <td>{c.status === 'Encerrado' ? <span className="secundario">Encerrado</span> : data(c.proximoAniversario)}</td>
                    <td className="clausulas">
                      {c.teto !== null && <span className="etiqueta">teto {c.teto}{c.unidadeLimite === 'Valor' ? ' R$' : '%'}</span>}
                      {c.piso !== null && <span className="etiqueta">piso {c.piso}{c.unidadeLimite === 'Valor' ? ' R$' : '%'}</span>}
                      {c.naoReduzir && <span className="etiqueta">não reduz</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {paginas > 1 && (
            <div className="paginacao">
              <button className="botao" disabled={pagina <= 1} onClick={() => alterar('pagina', String(pagina - 1))}>Anterior</button>
              <span>Página {pagina} de {paginas}</span>
              <button className="botao" disabled={pagina >= paginas} onClick={() => alterar('pagina', String(pagina + 1))}>Próxima</button>
            </div>
          )}
        </div>
      </Estado>
    </>
  );
}
