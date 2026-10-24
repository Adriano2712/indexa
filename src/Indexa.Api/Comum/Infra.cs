using Indexa.Dominio;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Indexa.Api.Comum;

public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Total, int NumeroPagina, int TamanhoPagina);

internal static class Paginacao
{
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;

    public static (int Numero, int Tamanho) Normalizar(int? pagina, int? tamanho) =>
        (Math.Max(1, pagina ?? 1), Math.Clamp(tamanho ?? TamanhoPadrao, 1, TamanhoMaximo));

    public static string PadraoContem(string termo) =>
        "%" + termo.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}

internal sealed class TratadorExcecoes(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        ProblemDetails? pd = ex switch
        {
            DadosInvalidosException d => new HttpValidationProblemDetails(d.Erros)
                { Status = StatusCodes.Status400BadRequest, Title = "Dados inválidos." },
            BadHttpRequestException b => new ProblemDetails { Status = b.StatusCode, Title = "Requisição inválida." },
            RegraDeNegocioException r => new ProblemDetails
                { Status = StatusCodes.Status409Conflict, Title = "Operação não permitida.", Detail = r.Message },
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Registro duplicado." },
            _ => null,
        };
        if (pd is null) return false;

        ctx.Response.StatusCode = pd.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd, Exception = ex });
    }
}
