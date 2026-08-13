if (args.Length == 0)
{
    return 2;
}

switch (args[0])
{
    case "isolate":
        foreach (var excluded in new[] { ".git", "node_modules", "dist", "bin", "obj", "proof-output" })
        {
            if (Directory.Exists(excluded) || File.Exists(excluded))
            {
                Console.Error.WriteLine($"excluded entry was copied: {excluded}");
                return 3;
            }
        }

        File.WriteAllText("source.txt", "changed only in isolated copy");
        Directory.CreateDirectory("proof-output");
        File.WriteAllText(Path.Combine("proof-output", "result.txt"), "generated");
        Console.WriteLine("fixture-result=generated");
        Console.Error.WriteLine("fixture-diagnostic=kept");
        return 0;

    case "wait" when args.Length == 2 && int.TryParse(args[1], out var milliseconds):
        await Task.Delay(milliseconds);
        return 0;

    case "emit":
        Console.Write("token=super-secret ");
        Console.Write(new string('x', 100_000));
        Console.Error.Write("password=hunter2");
        return 0;

    default:
        Console.Error.WriteLine("unknown fixture operation");
        return 4;
}
