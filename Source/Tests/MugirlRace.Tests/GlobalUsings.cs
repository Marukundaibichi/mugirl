// 测试工程的导入。与 Mod 工程一致：不带限定的 Vector2/Vector3/Vector4/Quaternion/Matrix4x4 是 System.Numerics 类型，
// UnityEngine 类型只以别名出现。测试代码可以直接调用 UnityStubs 与 Verse，作为差分比较的参照。

global using System;
global using System.Numerics;
global using Mugirl.Mathematics;
global using Mugirl.Tests.Support;
global using Xunit;

global using Color = UnityEngine.Color;
global using Color32 = UnityEngine.Color32;
global using Rect = UnityEngine.Rect;
global using UMathf = UnityEngine.Mathf;
global using UMatrix4x4 = UnityEngine.Matrix4x4;
global using UQuaternion = UnityEngine.Quaternion;
global using UVector2 = UnityEngine.Vector2;
global using UVector3 = UnityEngine.Vector3;
global using UVector4 = UnityEngine.Vector4;
