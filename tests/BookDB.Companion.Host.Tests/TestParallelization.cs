using Xunit;

// Each host test starts a real Kestrel listener on loopback, and the streaming tests hold a server call
// open until the client stops reading. Dozens of those at once contend for sockets and shutdown draining;
// the suite runs in a few seconds, so serializing it is cheaper than making every test defend itself.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
