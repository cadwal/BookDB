using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;

namespace BookDB.Desktop.Helpers;

internal static class UIThreadHelper
{
    internal static void PostAsync(Func<Task> work, string warningContext)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try { await work(); }
            catch (Exception ex) { Log.Error(ex, "Failed to {Context}", warningContext); }
        });
    }
}
