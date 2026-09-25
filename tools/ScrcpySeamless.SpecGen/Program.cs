using ScrcpySeamless.SpecGen;

if (args.Length != 1 || args[0] is not ("generate" or "verify"))
{
    Console.Error.WriteLine("Usage: SpecGen generate|verify (run from the repository root)");
    return 2;
}

try
{
    GenerationRunner.Execute(Directory.GetCurrentDirectory(), args[0]);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SpecGen failed: {exception.Message}");
    return 1;
}
