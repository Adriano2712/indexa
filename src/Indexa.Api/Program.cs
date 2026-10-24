using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Indexa.Api.Auth;
using Indexa.Api.Clientes;
using Indexa.Api.Comum;
using Indexa.Api.Contratos;
using Indexa.Api.Dados;
using Indexa.Api.Indices;
using Indexa.Dominio;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(o => Encoding.UTF8.GetByteCount(o.Chave) >= 32, "Jwt:Chave deve ter pelo menos 32 bytes.")
    .ValidateOnStart();

builder.Services.AddDbContext<IndexaDbContext>((sp, o) => o.UseNpgsql(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Indexa")
    ?? throw new InvalidOperationException("ConnectionStrings:Indexa não configurada.")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddSingleton<EmissorToken>();
builder.Services.AddScoped<ServicoColetaIndices>();

builder.Services.AddHttpClient<IIndexProvider, SgsIndexProvider>(http =>
        http.BaseAddress = new Uri(builder.Configuration["Sgs:UrlBase"] ?? "https://api.bcb.gov.br/"))
    .AddStandardResilienceHandler(o =>
    {
        // Série inexistente no SGS não responde: sem timeout por tentativa a coleta ficaria pendurada.
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromSeconds(2);
    });

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Emissor,
            ValidAudience = jwt.Value.Audiencia,
            IssuerSigningKey = jwt.Value.ChaveDeAssinatura(),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:LimiteLoginPorMinuto", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorExcecoes>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Configuration.GetValue<bool>("Banco:AplicarMigracoesAoIniciar"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<IndexaDbContext>().Database.Migrate();
}

app.MapHealthChecks("/health");
app.MapAuth();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapClientes();
api.MapContratos();
api.MapIndices();

app.Run();

public partial class Program { }
