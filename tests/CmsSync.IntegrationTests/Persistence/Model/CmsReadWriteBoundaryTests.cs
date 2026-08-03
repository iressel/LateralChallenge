using CmsSync.Application.Abstractions;
using CmsSync.Application.EntityQueries;
using CmsSync.Domain.Entities;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CmsSync.IntegrationTests.Persistence.Model;

[Trait("Category", "Security")]
public sealed class CmsReadWriteBoundaryTests
{
    [Fact]
    public void WriteContextExposesAllFourWriteSets()
    {
        using var context = PersistenceModelTestContext.CreateWriteContext();

        Assert.NotNull(context.CmsEntities);
        Assert.NotNull(context.CmsEntityRevisions);
        Assert.NotNull(context.CmsDeletionTombstones);
        Assert.NotNull(context.CmsEventProcessingLogs);
    }

    [Fact]
    public void ReadContextMapsOnlyActiveEntitiesAndDoesNotOwnMigrations()
    {
        using var readContext = PersistenceModelTestContext.CreateReadContext();
        using var writeContext = PersistenceModelTestContext.CreateWriteContext();
        var readModel = PersistenceModelTestContext.GetDesignTimeModel(readContext);
        var writeModel = PersistenceModelTestContext.GetDesignTimeModel(writeContext);
        var readEntityType = Assert.Single(readModel.GetEntityTypes());
        var writeEntityType = PersistenceModelTestContext.GetRequiredEntityType<CmsEntity>(writeModel);
        var readTable = StoreObjectIdentifier.Table(
            PersistenceModelConstants.CmsEntitiesTable,
            readEntityType.GetSchema());
        var writeTable = StoreObjectIdentifier.Table(
            PersistenceModelConstants.CmsEntitiesTable,
            writeEntityType.GetSchema());

        Assert.Equal(typeof(CmsEntityReadModel), readEntityType.ClrType);
        Assert.Equal(PersistenceModelConstants.CmsEntitiesTable, readEntityType.GetTableName());
        Assert.True(readEntityType.IsTableExcludedFromMigrations(readTable));
        Assert.False(writeEntityType.IsTableExcludedFromMigrations(writeTable));

        var readPublicationStatus = PersistenceModelTestContext.GetRequiredProperty(
            readEntityType,
            nameof(CmsEntityReadModel.CmsPublicationStatus));
        var writePublicationStatus = PersistenceModelTestContext.GetRequiredProperty(
            writeEntityType,
            nameof(CmsEntity.CmsPublicationStatus));
        Assert.Equal(PersistenceModelConstants.CaseSensitiveCollation, readPublicationStatus.GetCollation());
        Assert.Equal(writePublicationStatus.GetCollation(), readPublicationStatus.GetCollation());
    }

    [Fact]
    public void ReadContextDefaultsToNoTrackingForOrdinaryQueries()
    {
        using var context = PersistenceModelTestContext.CreateReadContext();

        _ = context.CmsEntities.Where(entity => entity.EntityId == "metadata-only");

        Assert.Equal(QueryTrackingBehavior.NoTracking, context.ChangeTracker.QueryTrackingBehavior);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ReadContextRejectsEverySaveChangesOverload()
    {
        await using var context = PersistenceModelTestContext.CreateReadContext();

        var first = Assert.Throws<NotSupportedException>(() => context.SaveChanges());
        var second = Assert.Throws<NotSupportedException>(() => context.SaveChanges(acceptAllChangesOnSuccess: true));
        var third = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.SaveChangesAsync(CancellationToken.None));
        var fourth = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.SaveChangesAsync(acceptAllChangesOnSuccess: true, CancellationToken.None));

        Assert.Equal("CmsReadDbContext is read-only and cannot save changes.", first.Message);
        Assert.Equal(first.Message, second.Message);
        Assert.Equal(first.Message, third.Message);
        Assert.Equal(first.Message, fourth.Message);
    }

    [Fact]
    public void DomainAndApplicationRemainIndependentFromEntityFrameworkCore()
    {
        var forbiddenReferences = new[]
        {
            typeof(ActiveCmsEntitySnapshot).Assembly,
            typeof(ICmsEntityQueries).Assembly,
        }
        .SelectMany(assembly => assembly.GetReferencedAssemblies())
        .Where(reference =>
            (reference.Name ?? string.Empty).StartsWith(
                "Microsoft.EntityFrameworkCore",
                StringComparison.Ordinal))
        .ToArray();

        Assert.Empty(forbiddenReferences);
        Assert.DoesNotContain(
            typeof(ICmsEntityQueries).GetMethods().SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)),
            type => type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
        Assert.Equal(typeof(CmsEntityReadProjection), typeof(CmsEntityReadPage).GetProperty(nameof(CmsEntityReadPage.Items))!
            .PropertyType.GenericTypeArguments.Single());
    }

    [Fact]
    public void InfrastructureDoesNotReferenceApiAndNoEfInMemoryPackageIsDeclared()
    {
        var infrastructureReferences = typeof(CmsWriteDbContext).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var projectFiles = Directory.GetFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories);

        Assert.DoesNotContain("CmsSync.Api", infrastructureReferences);
        Assert.All(
            projectFiles,
            projectFile => Assert.DoesNotContain(
                "Microsoft.EntityFrameworkCore.InMemory",
                File.ReadAllText(projectFile),
                StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot(string startingPath)
    {
        for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LateralChallenge.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LateralChallenge repository root.");
    }
}
