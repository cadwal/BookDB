using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using BookDB.Companion.Host;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// Mints the server certificate for the machine the suite is actually running on, and records what that
/// machine is called. A GitHub macOS runner reports an empty <see cref="Environment.MachineName"/>, which
/// used to make the certificate unmintable and every companion test fail with a stack trace that named
/// neither the machine nor the name — this reports both, so the next environment-shaped failure can be
/// read off the log without reproducing it.
/// </summary>
public sealed class HostEnvironmentReportTests
{
    private readonly ITestOutputHelper _output;

    public HostEnvironmentReportTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheServerCertificateCanBeMintedForThisMachine()
    {
        var machineName = Environment.MachineName;
        var subjectName = $"BookDB-{machineName}";

        var report =
            $"machine name: '{machineName}' (length {machineName.Length}); " +
            $"certificate subject: '{subjectName}'; " +
            $"OS: {RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}";
        _output.WriteLine(report);
        // Also on xunit's diagnostic channel: test output is only printed for failures, and the point of
        // this record is to be readable from a run that passed.
        TestContext.Current.SendDiagnosticMessage(report);

        using var certificate = CertificateFactory.CreateServerCertificate(subjectName);

        var san = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
        Assert.NotNull(san);
        Assert.Contains("localhost", san!.Format(false));
    }
}
