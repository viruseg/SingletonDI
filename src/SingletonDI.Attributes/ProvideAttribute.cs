namespace SingletonDI.Attributes;

/// <summary>
/// Attribute to mark a class as a singleton provider.
/// Only classes (not structs or record structs) can be providers.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProvideAttribute : Attribute
{
}
