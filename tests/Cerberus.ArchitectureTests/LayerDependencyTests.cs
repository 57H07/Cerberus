using NetArchTest.Rules;

namespace Cerberus.ArchitectureTests;

public class LayerDependencyTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(Cerberus.Domain.Common.Entity).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(Cerberus.Application.DependencyInjection.ServiceCollectionExtensions).Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = typeof(Cerberus.Infrastructure.DependencyInjection.ServiceCollectionExtensions).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOnOtherLayersOrFrameworks()
    {
        var result = Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "Cerberus.Application", "Cerberus.Infrastructure", "Cerberus.Web",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "OpenIddict", "Microsoft.Extensions").GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ShouldNotDependOnInfrastructureWebOrProtocolLibrary()
    {
        var result = Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(
            "Cerberus.Infrastructure", "Cerberus.Web", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "OpenIddict").GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOnWeb()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("Cerberus.Web").GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controllers_ShouldNotUseRepositoriesOrDbContextDirectly()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().HaveNameEndingWith("Controller")
            .ShouldNot().HaveDependencyOnAny("Cerberus.Application.Interfaces.Repositories", "Cerberus.Infrastructure.Data", "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
