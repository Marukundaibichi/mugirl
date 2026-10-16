namespace Mugirl.GameValidation.Core;

/// <summary>存档夹具的种类（critique-validation §4.1）；清单与参数文件中写作 <see cref="FixtureKinds.Text"/> 给出的文本。</summary>
internal enum FixtureKind
{
    /// <summary>P：在 1.7 游戏上重放已发布的旧构建得到的存档（版本 0）。</summary>
    Published,

    /// <summary>E：验证构建留下的证据存档（版本 0）。</summary>
    Evidence,

    /// <summary>R：玩家真实存档的只读副本（版本 0）。</summary>
    Real,

    /// <summary>C：被测构建本次运行时自己写出的存档（对照组：读档必须没有任何差异）。</summary>
    Control,

    /// <summary>X-alias：P 存档加上一个用旧 XML 类名的临时依赖 Mod。</summary>
    Alias,

    /// <summary>X-settings：P 存档加上旧构建写出的 Mod 设置文件。</summary>
    Settings,
}
