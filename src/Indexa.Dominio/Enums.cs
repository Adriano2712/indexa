namespace Indexa.Dominio;

public enum TipoIndice { Ipca = 1, IgpM = 2, PercentualFixo = 3 }

public enum UnidadeLimite { Percentual = 1, Valor = 2 }

public enum StatusContrato { Ativo = 1, Encerrado = 2 }

public enum StatusReajuste { AguardandoIndice = 1, Calculado = 2, Notificado = 3, Aplicado = 4, Substituido = 5 }

public enum RegraAplicada { Indice = 1, PercentualFixo = 2, Teto = 3, Piso = 4, NaoReducao = 5 }

public enum CanalNotificacao { Email = 1, Telegram = 2 }

public enum StatusNotificacao { Pendente = 1, Enviada = 2, Falhou = 3, Enviando = 4, Cancelada = 5 }
