namespace DbHealthInspector.UnitTests;

public sealed class BootstrapSmokeTests
{
    [Fact]
    public void CoreAssemblyHasExpectedName()
    {
        string assemblyName = typeof(Core.AssemblyMarker).Assembly.GetName().Name!;

        Assert.Equal("DbHealthInspector.Core", assemblyName);
    }
}
