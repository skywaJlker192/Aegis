using Aegis.Dtos;
using Aegis.Models;

namespace Aegis.Services;

/// <summary>Преобразование сущностей в DTO (наружу сущности не отдаём).</summary>
public static class AnomalyMapper
{
    public static AnomalyDto ToDto(this Anomaly a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Coordinates = new CoordinatesDto { Latitude = a.Latitude, Longitude = a.Longitude },
        ThreatLevel = a.ThreatLevel,
        Status = a.Status
    };

    public static AnomalyV2Dto ToV2Dto(this Anomaly a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Coordinates = new CoordinatesDto { Latitude = a.Latitude, Longitude = a.Longitude },
        ThreatLevel = a.ThreatLevel,
        Status = a.Status,
        // Последние 5 замеров, новые первыми
        TelemetryLogs = a.Logs
            .OrderByDescending(l => l.RecordedAt)
            .Take(5)
            .Select(l => new TelemetryLogDto
            {
                Source = l.Source,
                Message = l.Message,
                Reading = l.Reading,
                RecordedAt = l.RecordedAt
            })
            .ToList()
    };
}
