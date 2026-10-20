using System.Collections.Concurrent;

namespace Aegis.Services;

/// <summary>
/// Хранит, кто сейчас «на связи» (подключён к SignalR-хабу).
/// Регистрируется как singleton: счётчик общий для всех подключений.
/// </summary>
public class PresenceTracker
{
    // connectionId -> userId (у одного пользователя может быть несколько вкладок)
    private readonly ConcurrentDictionary<string, string> _connections = new();

    /// <summary>Сколько разных агентов сейчас онлайн.</summary>
    public int OnlineCount => _connections.Values.Distinct().Count();

    public int Connect(string connectionId, string userId)
    {
        _connections[connectionId] = userId;
        return OnlineCount;
    }

    public int Disconnect(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
        return OnlineCount;
    }

    /// <summary>Есть ли у пользователя ещё хотя бы одно активное подключение.</summary>
    public bool IsOnline(string userId) => _connections.Values.Contains(userId);
}
