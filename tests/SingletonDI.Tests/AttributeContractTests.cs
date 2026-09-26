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
    public void PropertyName_HasNoSetter_SoItCannotBeANamedAttributeArgument()
    {
        // A named attribute argument must target a field without readonly or an open non-static
        // property with a setter. A get-only PropertyName makes the named form a CS0617, which is
        // why the generator's named-argument branch is unreachable through the shipped attribute.
        var property = typeof(SingletonDIProvideAttribute)
            .GetProperty(nameof(SingletonDIProvideAttribute.PropertyName))!;

        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
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
