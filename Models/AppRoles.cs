namespace Aegis.Models;

/// <summary>Названия ролей приложения (чтобы не разбрасывать строки по коду).</summary>
public static class AppRoles
{
    public const string Agent = "Agent";
    public const string Moderator = "Moderator";
    public const string Admin = "Admin";

    /// <summary>Для атрибута [Authorize(Roles = ...)]: любая из двух ролей.</summary>
    public const string ModeratorOrAdmin = Moderator + "," + Admin;

    public static readonly string[] All = { Agent, Moderator, Admin };
}
