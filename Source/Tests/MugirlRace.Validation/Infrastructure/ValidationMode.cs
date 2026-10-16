namespace MugirlRace.Validation.Infrastructure;

/// <summary>验证程序的运行模式（见 <see cref="ValidationOptions.Usage"/>）。</summary>
internal enum ValidationMode
{
    /// <summary>运行全部检查。</summary>
    Validate,

    /// <summary><c>--extract-legacy-schema</c>：提取（或核对）旧存档名字语料。</summary>
    ExtractLegacySchema,

    /// <summary><c>--parity A B</c>：两个构建的 IL 与内容一致性。</summary>
    Parity,

    /// <summary><c>--save-corpus dir</c>：离线对真实存档运行升级管线。</summary>
    SaveCorpus,
}
