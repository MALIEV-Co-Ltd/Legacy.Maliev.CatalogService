using Legacy.Maliev.CatalogService.Api.Controllers;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.CatalogService.Tests.Controllers;

public sealed class CountryOriginalNullControllerTests
{
    [Fact]
    public async Task CreateCountry_NullBody_ReturnsEmptyBadRequestBeforeServiceAccess()
    {
        var service = new Mock<ICatalogService>(MockBehavior.Strict);
        var controller = new CountriesController(service.Object);

        var result = await controller.CreateCountryAsync(null!, CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task UpdateCountry_NullBody_ReturnsEmptyBadRequestBeforeLookup(int id)
    {
        var service = new Mock<ICatalogService>(MockBehavior.Strict);
        var controller = new CountriesController(service.Object);

        var result = await controller.UpdateCountryAsync(id, null!, CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateCountry_ZeroId_PreservesEmptyBadRequestWithoutServiceAccess()
    {
        var service = new Mock<ICatalogService>(MockBehavior.Strict);
        var controller = new CountriesController(service.Object);

        var result = await controller.UpdateCountryAsync(0, new UpsertCountryRequest("Candidate"), CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestResult>(result).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateCountry_NonNullBody_PreservesRequestTokenAndResult(bool found)
    {
        const int id = 101;
        var request = new UpsertCountryRequest("Candidate");
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<ICatalogService>(MockBehavior.Strict);
        service.Setup(value => value.UpdateCountryAsync(id, request, cancellation.Token)).ReturnsAsync(found);
        var controller = new CountriesController(service.Object);

        var result = await controller.UpdateCountryAsync(id, request, cancellation.Token);

        if (found) Assert.IsType<NoContentResult>(result);
        else Assert.IsType<NotFoundResult>(result);
        service.Verify(value => value.UpdateCountryAsync(id,
            It.Is<UpsertCountryRequest>(actual => ReferenceEquals(actual, request)), cancellation.Token), Times.Once);
        service.VerifyNoOtherCalls();
    }
}
