# Registro de decisões (ADRs)

Formato curto: **decisão · alternativas · motivo · custo de reverter · gatilho de reavaliação**.
As decisões de cálculo (ADR-01 a 07) modelam cláusulas contratuais comuns, mas **não são parecer jurídico**: o texto de cada contrato prevalece e deve ser confirmado com o cliente ou um advogado.

---

## Cálculo

### ADR-01 · Janela de apuração e defasagem
- **Decisão:** a janela tem `periodicidade` meses e termina `defasagem + 1` meses antes do mês do aniversário. Padrão: defasagem 1 (aniversário em novembro → janela out/ano anterior a set). Configurável por contrato (0 a 6).
- **Alternativas:** janela terminando no mês anterior ao aniversário (defasagem 0); defasagem fixa de 2.
- **Motivo:** IPCA e IGP-M saem no início do mês seguinte ao de referência (confirmado na API do SGS: em 24/10/2026 o último mês publicado era set/2026). Com defasagem 1 o índice já existe antes do aniversário e o aviso sai com antecedência; com 0, quase sempre atrasa.
- **Custo de reverter:** baixo. Campo por contrato; cada reajuste grava a janela que usou.
- **Gatilho:** contrato do beta com cláusula de período diferente.

### ADR-02 · Teto e piso sobre o percentual
- **Decisão:** teto e piso limitam o **percentual acumulado** (padrão). Variante por contrato: limite em **R$ de aumento** (`unidadeLimite = Valor`).
- **Alternativas:** limitar o valor final em R$.
- **Motivo:** cláusulas usuais falam em "reajuste limitado a X%". A variante em R$ existe desde o início para não exigir migração.
- **Custo de reverter:** médio (regra no motor + testes).
- **Gatilho:** contrato com teto expresso de outra forma.

### ADR-03 · Índice negativo e "não reduzir"
- **Decisão:** sem a cláusula, a redução é aplicada; com `naoReduzir`, o valor é mantido. Ordem: acumulado → teto/piso → não redução. O acumulado bruto negativo continua registrado para auditoria.
- **Alternativas:** tratar índice negativo como zero por padrão.
- **Motivo:** fidelidade ao índice oficial é o padrão; não reduzir é exceção contratual explícita. Caso real: IGP-M 2025 = −1,04%.
- **Custo de reverter:** baixo.
- **Gatilho:** nenhum técnico. Depende do texto do contrato.

### ADR-04 · Arredondamento
- **Decisão:** `decimal` em todo o cálculo; **um único arredondamento**, no valor final, 2 casas, meio exato para longe do zero (`MidpointRounding.AwayFromZero`), em `Arredondamento.Monetario`. O acumulado nunca é arredondado no meio.
- **Alternativas:** arredondar o percentual antes de multiplicar; arredondamento bancário.
- **Motivo:** menor erro composto e conferência simples. Exemplo: arredondar 4,26% antes daria R$ 2.606,50 em vez de R$ 2.606,61.
- **Custo de reverter:** baixo no código, alto depois de cartas emitidas.
- **Gatilho:** cliente exigir regra de arredondamento diferente em contrato.

### ADR-05 · Aniversários raros e contratos antigos
- **Decisão:** aniversário = `dataBase.AddMonths(ciclo × periodicidade)`, sempre a partir da data-base (31/01 → 29/02 → 31/03, sem "escorregar"; 29/02 vira 28/02 em ano não bissexto). Contrato cadastrado com data-base no passado tem os ciclos vencidos calculados e aplicados como **retroativos, sem aviso**.
- **Alternativas:** não recalcular o histórico (perde a auditoria, que é o diferencial do produto).
- **Motivo:** auditoria completa desde o primeiro dia.
- **Custo de reverter:** alto com dados reais.
- **Gatilho:** contrato do beta com data-base 29/02.

### ADR-06 · Índice ainda não divulgado
- **Decisão:** sem todos os meses da janela, o reajuste fica `AguardandoIndice`; o job diário tenta de novo; o aviso só sai depois do cálculo. Nunca há cálculo parcial.
- **Alternativas:** estimar com projeção de mercado (Focus).
- **Motivo:** o produto promete o índice oficial. O SGS responde 404 "Value(s) not found" para mês não publicado, o que torna a detecção simples.
- **Custo de reverter:** baixo.
- **Gatilho:** nenhum.

### ADR-07 · Acumulado pela composição das variações mensais
- **Decisão:** acumulado = Π(1 + vᵢ/100) − 1 sobre as variações mensais publicadas no SGS.
- **Alternativas:** usar a série oficial de acumulado (IPCA 12 meses, SGS 13522) ou o número-índice.
- **Motivo:** funciona para qualquer janela e índice, e é como cláusulas costumam descrever o reajuste. Conferido contra a série 13522: 126 de 129 janelas idênticas e 3 com diferença de 0,01 p.p. (05/2018, 02/2020, 12/2022), porque o IBGE acumula pelo número-índice e as variações mensais vêm arredondadas.
- **Custo de reverter:** baixo (troca a fonte do acumulado; o motor não muda).
- **Gatilho:** contrato que cite "acumulado divulgado pelo IBGE".

---

## Arquitetura

### ADR-08 · Fonte de índices: API SGS do Banco Central
- **Decisão:** séries 433 (IPCA) e 189 (IGP-M), atrás de `IIndexProvider`, com timeout por tentativa, 3 retentativas e circuit breaker.
- **Alternativas:** IBGE/SIDRA; raspagem de sites.
- **Motivo:** API pública, sem autenticação e mantida pelo BC. Uma série inexistente não responde (medido), daí o timeout obrigatório.
- **Custo de reverter:** baixo (nova implementação da interface).
- **Gatilho:** mudança de formato ou descontinuação.

### ADR-09 · Jobs com Hangfire no próprio Postgres
- **Decisão:** Hangfire com storage PostgreSQL (schema `hangfire`), no mesmo processo da API. Retentativas com espera crescente; o que esgota fica em *Failed* no painel.
- **Alternativas:** Quartz.NET; fila externa (SQS, RabbitMQ); `BackgroundService` próprio.
- **Motivo:** menor número de serviços. Retentativa, painel e histórico de falhas já vêm prontos.
- **Custo de reverter:** médio (os jobs são finos e chamam serviços que não dependem do Hangfire).
- **Gatilho:** volume que exija workers separados.

### ADR-10 · Idempotência e concorrência no banco
- **Decisão:** índice único parcial `(contrato, ciclo) WHERE status <> 'Substituido'`, chave de idempotência única por revisão, controle otimista por `xmin` e reserva da notificação (`Enviando`) antes do SMTP.
- **Alternativas:** locks distribuídos; confiar só em `DisableConcurrentExecution`.
- **Motivo:** a garantia fica no lugar que não pode falhar. Testado com 6 processamentos e 5 envios em paralelo.
- **Custo de reverter:** baixo.
- **Gatilho:** nenhum.

### ADR-11 · Histórico imutável com trigger
- **Decisão:** reajuste notificado ou aplicado não muda (aplicação + trigger `trg_reajustes_imutavel`). Correção = nova revisão; a anterior vira `Substituido`. Cada reajuste guarda cópia (jsonb) dos índices e das cláusulas usados.
- **Alternativas:** só validação na aplicação; event sourcing.
- **Motivo:** auditoria confiável mesmo diante de UPDATE manual; event sourcing seria desproporcional para o MVP.
- **Custo de reverter:** baixo.
- **Gatilho:** necessidade de trilha de quem alterou o quê (tabela de auditoria).

### ADR-12 · E-mail por SMTP; PDF com QuestPDF
- **Decisão:** `INotifier` implementado com SMTP (MailKit), que serve para Amazon SES, Resend ou qualquer provedor; Mailpit no desenvolvimento. Carta em PDF com QuestPDF, licença Community (gratuita para receita bruta anual abaixo de US$ 1 milhão, conforme a licença v3.0 de 06/07/2026), apenas com fontes embutidas.
- **Alternativas:** SDK do provedor; geração de PDF via HTML + navegador headless.
- **Motivo:** nenhum SDK de fornecedor no domínio. QuestPDF é puro .NET e não precisa de navegador no container.
- **Custo de reverter:** baixo (interfaces).
- **Gatilho:** faturamento perto do limite da licença; necessidade de rastrear abertura de e-mail.

### ADR-13 · Deploy: uma VM com Docker Compose
- **Decisão:** um container (API + painel + jobs) + Postgres + Caddy (HTTPS automático) numa única VM; alvo inicial: camada gratuita da Oracle Cloud (VM Ampere/ARM). **[VALIDAR limites e disponibilidade na página oficial antes de criar a conta.]**
- **Alternativas:** AWS (ECS/Fargate + RDS) e PaaS gerenciados: mais operação pronta, mas com custo mensal fixo desde o primeiro dia.
- **Motivo:** beta com 2 ou 3 usuários; menor custo e menor número de peças. A imagem é multi-stage e roda em x86 e ARM.
- **Custo de reverter:** baixo (é só uma imagem Docker e um Postgres).
- **Gatilho:** mais de um servidor de aplicação (PDFs vão para object storage, `IArmazenamentoDocumentos`), ou backup gerenciado exigido.

### ADR-14 · .NET 10 (LTS)
- **Decisão:** `net10.0`, pacotes 10.0.x, `global.json` em 10.0 com `rollForward: latestFeature`. O projeto começou em .NET 8 (SDK disponível no início) e migrou antes do deploy.
- **Alternativas:** ficar no .NET 8.
- **Motivo:** o .NET 8 sai de suporte em 10/11/2026; o .NET 10 é LTS. A migração não exigiu mudança de código nem de modelo (EF sem migração pendente).
- **Custo de reverter:** baixo.
- **Gatilho:** próximo LTS.

### ADR-15 · Sessão do painel em cookie HttpOnly
- **Decisão:** o login grava o JWT num cookie `HttpOnly`, `SameSite=Strict` e `Secure`; a API aceita o cookie ou o header `Authorization`.
- **Alternativas:** token no `localStorage` (versão inicial); sessão no servidor.
- **Motivo:** uma falha de XSS não consegue ler o token; `SameSite=Strict` cobre CSRF sem token anti-CSRF, porque painel e API estão na mesma origem.
- **Custo de reverter:** baixo.
- **Gatilho:** necessidade de revogar sessões no servidor (lista de tokens revogados ou sessão persistida).
