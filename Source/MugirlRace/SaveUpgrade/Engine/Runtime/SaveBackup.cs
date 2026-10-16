using System.Globalization;
using System.IO;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 旧档第一次升级前在原文件旁复制一份 <c>&lt;名称&gt;.rws.mugirl-schema&lt;N&gt;.bak</c>：RimWorld 存档时覆盖同一个文件，
/// 升级后的第一次保存就会替换原始内容。已有备份时从不覆盖（保留最早的原始文件）。扩展名不是 <c>.rws</c>，不会出现在读档列表中。
/// </summary>
internal static class SaveBackup
{
    internal static string PathFor(string savePath, int sourceSchema) =>
        savePath + ".mugirl-schema" + sourceSchema.ToString(CultureInfo.InvariantCulture) + ".bak";

    /// <summary>确保备份存在，返回备份路径；原文件不存在时返回 null。IO 异常由调用方处理。</summary>
    internal static string? Ensure(string savePath, int sourceSchema)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        string backup = PathFor(savePath, sourceSchema);
        if (File.Exists(backup))
        {
            return backup;
        }

        if (!File.Exists(savePath))
        {
            return null;
        }

        // 先完整写入本次唯一临时文件，再原子改名；复制中断不能留下看似有效的半份 .bak。
        string temporary = backup + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(savePath, temporary, overwrite: false);
            try
            {
                File.Move(temporary, backup, overwrite: false);
            }
            catch (IOException) when (File.Exists(backup))
            {
                // 另一读取方已完成首次备份，保留它，绝不覆盖。
            }

            return backup;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
