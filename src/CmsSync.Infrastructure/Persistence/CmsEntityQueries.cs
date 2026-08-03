using System.Diagnostics.CodeAnalysis;
using CmsSync.Application.Abstractions;
using CmsSync.Application.EntityQueries;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace CmsSync.Infrastructure.Persistence;

public sealed class CmsEntityQueries : ICmsEntityQueries
{
    private const string PublishedStatus = "Published";

    private readonly CmsReadDbContext _readDbContext;

    public CmsEntityQueries(CmsReadDbContext readDbContext)
    {
        _readDbContext = readDbContext;
    }

    [SuppressMessage(
        "Globalization",
        "CA1309:Use ordinal string comparison",
        Justification = "EF Core translates this comparison under the explicit binary SQL collation.")]
    public async Task<CmsEntityReadPage> ListAsync(
        CmsEntityListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PageSize is < CmsEntityQueryLimits.MinimumPageSize or > CmsEntityQueryLimits.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.PageSize,
                "The entity query page size is outside the supported range.");
        }

        var candidates = ApplyVisibility(_readDbContext.CmsEntities.AsNoTracking(), query.Visibility);

        if (query.AfterEntityId is not null)
        {
            candidates = candidates.Where(entity =>
                string.Compare(
                    EF.Functions.Collate(
                        entity.EntityId,
                        PersistenceModelConstants.CaseSensitiveCollation),
                    query.AfterEntityId) > 0);
        }

        var projectedItems = await candidates
            .OrderBy(entity => EF.Functions.Collate(
                entity.EntityId,
                PersistenceModelConstants.CaseSensitiveCollation))
            .Select(ProjectEntity())
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);

        var hasAnotherPage = projectedItems.Count > query.PageSize;

        if (hasAnotherPage)
        {
            projectedItems.RemoveAt(projectedItems.Count - 1);
        }

        var nextCursor = hasAnotherPage
            ? projectedItems[^1].EntityId
            : null;

        return new CmsEntityReadPage(projectedItems, nextCursor);
    }

    public async Task<CmsEntityReadProjection?> FindByIdAsync(
        CmsEntityDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrEmpty(query.EntityId))
        {
            throw new ArgumentException("The entity identifier is required.", nameof(query));
        }

        var candidates = ApplyVisibility(_readDbContext.CmsEntities.AsNoTracking(), query.Visibility);

        return await candidates
            .Where(entity =>
                EF.Functions.Collate(
                    entity.EntityId,
                    PersistenceModelConstants.CaseSensitiveCollation) == query.EntityId)
            .Select(ProjectEntity())
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<CmsEntityReadModel> ApplyVisibility(
        IQueryable<CmsEntityReadModel> candidates,
        CmsEntityQueryVisibility visibility)
    {
        return visibility switch
        {
            CmsEntityQueryVisibility.Consumer => candidates.Where(entity =>
                entity.CmsPublicationStatus == PublishedStatus &&
                !entity.AdministrativeDisabled),
            CmsEntityQueryVisibility.Administrator => candidates,
            _ => throw new ArgumentOutOfRangeException(
                nameof(visibility),
                visibility,
                "The entity query visibility is unsupported."),
        };
    }

    private static System.Linq.Expressions.Expression<Func<CmsEntityReadModel, CmsEntityReadProjection>>
        ProjectEntity()
    {
        return entity => new CmsEntityReadProjection(
            entity.EntityId,
            entity.Generation,
            entity.LatestVersion,
            entity.Payload,
            entity.CmsPublicationStatus,
            entity.CurrentVersionOccurredAtUtc,
            entity.EntityEventHighWatermarkUtc,
            entity.AdministrativeDisabled);
    }
}
