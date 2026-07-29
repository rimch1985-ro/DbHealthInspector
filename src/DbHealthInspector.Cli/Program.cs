using System.CommandLine;

RootCommand rootCommand = new(
    "DbHealth Inspector is a read-only PostgreSQL metadata diagnostics CLI. "
    + "This bootstrap build does not include inspection features.");

rootCommand.SetAction(_ =>
{
    Console.WriteLine("DbHealth Inspector bootstrap baseline.");
    Console.WriteLine("Database inspection is not implemented yet.");
});

return rootCommand.Parse(args).Invoke();
