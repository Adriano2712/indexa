import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { ErroApi } from './api';
import { status as statusInfo } from './formato';
import type { StatusReajuste } from './tipos';

export function useCarregar<T>(carregar: () => Promise<T>, dependencias: unknown[]) {
  const [dados, setDados] = useState<T | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);

  // O chamador declara as dependências, como em useEffect.
  const executar = useCallback(carregar, dependencias);

  const recarregar = useCallback(async () => {
    setCarregando(true);
    setErro(null);
    try {
      setDados(await executar());
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Erro inesperado.');
    } finally {
      setCarregando(false);
    }
  }, [executar]);

  useEffect(() => {
    void recarregar();
  }, [recarregar]);

  return { dados, erro, carregando, recarregar };
}

export function camposDoErro(e: unknown): Record<string, string[]> {
  return e instanceof ErroApi ? e.campos : {};
}

export function mensagemDoErro(e: unknown) {
  return e instanceof Error ? e.message : 'Erro inesperado.';
}

export function Selo({ status }: { status: StatusReajuste }) {
  const info = statusInfo[status];
  return <span className={`selo selo-${info.tom}`}>{info.rotulo}</span>;
}

export function Estado({ carregando, erro, vazio, children }: { carregando: boolean; erro: string | null; vazio?: boolean; children: ReactNode }) {
  if (carregando) return <p className="estado">Carregando…</p>;
  if (erro) return <p className="estado estado-erro" role="alert">{erro}</p>;
  if (vazio) return <p className="estado">Nada por aqui ainda.</p>;
  return <>{children}</>;
}

export function Campo({ rotulo, erros, ajuda, children }: { rotulo: string; erros?: string[]; ajuda?: string; children: ReactNode }) {
  return (
    <label className={`campo${erros?.length ? ' campo-invalido' : ''}`}>
      <span className="campo-rotulo">{rotulo}</span>
      {children}
      {ajuda && !erros?.length && <span className="campo-ajuda">{ajuda}</span>}
      {erros?.map((m) => (
        <span key={m} className="campo-erro">{m}</span>
      ))}
    </label>
  );
}
