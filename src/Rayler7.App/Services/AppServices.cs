using System.IO;
using Rayler7.Core.Audit;
using Rayler7.Infrastructure.Persistence;
using Rayler7.Infrastructure.Projects;
using Rayler7.Infrastructure.Security;

namespace Rayler7.App.Services;

/// <summary>Raiz de composição: cria os serviços uma vez e os entrega às telas.</summary>
public sealed class AppServices
{
    public AppServices()
    {
        // RAYLER7_DATA permite apontar para uma pasta de testes sem tocar nos dados reais.
        DataRoot = Environment.GetEnvironmentVariable("RAYLER7_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAYLER7");
        Directory.CreateDirectory(DataRoot);
        Database = new Database(Path.Combine(DataRoot, "rayler7.db"));
        Database.Migrate();
        Auth = new AuthService(Database, new PasswordHasher());
        Projects = new ProjectService(Database, Path.Combine(DataRoot, "projetos"));
        Audit = new SqliteAuditLog(Database);
        Mappings = new MappingStore(Database);
    }

    /// <summary>Pasta onde ficam banco, projetos e logs (documentado na Central de Ajuda).</summary>
    public string DataRoot { get; }

    public Database Database { get; }

    public AuthService Auth { get; }

    public ProjectService Projects { get; }

    public IAuditLog Audit { get; }

    public MappingStore Mappings { get; }

    public UserAccount? CurrentUser { get; set; }
}
