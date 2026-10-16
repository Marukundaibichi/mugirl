namespace Mugirl.GameValidation.Core;

/// <summary>本次运行使用的一个存档夹具（tools/Test-InGame.ps1 -Fixture 复制到临时存档目录的 Saves/ 中，或本次运行生成）。</summary>
/// <param name="Id">夹具标识，即 DevData/Evidence/SaveFixtures/&lt;id&gt;/ 的目录名（生成的对照组为 control）。</param>
/// <param name="SaveName">Saves/ 中的存档名（不含扩展名）。</param>
/// <param name="ManifestPath">存档的清单 manifest.json（对照组为播种套件的 sweep.json；X-* 夹具为 base 夹具的清单）的绝对路径。</param>
/// <param name="Kind">夹具种类。</param>
/// <param name="GameVersion">写出夹具的游戏版本（清单 game.version）；不同于运行中的版本时原版的 BackCompatibility 转换会运行。</param>
/// <param name="VariantManifestPath">X-* 夹具自己的清单（依赖 Mod、期待的 Def 等）；其他种类为 null。</param>
internal sealed record FixtureRef(string Id, string SaveName, string? ManifestPath, FixtureKind Kind, string? GameVersion, string? VariantManifestPath);
