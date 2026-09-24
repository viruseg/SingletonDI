using SingletonDI.Attributes;
using Xunit;

namespace SingletonDI.Tests;

public sealed class AttributeContractTests
{
    [Fact]
    public void ServiceType_CanBeSetWithoutChangingPropertyName()
    {
        var attribute = new SingletonDIProvideAttribute("_db")
        {
            ServiceType = typeof(IDatabaseService)
        };

        Assert.Equal("_db", attribute.PropertyName);
        Assert.Equal(typeof(IDatabaseService), attribute.ServiceType);
    }

    [Fact]
    public void ProviderModuleMarker_TargetsAssembly()
    {
        var usage = typeof(SingletonDIProviderModuleAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
    }

    private interface IDatabaseService
    {
    }
}
