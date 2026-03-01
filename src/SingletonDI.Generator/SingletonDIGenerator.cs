using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Generator.Emitters;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;
using SingletonDI.Generator.Validators;

namespace SingletonDI.Generator;

/// <summary>
/// Incremental Source Generator for SingletonDI dependency injection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public partial class SingletonDIGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // First, register the ExceptionHelper to be emitted always
        context.RegisterSourceOutput(context.CompilationProvider, (spc, compilation) =>
        {
            // Always emit ExceptionHelper
            spc.AddSource("ExceptionHelper.g.cs", SourceText.From(ExceptionHelperEmitter.Generate(), encoding: System.Text.Encoding.UTF8));
        });

        // Get provider types - types with [SingletonDIProvide] attribute
        var providers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SingletonDI.Attributes.SingletonDIProvideAttribute",
                predicate: (node, token) => node is TypeDeclarationSyntax,
                transform: (context, token) =>
                {
                    var typeDecl = (TypeDeclarationSyntax)context.TargetNode;
                    var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, token);

                    if (typeSymbol == null) return null;

                    return typeDecl;
                })
            .Where(t => t != null)
            .Select((t, _) => t!);

        // Get consumer types - types with [SingletonDIConsume] attribute
        var consumers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SingletonDI.Attributes.SingletonDIConsumeAttribute",
                predicate: (node, token) => node is TypeDeclarationSyntax,
                transform: (context, token) =>
                {
                    var typeDecl = (TypeDeclarationSyntax)context.TargetNode;
                    var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, token);

                    if (typeSymbol == null) return null;

                    return typeDecl;
                })
            .Where(t => t != null)
            .Select((t, _) => t!);

        // Combine providers and consumers with compilation for validation
        var combined = providers
            .Collect()
            .Combine(consumers.Collect())
            .Combine(context.CompilationProvider)
            .Select((data, cancellationToken) =>
            {
                var ((providerList, consumerList), compilation) = data;

                // Get all provider FQNs for validation
                var builder = ImmutableHashSet<string>.Empty.ToBuilder();
                foreach (var typeDecl in providerList)
                {
                    var semanticModel = compilation.GetSemanticModel(typeDecl.SyntaxTree);
                    var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                    if (typeSymbol != null)
                    {
                        builder.Add(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
                var providerFQNs = builder.ToImmutable();

                return (Providers: providerList, Consumers: consumerList, ProviderFQNs: providerFQNs, Compilation: compilation);
            });

        // Generate output when combined data changes
        context.RegisterSourceOutput(combined, (spc, data) =>
        {
            if (data.Providers.IsEmpty && data.Consumers.IsEmpty)
            {
                return;
            }

            // Convert TypeDeclarationSyntax to ProviderModel
            var providerModels = new List<ProviderModel>();
            foreach (var typeDecl in data.Providers)
            {
                var semanticModel = data.Compilation.GetSemanticModel(typeDecl.SyntaxTree);
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (typeSymbol != null)
                {
                    var model = ProviderValidator.Validate(typeDecl, typeSymbol, new HashSet<string>(data.ProviderFQNs), spc.ReportDiagnostic);
                    if (model.HasValue)
                    {
                        providerModels.Add(model.Value);
                    }
                }
            }

            // Run topological sort by levels
            var sortResult = TopologicalSorter.SortByLevels(providerModels.ToImmutableArray());

            // If there's a cycle, emit diagnostic
            if (sortResult.HasCycle)
            {
                var cycleMessage = string.Join(" -> ", sortResult.Cycle);
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CircularDependency,
                    Location.None,
                    cycleMessage));
                return;
            }

            // Resolve property names
            var propertyNames = PropertyNameResolver.ResolvePropertyNames(providerModels);

            // Generate container
            if (providerModels.Count > 0)
            {
                var containerSource = ContainerEmitter.Generate(
                    providerModels.ToImmutableArray(),
                    sortResult.Levels,
                    propertyNames);

                spc.AddSource("SingletonContainer.g.cs", SourceText.From(containerSource, encoding: System.Text.Encoding.UTF8));

                // Generate SingletonInitializer
                var initializerSource = SingletonInitializerEmitter.Generate();
                spc.AddSource("SingletonInitializer.g.cs", SourceText.From(initializerSource, encoding: System.Text.Encoding.UTF8));
            }

            // Generate consumer partial classes
            if (data.Consumers.Length > 0)
            {
                var consumerModels = new List<ConsumerModel>();
                foreach (var typeDecl in data.Consumers)
                {
                    var semanticModel = data.Compilation.GetSemanticModel(typeDecl.SyntaxTree);
                    var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                    if (typeSymbol != null)
                    {
                        var model = ConsumerValidator.Validate(typeDecl, typeSymbol, new HashSet<string>(data.ProviderFQNs), spc.ReportDiagnostic);
                        if (model.HasValue)
                        {
                            consumerModels.Add(model.Value);
                        }
                    }
                }

                var consumerSources = ConsumerEmitter.Generate(consumerModels, propertyNames);

                foreach (var p in consumerSources)
                {
                    var fileName = p.Key;
                    var source = p.Value;

                    spc.AddSource(fileName, SourceText.From(source, encoding: System.Text.Encoding.UTF8));
                }
            }
        });
    }
}
