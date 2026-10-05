using System.Collections.Immutable;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator;

/// <summary>
/// Names a consumer and the dependencies it declared, so DM0036 can judge them against a compilation
/// read back later.
/// </summary>
/// <param name="MetadataName">The metadata name of the consuming type.</param>
/// <param name="Dependencies">The validated dependencies of the consumer, in declaration order.</param>
/// <remarks>
/// Neither member refers to a syntax tree, and that is the whole point: the hint survives being
/// cached across compilations, and the report built from it names a location in the compilation that
/// asked for it.
/// </remarks>
internal readonly record struct ConsumerUsageHint(
    string MetadataName,
    ImmutableArray<ServiceReferenceModel> Dependencies);