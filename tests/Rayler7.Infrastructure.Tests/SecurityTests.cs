using Rayler7.Infrastructure.Persistence;
using Rayler7.Infrastructure.Security;

namespace Rayler7.Infrastructure.Tests;

public class SecurityTests
{
    private static readonly PasswordHasher FastHasher = new() { MemoryKib = 1024, Iterations = 1, Parallelism = 1 };

    private static (AuthService Auth, ManualClock Clock, Database Db) Setup(TempDir dir)
    {
        var db = new Database(dir.File("r7.db"));
        db.Migrate();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        return (new AuthService(db, FastHasher, clock), clock, db);
    }

    [Fact]
    public void Hash_argon2id_com_salt_individual_e_sem_texto_puro()
    {
        var h1 = FastHasher.Hash("Senha123");
        var h2 = FastHasher.Hash("Senha123");

        Assert.StartsWith("argon2id$v=19$", h1, StringComparison.Ordinal);
        Assert.NotEqual(h1, h2);
        Assert.DoesNotContain("Senha123", h1, StringComparison.Ordinal);
        Assert.True(FastHasher.Verify("Senha123", h1));
        Assert.False(FastHasher.Verify("senha123", h1));
    }

    [Theory]
    [InlineData("curta1")]
    [InlineData("somenteletras")]
    [InlineData("12345678")]
    public void Politica_de_senha(string weak)
    {
        Assert.Throws<ArgumentException>(() => FastHasher.Hash(weak));
    }

    [Fact]
    public void Migracao_e_idempotente()
    {
        using var dir = new TempDir();
        var (_, _, db) = Setup(dir);
        db.Migrate();
        Assert.Equal(1, db.CurrentVersion());
    }

    [Fact]
    public void Primeiro_administrador_so_uma_vez()
    {
        using var dir = new TempDir();
        var (auth, _, _) = Setup(dir);

        Assert.False(auth.HasAnyUser());
        var admin = auth.CreateFirstAdministrator("jhon", "Jhon", "Rayler7forte");
        Assert.Equal(UserRole.Administrador, admin.Role);
        Assert.Throws<InvalidOperationException>(() => auth.CreateFirstAdministrator("outro", "Outro", "Rayler7forte"));
    }

    [Fact]
    public void Login_correto_e_incorreto_com_mesma_mensagem()
    {
        using var dir = new TempDir();
        var (auth, _, _) = Setup(dir);
        auth.CreateFirstAdministrator("jhon", "Jhon", "Rayler7forte");

        Assert.Equal(LoginOutcome.Success, auth.Login("JHON", "Rayler7forte").Outcome);
        Assert.Equal(LoginOutcome.InvalidCredentials, auth.Login("jhon", "errada123").Outcome);
        Assert.Equal(LoginOutcome.InvalidCredentials, auth.Login("ninguem", "errada123").Outcome);
    }

    [Fact]
    public void Bloqueio_apos_tentativas_e_liberacao_apos_tempo()
    {
        using var dir = new TempDir();
        var (auth, clock, _) = Setup(dir);
        auth.CreateFirstAdministrator("jhon", "Jhon", "Rayler7forte");

        for (var i = 0; i < AuthService.MaxAttemptsBeforeLock - 1; i++)
        {
            Assert.Equal(LoginOutcome.InvalidCredentials, auth.Login("jhon", "errada123").Outcome);
        }

        var locked = auth.Login("jhon", "errada123");
        Assert.Equal(LoginOutcome.LockedOut, locked.Outcome);

        // Mesmo com a senha certa, continua bloqueado.
        Assert.Equal(LoginOutcome.LockedOut, auth.Login("jhon", "Rayler7forte").Outcome);

        clock.Now = locked.LockedUntil!.Value.AddSeconds(1);
        Assert.Equal(LoginOutcome.Success, auth.Login("jhon", "Rayler7forte").Outcome);
    }

    [Fact]
    public void Usuario_comum_nao_gerencia_usuarios_e_desativado_nao_entra()
    {
        using var dir = new TempDir();
        var (auth, _, _) = Setup(dir);
        var admin = auth.CreateFirstAdministrator("jhon", "Jhon", "Rayler7forte");
        var ana = auth.CreateUser(admin, "ana", "Ana", "Pesquisa2026", UserRole.Usuario);

        Assert.Throws<UnauthorizedAccessException>(() => auth.CreateUser(ana, "x", "X", "Pesquisa2026", UserRole.Usuario));
        auth.SetActive(admin, ana.Id, false);
        Assert.Equal(LoginOutcome.InvalidCredentials, auth.Login("ana", "Pesquisa2026").Outcome);
        Assert.Throws<InvalidOperationException>(() => auth.SetActive(admin, admin.Id, false));
        Assert.Equal(2, auth.ListUsers(admin).Count);
    }

    [Fact]
    public void Dpapi_protege_e_recupera()
    {
        var p = SecretProtector.Protect("token-secreto");
        Assert.DoesNotContain("token-secreto", p, StringComparison.Ordinal);
        Assert.Equal("token-secreto", SecretProtector.Unprotect(p));
    }

    [Fact]
    public void Auditoria_persistente()
    {
        using var dir = new TempDir();
        var (_, _, db) = Setup(dir);
        var log = new SqliteAuditLog(db);
        var project = Guid.NewGuid();

        log.Append(new Core.Audit.AuditEntry(project, "ana", DateTimeOffset.UtcNow, "Correção manual", "registro:1", "1,08", "1,80", "foto.jpg", "conferido"));

        var e = Assert.Single(log.Entries(project));
        Assert.Equal("1,08", e.OldValue);
        Assert.Equal("1,80", e.NewValue);
    }
}
