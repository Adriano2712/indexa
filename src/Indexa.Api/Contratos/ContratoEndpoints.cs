using System.Linq.Expressions;
using System.Security.Claims;
using Indexa.Api.Auth;
using Indexa.Api.Comum;
using Indexa.Api.Dados;
using Indexa.Calculo;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Contratos;

public static class ContratoEndpoints
{
    public sealed record ContratoRequest(
        Guid ClienteId,
        string? Descricao,
        decimal ValorInicial,
        DateOnly DataBase,
        TipoIndice TipoIndice,
        decimal? PercentualFixo,
        int? PeriodicidadeMeses,
        int? DefasagemMeses,
        decimal? Teto,
        decimal? Piso,
        UnidadeLimite? UnidadeLimite,
        bool NaoReduzir)
    {
        public DadosContrato ParaDados() => new(
            ClienteId, Descricao ?? "", ValorInicial, DataBase, TipoIndice, PercentualFixo,
            PeriodicidadeMeses ?? Contrato.PeriodicidadePadrao, DefasagemMeses ?? Contrato.DefasagemPadrao,
            Teto, Piso, UnidadeLimite, NaoReduzir);
    }

    public sealed record ContratoResponse(
        Guid Id, Guid ClienteId, string ClienteNome, string Descricao, decimal ValorInicial, decimal ValorAtual, DateOnly DataBase,
        TipoIndice TipoIndice, decimal? PercentualFixo, int PeriodicidadeMeses, int DefasagemMeses, decimal? Teto, decimal? Piso,
        UnidadeLimite? UnidadeLimite, bool NaoReduzir, StatusContrato Status, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm);

    public sealed record ReajusteResponse(
        Guid Id, int Ciclo, int Revisao, DateOnly DataAniversario, decimal ValorAntigo, decimal? ValorNovo,
        decimal? PercentualAcumulado, decimal? PercentualAplicado, DateOnly? JanelaInicio, DateOnly? JanelaFim,
        RegraAplicada? RegraAplicada, StatusReajuste Status, bool Retroativo, string GeradoPor, DateTimeOffset CriadoEm);

    public sealed record ContratoDetalheResponse(ContratoResponse Contrato, IReadOnlyList<ReajusteResponse> Reajustes);

    public sealed record CicloSimuladoResponse(
        int Ciclo, DateOnly Aniversario, bool Retroativo, StatusReajuste Status, decimal ValorAntigo, decimal? ValorNovo,
        decimal? PercentualAcumulado, decimal? PercentualAplicado, RegraAplicada? Regra, DateOnly? JanelaInicio, DateOnly? JanelaFim,
        IReadOnlyList<IndiceUsado> IndicesUsados, IReadOnlyList<DateOnly> MesesFaltantes);

    public sealed record SimulacaoResponse(Guid ContratoId, DateOnly Hoje, IReadOnlyList<CicloSimuladoResponse> Ciclos);

    public static void MapContratos(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/contratos");
        g.MapGet("/", Listar);
        g.MapGet("/{id:guid}", Obter);
        g.MapPost("/", Criar);
        g.MapPut("/{id:guid}", Atualizar);
        g.MapPost("/{id:guid}/encerrar", Encerrar);
        g.MapGet("/{id:guid}/simulacao", Simular);
    }

    /// <summary>
    /// Roda o motor sobre os índices guardados, sem persistir: ciclos vencidos (retroativos) + o próximo aniversário.
    /// </summary>
    private static async Task<IResult> Simular(Guid id, ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var contrato = await db.Contratos.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.Cliente!.UsuarioId == usuarioId, ct);
        if (contrato is null) return Results.NotFound();

        var hoje = relogio.HojeEmBrasilia();
        var (_, proximoAniversario) = Calendario.ProximoCiclo(contrato.DataBase, contrato.PeriodicidadeMeses, hoje);
        var clausulas = ClausulasReajuste.De(contrato);

        var variacoes = contrato.TipoIndice == TipoIndice.PercentualFixo
            ? new Dictionary<DateOnly, decimal>()
            : await db.ValoresIndiceMensal.AsNoTracking()
                .Where(v => v.TipoIndice == contrato.TipoIndice && v.MesReferencia >= contrato.DataBase.AddMonths(-24))
                .ToDictionaryAsync(v => v.MesReferencia, v => v.Valor, ct);

        var ciclos = MotorReajuste.CalcularHistorico(contrato.ValorInicial, contrato.DataBase, clausulas, variacoes, proximoAniversario)
            .Select(c => Mapear(c, hoje))
            .ToList();

        return Results.Ok(new SimulacaoResponse(contrato.Id, hoje, ciclos));
    }

    private static CicloSimuladoResponse Mapear(CicloCalculado c, DateOnly hoje) => c.Resultado switch
    {
        ReajusteCalculado r => new CicloSimuladoResponse(
            c.Ciclo, c.Aniversario, c.Aniversario < hoje, StatusReajuste.Calculado, r.ValorAntigo, r.ValorNovo,
            r.PercentualAcumulado, r.PercentualAplicado, r.Regra, r.Janela?.Inicio, r.Janela?.Fim, r.IndicesUsados, []),
        ReajusteAguardandoIndice a => new CicloSimuladoResponse(
            c.Ciclo, c.Aniversario, c.Aniversario < hoje, StatusReajuste.AguardandoIndice, a.ValorAntigo, null,
            null, null, null, a.Janela.Inicio, a.Janela.Fim, [], a.MesesFaltantes),
        _ => throw new InvalidOperationException($"Resultado desconhecido: {c.Resultado.GetType().Name}"),
    };

    private static readonly Expression<Func<Contrato, ContratoResponse>> Projecao = c => new ContratoResponse(
        c.Id, c.ClienteId, c.Cliente!.Nome, c.Descricao, c.ValorInicial, c.ValorAtual, c.DataBase, c.TipoIndice, c.PercentualFixo,
        c.PeriodicidadeMeses, c.DefasagemMeses, c.Teto, c.Piso, c.UnidadeLimite, c.NaoReduzir, c.Status, c.CriadoEm, c.AtualizadoEm);

    private static async Task<IResult> Listar(
        ClaimsPrincipal user, IndexaDbContext db, string? busca, TipoIndice? tipoIndice, StatusContrato? status,
        int? pagina, int? tamanhoPagina, CancellationToken ct)
    {
        var (numero, tamanho) = Paginacao.Normalizar(pagina, tamanhoPagina);
        var usuarioId = user.UsuarioId();
        var q = db.Contratos.AsNoTracking().Where(c => c.Cliente!.UsuarioId == usuarioId);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = Paginacao.PadraoContem(busca);
            q = q.Where(c => EF.Functions.ILike(c.Descricao, padrao, "\\") || EF.Functions.ILike(c.Cliente!.Nome, padrao, "\\"));
        }
        if (tipoIndice is not null) q = q.Where(c => c.TipoIndice == tipoIndice);
        if (status is not null) q = q.Where(c => c.Status == status);

        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(c => c.Cliente!.Nome).ThenBy(c => c.Descricao).ThenBy(c => c.Id)
            .Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(Projecao).ToListAsync(ct);
        return Results.Ok(new Pagina<ContratoResponse>(itens, total, numero, tamanho));
    }

    private static async Task<IResult> Obter(Guid id, ClaimsPrincipal user, IndexaDbContext db, CancellationToken ct)
    {
        var contrato = await ProjetarAsync(db, id, user.UsuarioId(), ct);
        if (contrato is null) return Results.NotFound();

        var reajustes = await db.Reajustes.AsNoTracking()
            .Where(r => r.ContratoId == id)
            .OrderBy(r => r.Ciclo).ThenBy(r => r.Revisao)
            .Select(r => new ReajusteResponse(
                r.Id, r.Ciclo, r.Revisao, r.DataAniversario, r.ValorAntigo, r.ValorNovo, r.PercentualAcumulado, r.PercentualAplicado,
                r.JanelaInicio, r.JanelaFim, r.RegraAplicada, r.Status, r.Retroativo, r.GeradoPor, r.CriadoEm))
            .ToListAsync(ct);

        return Results.Ok(new ContratoDetalheResponse(contrato, reajustes));
    }

    private static async Task<IResult> Criar(
        ContratoRequest req, ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var contrato = Contrato.Criar(req.ParaDados(), relogio.GetUtcNow());
        if (!await ClientePertenceAoUsuarioAsync(db, contrato.ClienteId, usuarioId, ct))
            return ClienteNaoEncontrado();

        db.Contratos.Add(contrato);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/contratos/{contrato.Id}", await ProjetarAsync(db, contrato.Id, usuarioId, ct));
    }

    private static async Task<IResult> Atualizar(
        Guid id, ContratoRequest req, ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var contrato = await db.Contratos.SingleOrDefaultAsync(c => c.Id == id && c.Cliente!.UsuarioId == usuarioId, ct);
        if (contrato is null) return Results.NotFound();

        var dados = req.ParaDados();
        var possuiReajustes = await db.Reajustes.AnyAsync(r => r.ContratoId == id, ct);
        var clienteAnterior = contrato.ClienteId;
        contrato.Atualizar(dados, possuiReajustes, relogio.GetUtcNow());

        if (dados.ClienteId != clienteAnterior && !await ClientePertenceAoUsuarioAsync(db, dados.ClienteId, usuarioId, ct))
            return ClienteNaoEncontrado();

        await db.SaveChangesAsync(ct);
        return Results.Ok(await ProjetarAsync(db, id, usuarioId, ct));
    }

    private static async Task<IResult> Encerrar(Guid id, ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var contrato = await db.Contratos.SingleOrDefaultAsync(c => c.Id == id && c.Cliente!.UsuarioId == usuarioId, ct);
        if (contrato is null) return Results.NotFound();

        contrato.Encerrar(relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ProjetarAsync(db, id, usuarioId, ct));
    }

    private static Task<bool> ClientePertenceAoUsuarioAsync(IndexaDbContext db, Guid clienteId, Guid usuarioId, CancellationToken ct) =>
        db.Clientes.AnyAsync(c => c.Id == clienteId && c.UsuarioId == usuarioId, ct);

    private static IResult ClienteNaoEncontrado() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["clienteId"] = ["Cliente não encontrado."] });

    private static Task<ContratoResponse?> ProjetarAsync(IndexaDbContext db, Guid id, Guid usuarioId, CancellationToken ct) =>
        db.Contratos.AsNoTracking().Where(c => c.Id == id && c.Cliente!.UsuarioId == usuarioId).Select(Projecao).SingleOrDefaultAsync(ct);
}
