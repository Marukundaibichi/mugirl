using System;
using System.Collections.Generic;
using System.IO;
using Mugirl.Build.MetadataOrder;

try
{
    string? assemblyPath = null, symbolsPath = null, orderPath = null;
    var directories = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        string option = args[i];
        if (++i >= args.Length) { throw new ArgumentException("Missing value for " + option); }
        switch (option)
        {
            case "--assembly": assemblyPath = Path.GetFullPath(args[i]); break;
            case "--pdb": symbolsPath = Path.GetFullPath(args[i]); break;
            case "--order": orderPath = Path.GetFullPath(args[i]); break;
            case "--search-dir": directories.Add(Path.GetFullPath(args[i])); break;
            default: throw new ArgumentException("Unknown option " + option);
        }
    }
    if (assemblyPath == null || symbolsPath == null || orderPath == null)
    {
        throw new ArgumentException("Usage: --assembly <dll> --pdb <portable-pdb> --order <manifest> [--search-dir <directory>]");
    }
    byte[] assembly = File.ReadAllBytes(assemblyPath), symbols = File.ReadAllBytes(symbolsPath);
    directories.Add(Path.GetDirectoryName(assemblyPath)!);
    MetadataOrderer.Result result = MetadataOrderer.Rewrite(assembly, symbols, MetadataOrderer.ReadOrder(File.ReadAllText(orderPath)), directories);
    if (result.Changed)
    {
        // 两个输出均已在内存读回复核。写入失败时恢复原字节，构建报错且不会进入部署。
        try
        {
            File.WriteAllBytes(assemblyPath, result.Assembly);
            File.WriteAllBytes(symbolsPath, result.Symbols);
        }
        catch
        {
            File.WriteAllBytes(assemblyPath, assembly);
            File.WriteAllBytes(symbolsPath, symbols);
            throw;
        }
    }
    Console.WriteLine($"Metadata order: {(result.Changed ? "reordered" : "unchanged")}; {result.Types} types, {result.SequencePoints} sequence points preserved.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Metadata ordering failed: " + exception);
    return 1;
}
