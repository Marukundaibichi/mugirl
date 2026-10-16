using Verse;

namespace MugirlRace.Validation.Content;

/// <summary>按加载它的 Graphic 类，判断一个贴图路径需要哪些文件。</summary>
internal static class TextureRules
{
    /// <summary>
    /// 按 <paramref name="graphicClass"/> 检查 <paramref name="path"/>：<c>Graphic_Single</c> 需要该文件；<c>Graphic_Multi</c> 需要
    /// <c>_north/_east/_south/_west</c> 任一或路径本身（同 <c>Graphic_Multi.Init</c> 的回退）；<c>Graphic_Collection</c>（Random、
    /// StackCount、Flicker、Cluster 等）需要非空目录；未知类接受以上任一形式。
    /// </summary>
    public static (TextureMatch Match, string Form) Check(TextureIndex index, string path, Type? graphicClass)
    {
        if (graphicClass is not null && typeof(Graphic_Collection).IsAssignableFrom(graphicClass))
        {
            return (index.FolderHasTextures(path) ? TextureMatch.Found : TextureMatch.Missing, "folder");
        }

        if (graphicClass is not null && typeof(Graphic_Multi).IsAssignableFrom(graphicClass))
        {
            return (Multi(index, path), "multi");
        }

        if (graphicClass is not null && typeof(Graphic_Single).IsAssignableFrom(graphicClass))
        {
            return (index.Find(path), "file");
        }

        TextureMatch any = Best(index.Find(path), Multi(index, path));
        return (any == TextureMatch.Missing && index.FolderHasTextures(path) ? TextureMatch.Found : any, "file, multi or folder");
    }

    /// <summary>
    /// 服装 <c>wornGraphicPath</c>：路径本身或至少一种体型的 <c>path_&lt;BodyType&gt;</c> 处存在 <c>Graphic_Multi</c>
    /// （<c>ApparelGraphicRecordGetter</c> 对大多数层追加穿戴者体型）。
    /// </summary>
    public static TextureMatch CheckWorn(TextureIndex index, string path, IEnumerable<string> bodyTypes)
    {
        TextureMatch best = Multi(index, path);
        foreach (string bodyType in bodyTypes)
        {
            best = Best(best, Multi(index, path + "_" + bodyType));
        }

        return best;
    }

    private static TextureMatch Multi(TextureIndex index, string path)
        => Best(index.Find(path + "_north"), index.Find(path + "_east"), index.Find(path + "_south"), index.Find(path + "_west"), index.Find(path));

    private static TextureMatch Best(params TextureMatch[] matches) => matches.Min();
}
