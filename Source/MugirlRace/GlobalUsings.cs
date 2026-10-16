// 全工程的导入。
//
// Mod 内的数学运算使用 System.Numerics：不带限定的 Vector2/Vector3/Vector4/Quaternion/Matrix4x4 一律是 System.Numerics 类型。
// 下面的 UnityEngine 类型是 RimWorld API 的边界类型（1.7 中由 UnityStubs 提供），逐个列出而不导入整个 UnityEngine，
// 让每一处越界在审阅时可见。它们只作数据：构造、读写字段、与游戏交换；其方法、运算符与静态成员属于 UnityStubs 逻辑，
// 由 BannedSymbols.txt 禁用（ADR-006）。向量进入时用 ToNumerics()、离开时用 ToUnity()（Mugirl.Mathematics.UnityInterop），
// 需要 Unity 语义的运算用 UnityMathf、UnityVector、UnityQuaternion、UnityMatrix、UnityColor。
// Mugirl.Core 提供日志、Harmony 启动、补丁元数据与游戏状态辅助类，业务代码无需逐文件导入。

global using System;
global using System.Collections.Generic;
global using System.Numerics;
global using Mugirl.Core;
global using Mugirl.Mathematics;
global using RimWorld;
global using Verse;

global using Color = UnityEngine.Color;
global using Color32 = UnityEngine.Color32;
global using Material = UnityEngine.Material;
global using MaterialPropertyBlock = UnityEngine.MaterialPropertyBlock;
global using Mesh = UnityEngine.Mesh;
global using Rect = UnityEngine.Rect;
global using Shader = UnityEngine.Shader;
global using TextAnchor = UnityEngine.TextAnchor;
global using Texture = UnityEngine.Texture;
global using Texture2D = UnityEngine.Texture2D;
global using UMatrix4x4 = UnityEngine.Matrix4x4;
global using UQuaternion = UnityEngine.Quaternion;
global using UVector2 = UnityEngine.Vector2;
global using UVector3 = UnityEngine.Vector3;
global using UVector4 = UnityEngine.Vector4;
