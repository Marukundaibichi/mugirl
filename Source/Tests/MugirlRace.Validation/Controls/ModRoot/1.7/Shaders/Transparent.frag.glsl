// 违规：没有 Mugirl_ 前缀，遮蔽游戏的 Transparent.frag.glsl，并 include 一个不存在的文件。
#version 450
#include "Control_NoSuchInclude.glsl"
void main() {}
