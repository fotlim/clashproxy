using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClashProxy.Core;

/// <summary>
/// 用一个 Job Object 把内核进程绑定到本进程：本进程无论怎么消失
/// （正常退出、被任务管理器强杀、崩溃），内核都会跟着被系统回收，
/// 不会留下孤儿 mihomo 占着端口。
/// </summary>
internal static class KernelJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static IntPtr _job;

    /// <summary>创建（或复用）带 KILL_ON_JOB_CLOSE 的作业对象。</summary>
    private static bool EnsureJob()
    {
        if (_job != IntPtr.Zero) return true;

        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return false;

        var info = new ExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                CloseHandle(job);
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        _job = job;
        return true;
    }

    /// <summary>把已启动的进程放进作业对象。</summary>
    public static bool Attach(Process p)
    {
        try
        {
            if (!EnsureJob()) return false;
            return AssignProcessToJobObject(_job, p.Handle);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 清理上次异常退出留下的、由本程序启动的内核进程。
    /// 只杀「可执行文件路径 == 本程序自带 mihomo」的进程，
    /// 不会碰用户自己装的 Clash / Verge。
    /// </summary>
    public static int KillOrphans()
    {
        var killed = 0;
        Process[] all;
        try { all = Process.GetProcessesByName("mihomo"); }
        catch { return 0; }

        foreach (var p in all)
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;

                var path = p.MainModule?.FileName;   // 跨用户/受保护进程会抛，跳过即可
                if (string.IsNullOrEmpty(path)) continue;
                if (!string.Equals(path, AppPaths.KernelExe, StringComparison.OrdinalIgnoreCase)) continue;

                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                killed++;
            }
            catch
            {
                // 权限不足或已退出：忽略
            }
            finally
            {
                p.Dispose();
            }
        }
        return killed;
    }
}
