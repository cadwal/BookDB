using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using BookDB.Desktop.UITests;

// Runs every [Fact]/[Theory] in this assembly on the Avalonia dispatcher thread built by TestAppBuilder — the
// xunit.v3-native replacement for the manual HeadlessUnitTestSession driver. An alternative to decorating every
// method with [AvaloniaFact]/[AvaloniaTheory].
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestFramework]
