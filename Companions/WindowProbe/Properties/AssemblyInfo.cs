using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("TonePrism_WindowProbe")]
[assembly: AssemblyDescription("Probes whether a process tree owns a visible / foreground window. Used by Launcher to sync the 起動中→プレイ中 transition and detect launcher-foreground anomalies. See SPECIFICATION.md §2.4 / issues #101, #216.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("TonePrism_WindowProbe")]
[assembly: AssemblyCopyright("Copyright © 2025-2026 TonePrism Project — Lead maintainer: Kenshiro Kuroga (Osaka Prefectural Toneyama Upper Secondary School PC Club)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("c7a2f3e1-8b4d-4a6c-9e2f-3d5a1b7c9e0f")]

// 本コンポーネントは通常 release 跨ぎで変更しない (SPEC §2.4)。
// 変更時はここを bump + CHANGELOG `## Companions` セクションに entry 追加。
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
