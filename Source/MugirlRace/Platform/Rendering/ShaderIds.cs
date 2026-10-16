using Waylith.Rendering;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 着色器属性 id，替代 <c>UnityEngine.Shader.PropertyToID</c>。两者共用 Waylith 唯一的一张 id 表，所以这里的 id 与 Verse
/// <see cref="ShaderPropertyIDs"/> 可以互换，也可以直接交给 <see cref="MaterialPropertyBlock"/> 的 int 重载。
/// </summary>
/// <remarks>
/// <para>
/// 原版名称用 <see cref="ShaderPropertyIDs"/>。其余 id 在设置它的代码旁缓存为 <c>static readonly int</c>
/// （进程级常量，不随存档变化，不需要清理）。
/// </para>
/// <para>
/// Waylith 会静默忽略着色器没有声明的属性，拼错的名字只表现为画面不对。所以模组自己的着色器参数一律用 <see cref="For(string, string)"/>：
/// 开发者模式下它会检查对应 ShaderDef 是否声明了该属性。
/// </para>
/// </remarks>
public static class ShaderIds
{
    /// <summary>
    /// <c>_MainTex</c>。1.7 的 Verse <see cref="ShaderPropertyIDs"/> 没有这一项。
    /// StaticCacheLifecycle: 进程级不变的属性 id，首次访问类型时注册，永不清理。
    /// </summary>
    public static readonly int MainTex = Get("_MainTex");

    /// <summary>返回 <paramref name="propertyName"/> 的 id，首次使用时创建。</summary>
    public static int Get(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        return ShaderPropertyId.GetOrCreate(propertyName);
    }

    /// <summary>
    /// 返回模组 ShaderDef <paramref name="shaderDefName"/> 中某参数的 id。开发者模式下，若该着色器不存在、编译失败或没有声明
    /// <paramref name="propertyName"/>，记录一次错误。
    /// </summary>
    public static int For(string shaderDefName, string propertyName)
    {
        int id = Get(propertyName);
        if (Prefs.DevMode)
        {
            ShaderSelfTest.VerifyProperty(shaderDefName, propertyName, id);
        }

        return id;
    }
}
