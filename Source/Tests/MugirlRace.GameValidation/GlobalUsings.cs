// 测试模组的全局导入。被测 Mod 的类型只在需要 HAR.MugirlRace 的套件中按需 using，
// 这样 -Plumbing（不加载雪牛娘）运行时框架代码不会触发对 MugirlRace.dll 的解析。

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using RimWorld;
global using Verse;
