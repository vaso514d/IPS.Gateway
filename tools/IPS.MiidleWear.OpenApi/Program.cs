namespace IPS.MiidleWear.OpenApi;

// dotnet run --project tools/IPS.MiidleWear.OpenApi [-- <repository root>]
// A named entry point, so referencing the tool does not add a second global Program next to the Api's.
internal static class DocumentWriter
{
    private static async Task Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : CoreReceiverDocument.FindRepositoryRoot(Directory.GetCurrentDirectory());
        var path = Path.Combine(root, CoreReceiverDocument.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, await CoreReceiverDocument.GenerateAsync(root));
        Console.WriteLine($"Wrote {path}");
    }
}
