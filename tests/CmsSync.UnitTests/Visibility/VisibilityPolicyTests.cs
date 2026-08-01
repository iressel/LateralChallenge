using CmsSync.Domain.Entities;
using CmsSync.Domain.Visibility;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Visibility;

public sealed class VisibilityPolicyTests
{
    [Theory]
    [InlineData(CmsPublicationStatus.Published, false, true)]
    [InlineData(CmsPublicationStatus.Published, true, false)]
    [InlineData(CmsPublicationStatus.Unpublished, false, false)]
    [InlineData(CmsPublicationStatus.Unpublished, true, false)]
    public void AC035NormalConsumerSeesOnlyPublishedAndAdministrativelyEnabledEntities(
        CmsPublicationStatus publicationStatus,
        bool administrativeDisabled,
        bool expectedVisible)
    {
        var entity = CmsStateTestData.Active(
            status: publicationStatus,
            administrativeDisabled: administrativeDisabled);

        var visible = EntityVisibilityPolicy.IsVisibleToNormalConsumer(entity);

        Assert.Equal(expectedVisible, visible);
    }

    [Theory]
    [InlineData(CmsPublicationStatus.Published, false)]
    [InlineData(CmsPublicationStatus.Published, true)]
    [InlineData(CmsPublicationStatus.Unpublished, false)]
    [InlineData(CmsPublicationStatus.Unpublished, true)]
    public void AdministratorSeesEveryActiveEntityRegardlessOfCmsOrAdministrativeState(
        CmsPublicationStatus publicationStatus,
        bool administrativeDisabled)
    {
        var entity = CmsStateTestData.Active(
            status: publicationStatus,
            administrativeDisabled: administrativeDisabled);

        Assert.True(EntityVisibilityPolicy.IsVisibleToAdministrator(entity));
    }

    [Fact]
    public void NullOrDeletedEntityIsNotVisibleToAnyAudience()
    {
        Assert.False(EntityVisibilityPolicy.IsVisibleToNormalConsumer(entity: null));
        Assert.False(EntityVisibilityPolicy.IsVisibleToAdministrator(entity: null));
    }
}
