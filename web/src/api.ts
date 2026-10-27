const CHAVE_SESSAO = 'indexa.sessao';

interface Sessao {
  token: string;
  expiraEm: string;
  email: string;
}

export class ErroApi extends Error {
  constructor(
    public readonly status: number,
    mensagem: string,
    public readonly campos: Record<string, string[]> = {},
  ) {
    super(mensagem);
  }
}

export function sessaoAtual(): Sessao | null {
  try {
    const bruto = localStorage.getItem(CHAVE_SESSAO);
    if (!bruto) return null;
    const sessao = JSON.parse(bruto) as Sessao;
    return new Date(sessao.expiraEm) > new Date() ? sessao : null;
  } catch {
    return null;
  }
}

export function salvarSessao(sessao: Sessao) {
  localStorage.setItem(CHAVE_SESSAO, JSON.stringify(sessao));
}

export function encerrarSessao() {
  localStorage.removeItem(CHAVE_SESSAO);
}

async function requisitar(caminho: string, opcoes: RequestInit = {}): Promise<Response> {
  const sessao = sessaoAtual();
  const resposta = await fetch(`/api${caminho}`, {
    ...opcoes,
    headers: {
      ...(opcoes.body ? { 'Content-Type': 'application/json' } : {}),
      ...(sessao ? { Authorization: `Bearer ${sessao.token}` } : {}),
      ...opcoes.headers,
    },
  });

  if (resposta.status === 401 && !caminho.startsWith('/auth/')) {
    encerrarSessao();
    window.location.assign('/login');
  }
  if (!resposta.ok) {
    const corpo = await resposta.json().catch(() => ({}));
    throw new ErroApi(resposta.status, corpo.detail ?? corpo.title ?? `Erro ${resposta.status}`, corpo.errors ?? {});
  }
  return resposta;
}

export async function api<T>(caminho: string, opcoes: RequestInit = {}): Promise<T> {
  const resposta = await requisitar(caminho, opcoes);
  return (resposta.status === 204 ? undefined : await resposta.json()) as T;
}

export const enviar = <T>(metodo: 'POST' | 'PUT', caminho: string, corpo?: unknown) =>
  api<T>(caminho, { method: metodo, body: corpo === undefined ? undefined : JSON.stringify(corpo) });

/** PDF protegido por token: baixa como blob e abre numa aba. */
export async function abrirArquivo(caminho: string) {
  const resposta = await requisitar(caminho);
  const url = URL.createObjectURL(await resposta.blob());
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
