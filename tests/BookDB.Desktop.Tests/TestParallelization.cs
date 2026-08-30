using Xunit;

// AppHost.ApplyCultureBootstrap sets the process-wide default UI culture, and the tests that cover it can
// only restore it after the fact. Anything reading a localized resource in a parallel collection could
// straddle that window and read one string in Swedish and the next in English. The assembly runs in a
// couple of seconds, so serializing it is cheaper than making every resource-reading test culture-proof.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
