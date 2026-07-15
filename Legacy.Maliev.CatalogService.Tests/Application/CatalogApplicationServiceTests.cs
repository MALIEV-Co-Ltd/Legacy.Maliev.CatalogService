using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Legacy.Maliev.CatalogService.Application.Services;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Legacy.Maliev.CatalogService.Tests.Application;

public sealed class CatalogApplicationServiceTests
{
    [Fact]
    public async Task GetCountriesAsync_UnorderedRecords_ReturnsNameOrder()
    {
        var repository = new Mock<ICatalogRepository>();
        repository.Setup(value => value.ListAsync<Country>(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Country { Id = 2, Name = "Thailand" }, new Country { Id = 1, Name = "Japan" }]);
        var service = new CatalogApplicationService(repository.Object, TimeProvider.System);

        var countries = await service.GetCountriesAsync(CancellationToken.None);

        Assert.Equal(["Japan", "Thailand"], countries.Select(country => country.Name));
    }

    [Fact]
    public async Task GetCountriesAsync_CacheHit_DoesNotQueryRepository()
    {
        var repository = new Mock<ICatalogRepository>(MockBehavior.Strict);
        var cache = new Mock<ICatalogCache>();
        cache.Setup(value => value.GetAsync<Country[]>("countries:all:v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Country { Id = 1, Name = "Thailand" }]);
        var service = new CatalogApplicationService(repository.Object, TimeProvider.System, cache.Object);

        var countries = await service.GetCountriesAsync(CancellationToken.None);

        Assert.Equal("Thailand", Assert.Single(countries).Name);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateCountryAsync_ValidRequest_SetsUtcAuditTimestamps()
    {
        var repository = new Mock<ICatalogRepository>();
        Country? captured = null;
        repository.Setup(value => value.AddAsync(It.IsAny<Country>(), It.IsAny<CancellationToken>()))
            .Callback<Country, CancellationToken>((country, _) => captured = country)
            .Returns(Task.CompletedTask);
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var service = new CatalogApplicationService(repository.Object, new FakeTimeProvider(now));

        await service.CreateCountryAsync(new("Thailand", "Asia", "+66", "TH", "THA"), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(now.UtcDateTime, captured.CreatedDate);
        Assert.Equal(now.UtcDateTime, captured.ModifiedDate);
    }

    [Fact]
    public async Task GetMaterialsAsync_SearchSortAndPage_PreservesLegacyBehavior()
    {
        var repository = new Mock<ICatalogRepository>();
        repository.Setup(value => value.ListMaterialsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Material { Id = 1, Name = "Aluminium 6061", MaterialNumber = "6061", MaterialGroup = new MaterialGroup { Name = "Aluminium" } },
            new Material { Id = 2, Name = "Steel 304", MaterialNumber = "304", MaterialGroup = new MaterialGroup { Name = "Steel" } },
            new Material { Id = 3, Name = "Steel 316", MaterialNumber = "316", MaterialGroup = new MaterialGroup { Name = "Steel" } },
        ]);
        var service = new CatalogApplicationService(repository.Object, TimeProvider.System);

        var page = await service.GetMaterialsAsync(MaterialSortType.MaterialNumber_Descending, "steel", 1, 1, CancellationToken.None);

        Assert.Equal(2, page.TotalRecords);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.False(page.HasPreviousPage);
        Assert.Single(page.Items);
        Assert.Equal("316", page.Items[0].MaterialNumber);
    }

    [Fact]
    public async Task UpdateMaterialGroupAsync_ExistingGroup_InvalidatesGroupAndEmbeddedMaterialCaches()
    {
        var repository = new Mock<ICatalogRepository>();
        repository.Setup(value => value.FindAsync<MaterialGroup>(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MaterialGroup { Id = 7, Name = "Old" });
        repository.Setup(value => value.UpdateAsync(It.IsAny<MaterialGroup>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var cache = new Mock<ICatalogCache>();
        cache.Setup(value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new CatalogApplicationService(repository.Object, TimeProvider.System, cache.Object);

        var updated = await service.UpdateMaterialGroupAsync(7, new("Steel", "Ferrous"), CancellationToken.None);

        Assert.True(updated);
        cache.Verify(value => value.RemoveAsync("material-groups:all:v1", It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(value => value.RemoveAsync("materials:all:v1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
