using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using BookDB.Mobile.UITests;

// Runs every [Fact]/[Theory] in this assembly on the Avalonia dispatcher thread built by TestAppBuilder, the
// same arrangement the desktop harness uses.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestFramework]
