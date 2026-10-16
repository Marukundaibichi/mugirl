namespace Mugirl.Core;

/// <summary>
/// 每个 Harmony 补丁类必须标注的审计元数据。
/// </summary>
/// <remarks>
/// 目标方法、补丁种类和是否跳过原方法都由 Harmony 自身的特性与补丁方法签名决定，这里只记录无法从代码推导的信息：
/// 所属模块、失效风险和失效后的表现。<see cref="Deferred"/> 为 true 的类由 <see cref="HarmonyBootstrap"/>
/// 推迟到加载长事件结束后再打补丁，只用于目标类型的静态构造会加载贴图等资源的情况。
/// </remarks>
/// <param name="module">补丁所属模块（<see cref="PatchModule"/>，与补丁类所在的功能或兼容文件夹一致）。</param>
/// <param name="risk">补丁失效时的影响等级。</param>
/// <param name="failureBehavior">补丁未生效时玩家会看到的行为。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MugirlPatchAttribute(PatchModule module, PatchRisk risk, string failureBehavior) : Attribute
{
    public PatchModule Module { get; } = module;

    public PatchRisk Risk { get; } = risk;

    public string FailureBehavior { get; } = failureBehavior;

    /// <summary>为 true 时补丁在 <c>LongEventHandler.ExecuteWhenFinished</c> 中注册，而不是在 Mod 构造函数中。</summary>
    public bool Deferred { get; set; }
}
