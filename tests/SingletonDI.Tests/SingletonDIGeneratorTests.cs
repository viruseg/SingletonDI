using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using SingletonDI.Generator;
using SingletonDI.Generator.Models;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for the SingletonDI source generator.
/// </summary>
public class SingletonDIGeneratorTests
{
    [Fact]
    public void Generator_CanBeInstantiated()
    {
        // Arrange
        var generator = new SingletonDIGenerator();

        // Assert
        Assert.NotNull(generator);
    }

    [Fact]
    public void ProviderModel_CanBeCreated()
    {
        // Arrange & Act
        var model = new ProviderModel
        {
            FullyQualifiedName = "Test.Provider",
            ShortName = "Provider",
            Namespace = "Test",
            HasInitializeAsyncMethod = true,
            IsDisposable = false,
            Dependencies = ImmutableArray<string>.Empty
        };

        // Assert
        Assert.Equal("Test.Provider", model.FullyQualifiedName);
        Assert.Equal("Provider", model.ShortName);
        Assert.True(model.HasInitializeAsyncMethod);
    }

    [Fact]
    public void ProviderModel_WithoutInitializeAsync_CanBeCreated()
    {
        // Arrange & Act
        var model = new ProviderModel
        {
            FullyQualifiedName = "Test.Provider",
            ShortName = "Provider",
            Namespace = "Test",
            HasInitializeAsyncMethod = false,
            IsDisposable = false,
            Dependencies = ImmutableArray<string>.Empty
        };

        // Assert
        Assert.False(model.HasInitializeAsyncMethod);
    }

    [Fact]
    public void ConsumerModel_CanBeCreated()
    {
        // Arrange & Act
        var model = new ConsumerModel
        {
            FullyQualifiedName = "Test.Consumer",
            ShortName = "Consumer",
            Namespace = "Test",
            IsPartial = true,
            Dependencies = ["Test.Provider"]
        };

        // Assert
        Assert.Equal("Test.Consumer", model.FullyQualifiedName);
        Assert.True(model.IsPartial);
        Assert.Single(model.Dependencies);
    }

    [Fact]
    public void CombinedModel_HasCircularDependency_ReturnsTrue()
    {
        // Arrange - circular dependency exists when TopologicalOrder is empty but Providers is not
        var provider = new ProviderModel
        {
            FullyQualifiedName = "Test.Provider",
            ShortName = "Provider",
            Namespace = "Test",
            HasInitializeAsyncMethod = false,
            IsDisposable = false,
            Dependencies = ImmutableArray<string>.Empty
        };

        var model = new CombinedModel
        {
            Providers = [provider],
            Consumers = ImmutableArray<ConsumerModel>.Empty,
            TopologicalOrder = ImmutableArray<string>.Empty // Empty indicates cycle
        };

        // Assert
        Assert.True(model.HasCircularDependency);
    }

    [Fact]
    public void CombinedModel_NoCircularDependency_ReturnsFalse()
    {
        // Arrange - no circular dependency when both are empty (no providers)
        var model = new CombinedModel
        {
            Providers = ImmutableArray<ProviderModel>.Empty,
            Consumers = ImmutableArray<ConsumerModel>.Empty,
            TopologicalOrder = ImmutableArray<string>.Empty
        };

        // Assert
        Assert.False(model.HasCircularDependency);
    }
}
