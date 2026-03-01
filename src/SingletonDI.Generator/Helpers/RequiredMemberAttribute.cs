namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Event | AttributeTargets.Constructor)]
public sealed class RequiredMemberAttribute : Attribute
{
    public RequiredMemberAttribute() { }
}