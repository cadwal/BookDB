namespace System.Runtime.CompilerServices;

/// <summary>
/// The marker the compiler needs for <c>init</c> accessors and records. It ships in .NET 5 and later but
/// not in netstandard2.0, which this assembly targets so the mobile heads can share it; declaring it here
/// is the sanctioned workaround. Internal, so it cannot collide with a consumer's own copy.
/// </summary>
internal static class IsExternalInit
{
}
