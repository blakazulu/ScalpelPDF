// Polyfill required for 'record' types on .NET Framework 4.8.
// The compiler emits init-only setters which reference this type;
// it is not present in net48 BCL, so we declare it ourselves.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
