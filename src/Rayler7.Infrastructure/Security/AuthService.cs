using System.Globalization;
using Rayler7.Infrastructure.Persistence;

namespace Rayler7.Infrastructure.Security;

public enum UserRole
{
    Administrador,
    Usuario,
}

public sealed record UserAccount(Guid Id, string Username, string DisplayName, UserRole Role, bool IsActive);

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
}

public sealed record LoginResult(LoginOutcome Outcome, UserAccount? User, DateTimeOffset? LockedUntil);

/// <summary>
/// Autenticação local, sem nuvem. Sem senha universal: o primeiro administrador só pode ser criado
/// enquanto não existir nenhum usuário. Bloqueio progressivo após tentativas erradas.
/// Mensagens não revelam se o usuário existe.
/// </summary>
public sealed class AuthService(Database db, PasswordHasher hasher, TimeProvider? clock = null)
{
    public const int MaxAttemptsBeforeLock = 5;
    private static readonly TimeSpan BaseLock = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxLock = TimeSpan.FromHours(1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lazy<string> _dummyHash = new(() => hasher.Hash("dummy-password-1"));

    public bool HasAnyUser()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public UserAccount CreateFirstAdministrator(string username, string displayName, string password)
    {
        if (HasAnyUser())
        {
            throw new InvalidOperationException("Já existe um administrador. Use o gerenciamento de usuários.");
        }

        return Insert(username, displayName, password, UserRole.Administrador);
    }

    public UserAccount CreateUser(UserAccount actingAdmin, string username, string displayName, string password, UserRole role)
    {
        RequireAdmin(actingAdmin);
        return Insert(username, displayName, password, role);
    }

    public void SetActive(UserAccount actingAdmin, Guid userId, bool active)
    {
        RequireAdmin(actingAdmin);
        if (userId == actingAdmin.Id && !active)
        {
            throw new InvalidOperationException("Você não pode desativar a própria conta.");
        }

        Exec("UPDATE users SET is_active = $a, updated_at = $t WHERE id = $id",
            ("$a", active ? 1 : 0), ("$t", Now()), ("$id", userId.ToString()));
    }

    public void ChangePassword(Guid userId, string currentPassword, string newPassword)
    {
        var row = Find(userId: userId) ?? throw new InvalidOperationException("Usuário não encontrado.");
        if (!hasher.Verify(currentPassword, row.Hash))
        {
            throw new UnauthorizedAccessException("Senha atual incorreta.");
        }

        Exec("UPDATE users SET password_hash = $h, updated_at = $t WHERE id = $id",
            ("$h", hasher.Hash(newPassword)), ("$t", Now()), ("$id", userId.ToString()));
    }

    public LoginResult Login(string username, string password)
    {
        var row = Find(username: username);
        var now = _clock.GetUtcNow();
        if (row is null || !row.Account.IsActive)
        {
            _ = hasher.Verify(password ?? string.Empty, _dummyHash.Value); // tempo constante aproximado
            return new LoginResult(LoginOutcome.InvalidCredentials, null, null);
        }

        if (row.LockedUntil is { } until && until > now)
        {
            return new LoginResult(LoginOutcome.LockedOut, null, until);
        }

        if (!hasher.Verify(password ?? string.Empty, row.Hash))
        {
            var attempts = row.FailedAttempts + 1;
            DateTimeOffset? lockUntil = null;
            if (attempts >= MaxAttemptsBeforeLock)
            {
                var factor = Math.Pow(2, attempts - MaxAttemptsBeforeLock);
                var span = TimeSpan.FromTicks((long)Math.Min(MaxLock.Ticks, BaseLock.Ticks * factor));
                lockUntil = now + span;
            }

            Exec("UPDATE users SET failed_attempts = $f, lockout_until = $l WHERE id = $id",
                ("$f", attempts), ("$l", lockUntil?.ToString("O", CultureInfo.InvariantCulture)), ("$id", row.Account.Id.ToString()));
            return lockUntil is null
                ? new LoginResult(LoginOutcome.InvalidCredentials, null, null)
                : new LoginResult(LoginOutcome.LockedOut, null, lockUntil);
        }

        Exec("UPDATE users SET failed_attempts = 0, lockout_until = NULL WHERE id = $id", ("$id", row.Account.Id.ToString()));
        if (hasher.NeedsRehash(row.Hash))
        {
            Exec("UPDATE users SET password_hash = $h WHERE id = $id", ("$h", hasher.Hash(password!)), ("$id", row.Account.Id.ToString()));
        }

        return new LoginResult(LoginOutcome.Success, row.Account, null);
    }

    public IReadOnlyList<UserAccount> ListUsers(UserAccount actingAdmin)
    {
        RequireAdmin(actingAdmin);
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, username, display_name, role, is_active FROM users ORDER BY username";
        using var r = cmd.ExecuteReader();
        var list = new List<UserAccount>();
        while (r.Read())
        {
            list.Add(new UserAccount(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
                Enum.Parse<UserRole>(r.GetString(3)), r.GetInt64(4) == 1));
        }

        return list;
    }

    private UserAccount Insert(string username, string displayName, string password, UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var hash = hasher.Hash(password);
        var id = Guid.NewGuid();
        var now = Now();
        try
        {
            Exec("""
                INSERT INTO users(id, username, display_name, role, password_hash, is_active, created_at, updated_at)
                VALUES ($id, $u, $d, $r, $h, 1, $t, $t)
                """,
                ("$id", id.ToString()), ("$u", username.Trim()), ("$d", displayName.Trim()),
                ("$r", role.ToString()), ("$h", hash), ("$t", now));
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Já existe um usuário com esse nome.", ex);
        }

        return new UserAccount(id, username.Trim(), displayName.Trim(), role, true);
    }

    private static void RequireAdmin(UserAccount acting)
    {
        ArgumentNullException.ThrowIfNull(acting);
        if (acting.Role != UserRole.Administrador)
        {
            throw new UnauthorizedAccessException("Somente administradores podem gerenciar usuários.");
        }
    }

    private sealed record Row(UserAccount Account, string Hash, int FailedAttempts, DateTimeOffset? LockedUntil);

    private Row? Find(string? username = null, Guid? userId = null)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, username, display_name, role, is_active, password_hash, failed_attempts, lockout_until FROM users WHERE "
                          + (userId is null ? "username = $k" : "id = $k");
        cmd.Parameters.AddWithValue("$k", userId?.ToString() ?? username?.Trim() ?? string.Empty);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        var account = new UserAccount(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
            Enum.Parse<UserRole>(r.GetString(3)), r.GetInt64(4) == 1);
        DateTimeOffset? locked = r.IsDBNull(7) ? null : DateTimeOffset.Parse(r.GetString(7), CultureInfo.InvariantCulture);
        return new Row(account, r.GetString(5), r.GetInt32(6), locked);
    }

    private string Now() => _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}
