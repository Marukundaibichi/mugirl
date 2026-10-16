using System.Runtime.CompilerServices;
using System.Text;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;

namespace MugirlRace.Validation;

/// <summary>离线验证程序入口。用法见 <see cref="ValidationOptions.Usage"/> 与 tools/Validate.ps1。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Def、路径与 Keyed 文本含中文；apphost 控制台默认代码页 936。
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        ValidationOptions options;
        try
        {
            options = ValidationOptions.Parse(args);
        }
        catch (UsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ValidationOptions.Usage);
            return ExitCodes.Usage;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ValidationOptions.Usage);
            return ExitCodes.Passed;
        }

        try
        {
            // 语料提取与一致性比较只读元数据与 XML，不加载游戏。
            switch (options.Mode)
            {
                case ValidationMode.ExtractLegacySchema:
                    return Legacy.LegacyExtraction.Run(options);
                case ValidationMode.Parity:
                    return Parity.ParityRun.Execute(options);
            }

            // 这两步只读文件和 XML：解析器安装之前不碰任何游戏、Harmony 或 Mod 类型。
            ModCatalog catalog = ModCatalog.Discover(options);
            GameAssemblyResolver.Install(options, catalog);
            return Run(options, catalog);
        }
        catch (UsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitCodes.Usage;
        }
        catch (EnvironmentException exception)
        {
            Console.Error.WriteLine("ENVIRONMENT ERROR: " + exception.Message);
            return ExitCodes.Usage;
        }
    }

    // 不内联进 Main：Verse、Harmony 与 Mod 类型必须在解析器安装之后才绑定。
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(ValidationOptions options, ModCatalog catalog)
        => options.Mode == ValidationMode.SaveCorpus ? SaveCorpusRun.Execute(options, catalog) : ValidationRun.Execute(options, catalog);
}
