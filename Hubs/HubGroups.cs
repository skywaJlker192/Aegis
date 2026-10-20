namespace Aegis.Hubs;

/// <summary>Имена SignalR-групп.</summary>
public static class HubGroups
{
    /// <summary>Группа модераторов и админов: они получают телеметрию по всем экспедициям.</summary>
    public const string Moderators = "moderators";

    /// <summary>Группа «HQ» (штаб): модераторы и админы. Получают положения агентов и уведомления о сканированиях.</summary>
    public const string Hq = "HQ";

    /// <summary>Группа конкретной экспедиции (участники + руководитель).</summary>
    public static string Expedition(int expeditionId) => $"expedition-{expeditionId}";

    /// <summary>Группа чата экспедиции (хаб ExpeditionCommsHub): участники + координаторы.</summary>
    public static string Comms(int expeditionId) => $"comms-{expeditionId}";
}
