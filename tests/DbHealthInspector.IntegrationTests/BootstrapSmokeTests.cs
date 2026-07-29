namespace DbHealthInspector.IntegrationTests;

public sealed class BootstrapSmokeTests
{
    [Fact]
    public void PostgreSqlAssemblyHasExpectedName()
    {
        string assemblyName = typeof(PostgreSql.AssemblyMarker).Assembly.GetName().Name!;

        Assert.Equal("DbHealthInspector.PostgreSql", assemblyName);
    }
}
