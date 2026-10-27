using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Indexa.Api.Auth;
using Indexa.Api.Avisos;
using Indexa.Api.Clientes;
using Indexa.Api.Comum;
using Indexa.Api.Contratos;
using Indexa.Api.Dados;
using Indexa.Api.Documentos;
using Indexa.Api.Indices;
using Indexa.Api.Jobs;
using Indexa.Api.Painel;
using Indexa.Api.Reajustes;
using Indexa.Dominio;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

string ConnectionString(IServiceProvider sp) =>
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Indexa")
    ?? throw new InvalidOperationException("ConnectionStrings:Indexa não configurada.");

builder.Services.AddOptions<JwtOptions>()
    .Bind(config.GetSection("Jwt"))
    .Validate(o => Encoding.UTF8.GetByteCount(o.Chave) >= 32, "Jwt:Chave deve ter pelo menos 32 bytes.")
    .ValidateOnStart();
builder.Services.Configure<SmtpOptions>(config.GetSection("Smtp"));
builder.Services.Configure<ArmazenamentoOptions>(config.GetSection("Armazenamento"));

builder.Services.AddDbContext<IndexaDbContext>((sp, o) => o.UseNpgsql(ConnectionString(sp)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddSingleton<EmissorToken>();
builder.Services.AddSingleton<IPdfGenerator, CartaReajustePdf>();
builder.Services.AddSingleton<IArmazenamentoDocumentos, ArmazenamentoLocal>();
builder.Services.AddSingleton<INotifier, SmtpNotifier>();
builder.Services.AddScoped<ServicoColetaIndices>();
builder.Services.AddScoped<ServicoReajustes>();
builder.Services.AddScoped<ServicoDocumentos>();
builder.Services.AddScoped<ServicoEnvioAvisos>();
builder.Services.AddScoped<IFilaJobs, FilaHangfire>();

builder.Services.AddHttpClient<IIndexProvider, SgsIndexProvider>(http =>
        http.BaseAddress = new Uri(config["Sgs:UrlBase"] ?? "https://api.bcb.gov.br/"))
    .AddStandardResilienceHandler(o =>
    {
        // Série inexistente no SGS não responde: sem timeout por tentativa a coleta ficaria pendurada.
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromSeconds(2);
    });

builder.Services.AddHangfire((sp, h) => h
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        pg => pg.UseNpgsqlConnection(ConnectionString(sp)),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
if (config.GetValue("Hangfire:Servidor", true))
    builder.Services.AddHangfireServer(o => o.WorkerCount = config.GetValue("Hangfire:Workers", 2));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, TimeProvider>((o, jwt, relogio) =>
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
            // Mesmo relógio da emissão (TimeProvider), em vez do relógio do sistema embutido na biblioteca.
            LifetimeValidator = (inicio, fim, _, p) =>
            {
                var agora = relogio.GetUtcNow().UtcDateTime;
                return (inicio is null || inicio <= agora + p.ClockSkew) && fim is not null && fim > agora - p.ClockSkew;
            },
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
// Painel React compilado em wwwroot: mesma origem da API (sem CORS) e um container só.
// Estáticos antes do roteamento; senão o fallback do SPA "casa" antes e /assets/*.js volta como index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Configuration.GetValue<bool>("Banco:AplicarMigracoesAoIniciar"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<IndexaDbContext>().Database.Migrate();
}

if (app.Configuration.GetValue("Hangfire:Servidor", true))
    AgendaJobs.Registrar(app.Services.GetRequiredService<IRecurringJobManager>());

// Painel de jobs (falhas, retentativas, disparo manual). Padrão do Hangfire: só acesso local.
if (app.Configuration.GetValue<bool>("Hangfire:Painel"))
    app.UseHangfireDashboard("/hangfire");

app.MapHealthChecks("/health");
app.MapAuth();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapClientes();
api.MapContratos();
api.MapIndices();
api.MapReajustes();
api.MapPainel();

app.MapFallbackToFile("{*rota:regex(^(?!api/|hangfire).*$)}", "index.html");

app.Run();

public partial class Program { }
