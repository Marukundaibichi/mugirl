using System.Buffers.Binary;
using System.Text;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 最小的 SPIR-V 反射：只读取接口变量（OpVariable）的存储类、描述符集、绑定、location 与名字，足以核对编译结果与引擎解析的布局一致。
/// </summary>
internal sealed class SpirvModule
{
    private const uint MagicNumber = 0x07230203;

    private SpirvModule(IReadOnlyList<SpirvVariable> variables) => Variables = variables;

    /// <summary>模块中的全局变量（不含函数内变量）。</summary>
    public IReadOnlyList<SpirvVariable> Variables { get; }

    /// <summary>解析 <paramref name="spirv"/>；不是 SPIR-V 时抛出 <see cref="FormatException"/>。</summary>
    public static SpirvModule Parse(byte[] spirv)
    {
        if (spirv.Length < 20 || spirv.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(spirv) != MagicNumber)
        {
            throw new FormatException("Not a little-endian SPIR-V module.");
        }

        Dictionary<uint, string> names = [];
        Dictionary<uint, Dictionary<SpirvDecoration, uint>> decorations = [];
        List<(uint Id, SpirvStorageClass StorageClass)> variables = [];
        int words = spirv.Length / 4;
        for (int index = 5; index < words;)
        {
            uint first = Word(spirv, index);
            int count = (int)(first >> 16);
            var opcode = (SpirvOpcode)(first & 0xFFFF);
            if (count <= 0 || index + count > words)
            {
                throw new FormatException($"Malformed instruction at word {index}.");
            }

            switch (opcode)
            {
                case SpirvOpcode.Name when count >= 3:
                    names[Word(spirv, index + 1)] = ReadString(spirv, index + 2, count - 2);
                    break;
                case SpirvOpcode.Decorate when count >= 3:
                    uint target = Word(spirv, index + 1);
                    var decoration = (SpirvDecoration)Word(spirv, index + 2);
                    if (!decorations.TryGetValue(target, out Dictionary<SpirvDecoration, uint>? values))
                    {
                        decorations[target] = values = [];
                    }

                    values[decoration] = count >= 4 ? Word(spirv, index + 3) : 1u;
                    break;
                case SpirvOpcode.Variable when count >= 4:
                    var storageClass = (SpirvStorageClass)Word(spirv, index + 3);
                    if (storageClass != SpirvStorageClass.Function)
                    {
                        variables.Add((Word(spirv, index + 2), storageClass));
                    }

                    break;
            }

            index += count;
        }

        return new SpirvModule([.. variables.Select(variable =>
        {
            Dictionary<SpirvDecoration, uint> values = decorations.GetValueOrDefault(variable.Id) ?? [];
            return new SpirvVariable(
                names.GetValueOrDefault(variable.Id) ?? $"%{variable.Id}",
                variable.StorageClass,
                values.TryGetValue(SpirvDecoration.DescriptorSet, out uint set) ? (int)set : null,
                values.TryGetValue(SpirvDecoration.Binding, out uint binding) ? (int)binding : null,
                values.TryGetValue(SpirvDecoration.Location, out uint location) ? (int)location : null,
                values.ContainsKey(SpirvDecoration.BuiltIn));
        })]);
    }

    private static uint Word(byte[] spirv, int index) => BinaryPrimitives.ReadUInt32LittleEndian(spirv.AsSpan(index * 4));

    private static string ReadString(byte[] spirv, int index, int wordCount)
    {
        ReadOnlySpan<byte> bytes = spirv.AsSpan(index * 4, wordCount * 4);
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes);
    }
}

/// <summary>一个 SPIR-V 全局变量。</summary>
/// <param name="Name">OpName 给出的名字，没有时为 %id。</param>
/// <param name="StorageClass">存储类。</param>
/// <param name="Set">描述符集。</param>
/// <param name="Binding">绑定号。</param>
/// <param name="Location">location。</param>
/// <param name="IsBuiltIn">内建变量（gl_Position 等）。</param>
internal sealed record SpirvVariable(string Name, SpirvStorageClass StorageClass, int? Set, int? Binding, int? Location, bool IsBuiltIn);

/// <summary>用到的 SPIR-V 指令。</summary>
internal enum SpirvOpcode : uint
{
    /// <summary>OpName。</summary>
    Name = 5,

    /// <summary>OpVariable。</summary>
    Variable = 59,

    /// <summary>OpDecorate。</summary>
    Decorate = 71,
}

/// <summary>用到的 SPIR-V 修饰。</summary>
internal enum SpirvDecoration : uint
{
    /// <summary>BuiltIn。</summary>
    BuiltIn = 11,

    /// <summary>Location。</summary>
    Location = 30,

    /// <summary>Binding。</summary>
    Binding = 33,

    /// <summary>DescriptorSet。</summary>
    DescriptorSet = 34,
}

/// <summary>SPIR-V 存储类。</summary>
internal enum SpirvStorageClass : uint
{
    /// <summary>采样器、纹理。</summary>
    UniformConstant = 0,

    /// <summary>阶段输入。</summary>
    Input = 1,

    /// <summary>uniform 块。</summary>
    Uniform = 2,

    /// <summary>阶段输出。</summary>
    Output = 3,

    /// <summary>工作组共享。</summary>
    Workgroup = 4,

    /// <summary>跨工作组。</summary>
    CrossWorkgroup = 5,

    /// <summary>私有全局。</summary>
    Private = 6,

    /// <summary>函数局部。</summary>
    Function = 7,

    /// <summary>push constant。</summary>
    PushConstant = 9,

    /// <summary>存储缓冲。</summary>
    StorageBuffer = 12,
}
