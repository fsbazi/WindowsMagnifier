// 限制 DLL 搜索路径为 System32，防止 DLL 劫持攻击
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(
    System.Runtime.InteropServices.DllImportSearchPath.System32)]
