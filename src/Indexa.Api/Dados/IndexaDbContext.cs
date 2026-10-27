using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Indexa.Api.Dados;

public sealed class IndexaDbContext(DbContextOptions<IndexaDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Contrato> Contratos => Set<Contrato>();
    public DbSet<Reajuste> Reajustes => Set<Reajuste>();
    public DbSet<ValorIndiceMensal> ValoresIndiceMensal => Set<ValorIndiceMensal>();
    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();
    public DbSet<DivergenciaIndice> DivergenciasIndice => Set<DivergenciaIndice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.SenhaHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.EmailAviso).HasMaxLength(254);
            e.Property(x => x.AntecedenciaAvisoDias).HasDefaultValue(Usuario.AntecedenciaPadrao);
            e.Property(x => x.AvisoEmailAtivo).HasDefaultValue(true);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Cliente>(e =>
        {
            e.ToTable("clientes");
            e.Property(x => x.Nome).HasMaxLength(Cliente.TamanhoMaximoNome).IsRequired();
            e.Property(x => x.Documento).HasMaxLength(14);
            e.Property(x => x.Email).HasMaxLength(254);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UsuarioId, x.Nome });
        });

        b.Entity<Contrato>(e =>
        {
            e.ToTable("contratos");
            e.Property(x => x.Descricao).HasMaxLength(Contrato.TamanhoMaximoDescricao).IsRequired();
            e.Property(x => x.ValorInicial).HasPrecision(14, 2);
            e.Property(x => x.ValorAtual).HasPrecision(14, 2);
            e.Property(x => x.PercentualFixo).HasPrecision(9, 4);
            e.Property(x => x.Teto).HasPrecision(16, 4);
            e.Property(x => x.Piso).HasPrecision(16, 4);
            e.Property(x => x.TipoIndice).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UnidadeLimite).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status);
            // uint + IsRowVersion → coluna de sistema xmin do Postgres (controle otimista de concorrência).
            e.Property(x => x.Versao).IsRowVersion();
        });

        b.Entity<Reajuste>(e =>
        {
            e.ToTable("reajustes");
            e.Property(x => x.ValorAntigo).HasPrecision(14, 2);
            e.Property(x => x.ValorNovo).HasPrecision(14, 2);
            // numeric sem escala: guarda o percentual exatamente como o motor calculou (ADR-04: só o valor em R$ é arredondado).
            e.Property(x => x.PercentualAcumulado).HasColumnType("numeric");
            e.Property(x => x.PercentualAplicado).HasColumnType("numeric");
            e.Property(x => x.RegraAplicada).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.GeradoPor).HasMaxLength(100).IsRequired();
            e.Property(x => x.ChaveIdempotencia).HasMaxLength(100).IsRequired();
            e.Property(x => x.CaminhoPdf).HasMaxLength(500);
            e.Property(x => x.IndicesUsadosJson).HasColumnType("jsonb");
            e.Property(x => x.ClausulasJson).HasColumnType("jsonb");
            e.Property(x => x.Versao).IsRowVersion();
            e.HasOne<Contrato>().WithMany().HasForeignKey(x => x.ContratoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Reajuste>().WithMany().HasForeignKey(x => x.SubstituiReajusteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ChaveIdempotencia).IsUnique();
            // Idempotência: no máximo um reajuste vigente (não substituído) por contrato + ciclo.
            e.HasIndex(x => new { x.ContratoId, x.Ciclo })
                .IsUnique()
                .HasFilter("\"Status\" <> 'Substituido'")
                .HasDatabaseName("ux_reajustes_contrato_ciclo_vigente");
            e.HasIndex(x => new { x.Status, x.DataAniversario });
        });

        b.Entity<ValorIndiceMensal>(e =>
        {
            e.ToTable("valores_indice_mensal");
            e.Property(x => x.TipoIndice).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Valor).HasPrecision(9, 4);
            e.Property(x => x.Fonte).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.TipoIndice, x.MesReferencia }).IsUnique();
        });

        b.Entity<DivergenciaIndice>(e =>
        {
            e.ToTable("divergencias_indice");
            e.Property(x => x.TipoIndice).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ValorAnterior).HasPrecision(9, 4);
            e.Property(x => x.ValorNovo).HasPrecision(9, 4);
            e.Property(x => x.Fonte).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.DetectadaEm);
        });

        b.Entity<Notificacao>(e =>
        {
            e.ToTable("notificacoes");
            e.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UltimoErro).HasMaxLength(2000);
            e.Property(x => x.Destinatario).HasMaxLength(254).IsRequired();
            e.Property(x => x.Versao).IsRowVersion();
            e.HasIndex(x => x.Status);
            e.HasOne<Reajuste>().WithMany().HasForeignKey(x => x.ReajusteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ReajusteId, x.Canal }).IsUnique();
        });
    }
}

public sealed class IndexaDbContextDesignFactory : IDesignTimeDbContextFactory<IndexaDbContext>
{
    public IndexaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IndexaDbContext>().UseNpgsql("Host=localhost;Database=indexa_design").Options);
}
