using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("TonePrism_LauncherAgent")]
[assembly: AssemblyDescription("Resident Launcher-side Win32 agent: window-state probe (起動中→プレイ中 #101 / foreground anomaly #216), global hotkey sensor (HOME / XInput Guide) for the on-game overlay menu (#30), and forced foreground. Talks to the Godot Launcher over bidirectional localhost UDP. Replaces WindowProbe. See SPECIFICATION.md §2.4.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("TonePrism_LauncherAgent")]
[assembly: AssemblyCopyright("Copyright © 2025-2026 TonePrism Project — Lead maintainer: Kenshiro Kuroga (Osaka Prefectural Toneyama Upper Secondary School PC Club)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("b2d4e6f8-1a3c-4e5d-9f70-2c4a6e8d0b1f")]

// 変更時はここを bump + CHANGELOG `## Companions` セクションに entry 追加。
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
