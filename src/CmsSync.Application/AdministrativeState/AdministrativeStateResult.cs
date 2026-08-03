namespace CmsSync.Application.AdministrativeState;

public sealed record AdministrativeStateResult(
    string EntityId,
    bool AdministrativeDisabled,
    DateTime? AdministrativeStateChangedAtUtc,
    string? AdministrativeStateChangedBy);
