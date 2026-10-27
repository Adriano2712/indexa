// Sessão em cookie HttpOnly definido pela API no login: o JavaScript da página nunca vê o token.

export interface Usuario {
  id: string;
  nome: string;
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

async function requisitar(caminho: string, opcoes: RequestInit = {}): Promise<Response> {
  const resposta = await fetch(`/api${caminho}`, {
    ...opcoes,
    credentials: 'same-origin',
    headers: {
      ...(opcoes.body ? { 'Content-Type': 'application/json' } : {}),
      ...opcoes.headers,
    },
  });

  if (resposta.status === 401 && !caminho.startsWith('/auth/')) {
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

export async function usuarioAtual(): Promise<Usuario | null> {
  try {
    return await api<Usuario>('/auth/eu');
  } catch {
    return null;
  }
}

export const sair = () => enviar('POST', '/auth/sair');

/** PDF protegido: baixa como blob (cookie vai junto) e abre numa aba. */
export async function abrirArquivo(caminho: string) {
  const resposta = await requisitar(caminho);
  const url = URL.createObjectURL(await resposta.blob());
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
