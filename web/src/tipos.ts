export type TipoIndice = 'Ipca' | 'IgpM' | 'PercentualFixo';
export type UnidadeLimite = 'Percentual' | 'Valor';
export type StatusContrato = 'Ativo' | 'Encerrado';
export type StatusReajuste = 'AguardandoIndice' | 'Calculado' | 'Notificado' | 'Aplicado' | 'Substituido';
export type RegraAplicada = 'Indice' | 'PercentualFixo' | 'Teto' | 'Piso' | 'NaoReducao';

export interface Pagina<T> {
  itens: T[];
  total: number;
  numeroPagina: number;
  tamanhoPagina: number;
}

export interface Cliente {
  id: string;
  nome: string;
  documento: string | null;
  email: string | null;
  quantidadeContratos: number;
}

export interface Contrato {
  id: string;
  clienteId: string;
  clienteNome: string;
  descricao: string;
  valorInicial: number;
  valorAtual: number;
  dataBase: string;
  tipoIndice: TipoIndice;
  percentualFixo: number | null;
  periodicidadeMeses: number;
  defasagemMeses: number;
  teto: number | null;
  piso: number | null;
  unidadeLimite: UnidadeLimite | null;
  naoReduzir: boolean;
  status: StatusContrato;
  ultimoCicloAplicado: number;
  proximoAniversario: string | null;
}

export interface Reajuste {
  id: string;
  ciclo: number;
  revisao: number;
  dataAniversario: string;
  valorAntigo: number;
  valorNovo: number | null;
  percentualAcumulado: number | null;
  percentualAplicado: number | null;
  janelaInicio: string | null;
  janelaFim: string | null;
  regraAplicada: RegraAplicada | null;
  status: StatusReajuste;
  retroativo: boolean;
  geradoPor: string;
}

export interface ContratoDetalhe {
  contrato: Contrato;
  reajustes: Reajuste[];
}

export interface CicloSimulado {
  ciclo: number;
  aniversario: string;
  retroativo: boolean;
  status: StatusReajuste;
  valorAntigo: number;
  valorNovo: number | null;
  percentualAplicado: number | null;
  regra: RegraAplicada | null;
  janelaInicio: string | null;
  janelaFim: string | null;
  mesesFaltantes: string[];
}

export interface Simulacao {
  hoje: string;
  ciclos: CicloSimulado[];
}

export interface ProximoReajuste {
  contratoId: string;
  descricao: string;
  cliente: string;
  ciclo: number;
  aniversario: string;
  diasRestantes: number;
  tipoIndice: TipoIndice;
  valorAtual: number;
  valorNovo: number | null;
  status: StatusReajuste | null;
}

export interface HistoricoReajuste {
  reajusteId: string;
  contratoId: string;
  descricao: string;
  ciclo: number;
  aniversario: string;
  valorAntigo: number;
  valorNovo: number;
  percentualAplicado: number;
  regra: RegraAplicada;
  retroativo: boolean;
}

export interface Painel {
  hoje: string;
  contratosAtivos: number;
  valorMensalAtivo: number;
  reajustesProximos30Dias: number;
  aguardandoIndice: number;
  avisosComFalha: number;
  ultimoIpca: string | null;
  ultimoIgpM: string | null;
  proximos: ProximoReajuste[];
  historico: HistoricoReajuste[];
}

export interface Configuracoes {
  antecedenciaAvisoDias: number;
  emailAviso: string | null;
  destinatarioEfetivo: string;
  avisoEmailAtivo: boolean;
}
