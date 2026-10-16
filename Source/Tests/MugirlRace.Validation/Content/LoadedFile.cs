namespace MugirlRace.Validation.Content;

/// <summary>游戏会从 Mod 某个加载目录加载的文件。</summary>
/// <param name="LoadFolder">来源加载目录。</param>
/// <param name="RelativePath">相对 <paramref name="LoadFolder"/> 的路径（例如 <c>Defs\ThingDefs\X.xml</c>）。</param>
/// <param name="FullPath">绝对路径。</param>
internal sealed record LoadedFile(string LoadFolder, string RelativePath, string FullPath);
