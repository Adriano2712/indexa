import type { RegraAplicada, StatusReajuste, TipoIndice } from './tipos';

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const MESES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

export const moeda = (valor: number) => brl.format(valor);

export const percentual = (valor: number, casas = 2) =>
  `${valor.toLocaleString('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas })}%`;

// Datas da API são "aaaa-mm-dd" sem fuso: formatar por texto evita o deslocamento de um dia do new Date().
export function data(iso: string | null | undefined) {
  if (!iso) return '—';
  const [a, m, d] = iso.slice(0, 10).split('-');
  return `${d}/${m}/${a}`;
}

export function mes(iso: string | null | undefined) {
  if (!iso) return '—';
  const [a, m] = iso.split('-');
  return `${MESES[Number(m) - 1]}/${a}`;
}

export const nomeIndice: Record<TipoIndice, string> = {
  Ipca: 'IPCA',
  IgpM: 'IGP-M',
  PercentualFixo: 'Percentual fixo',
};

export const nomeRegra: Record<RegraAplicada, string> = {
  Indice: 'Índice integral',
  PercentualFixo: 'Percentual fixo',
  Teto: 'Limitado pelo teto',
  Piso: 'Limitado pelo piso',
  NaoReducao: 'Não redução',
};

export const status: Record<StatusReajuste, { rotulo: string; tom: 'neutro' | 'aviso' | 'info' | 'ok' | 'apagado' }> = {
  AguardandoIndice: { rotulo: 'Aguardando índice', tom: 'aviso' },
  Calculado: { rotulo: 'Calculado', tom: 'info' },
  Notificado: { rotulo: 'Aviso enviado', tom: 'info' },
  Aplicado: { rotulo: 'Aplicado', tom: 'ok' },
  Substituido: { rotulo: 'Substituído', tom: 'apagado' },
};

export function dias(n: number) {
  if (n === 0) return 'hoje';
  if (n < 0) return `há ${-n} dia${n === -1 ? '' : 's'}`;
  return `em ${n} dia${n === 1 ? '' : 's'}`;
}
