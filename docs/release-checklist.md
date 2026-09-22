# 发布检查清单

本文档用于 Workshop 或本地发布前的最后确认。目标是发布包干净、可加载、没有临时文件和源码泄漏。

## 发布前

1. 确认当前工作区没有无关改动。
2. 跑完整静态验证。
3. 对涉及玩家行为的改动做 fresh `Player.log` 验证。
4. 检查 `About/About.xml`、`LoadFolders.xml` 和可选集成目录。
5. 确认翻译 key 对齐，玩家可见文本不缺 key。
6. 按[目录与清理约定](../README.md)核查 `TMP`：先归档原始素材、回退 DLL、存档和需追溯的证据，再清理可再生成输出，不能整目录盲删。

## 生成发布包

```powershell
.\docs\tools\New-WorkshopPackage.ps1
```

默认输出：

```text
TMP\WorkshopPackage\MugirlRace
```

如果已经构建过 DLL，可以跳过构建：

```powershell
.\docs\tools\New-WorkshopPackage.ps1 -SkipBuild
```

## 发布包不应包含

- `1.6/Source`
- `bin`、`obj`
- `.cs`、`.csproj`、`.pdb`
- `.sai2`、`.tmp`、`.bak`
- `TMP`
- `docs`
- `DevData`、`SourceAssets`
- 备份目录或源素材

发布脚本只复制明确列出的游戏目录，不复制上述开发资料目录。需要保留的发布 ZIP 放在 `DevData/Releases`；展开副本经逐文件哈希与 ZIP 核对一致后可清理。

## 发布包应包含

- `About`
- `LoadFolders.xml`
- `1.6/Assemblies/MugirlRace.dll`
- `1.6/Defs`
- `1.6/Languages`
- `1.6/Patches`
- 条件目录：`Bio_1.6`、`Odyssey_1.6`、`Versions`、`1.6/FacialAnimation`
- `Textures`
- `Sounds`

## 最后检查

```powershell
git diff --check
```

Windows 上出现 LF/CRLF 提示不等于空白错误；如果有 trailing whitespace 或 conflict marker，必须修复。
