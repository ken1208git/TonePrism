using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("GCTonePrism_Updater")]
[assembly: AssemblyDescription("Updates GCTonePrism Manager (Phase 3, minimal CLI). See SPECIFICATION.md §3.7.4.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("GCTonePrism_Updater")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("b5d71e9c-0f2e-4f4a-9e5a-1a2c3d4e5f6a")]

// Updater 初版。本コンポーネントは通常 release 跨ぎで変更しない (SPEC §3.7.4)。
// 変更時はここを bump + CHANGELOG `## Companions` セクション (旧 `## Updater`、#160 で rename) に entry 追加。
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
