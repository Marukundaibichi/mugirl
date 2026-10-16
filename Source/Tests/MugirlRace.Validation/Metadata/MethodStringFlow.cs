using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary><see cref="StringFlowAnalysis.Analyze"/> 对一个方法的结果。</summary>
/// <param name="Method">被分析的方法。</param>
/// <param name="Calls">所有可达调用及其实参值。</param>
/// <param name="Stores">所有可达字段写入及写入值。</param>
/// <param name="Traceable">IL 无法分析时为 false，此时所有实参都视为未知。</param>
internal sealed record MethodStringFlow(MethodDefinition Method, IReadOnlyList<StringCall> Calls, IReadOnlyList<FieldStore> Stores, bool Traceable);
