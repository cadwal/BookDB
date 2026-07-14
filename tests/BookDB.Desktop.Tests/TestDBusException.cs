// Tmds.DBus's own exception types have no public constructors; a stand-in declared in its namespace
// exercises the type-name match AppHost.IsBenignDesktopDBusError relies on.
namespace Tmds.DBus.Protocol;

internal sealed class TestDBusException : System.Exception
{
    public TestDBusException(string message) : base(message)
    {
    }
}
