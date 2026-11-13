// Glimpseon
// Copyright (C) 2026 HelloGaoo
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// MicSampler
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

/// <summary>麦克风采样 winmm waveIn RMS -> dB(约 30~90 语音区间)</summary>
internal static class MicSampler
{
    private static bool _started;
    private static WaveInProc? _proc;

    public static event Action<double>? DbReady;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private delegate void WaveInProc(IntPtr h, uint msg, IntPtr inst, IntPtr wavhdr, IntPtr p2);

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr h, uint devId, ref WAVEFORMATEX fmt, WaveInProc proc, IntPtr inst, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr h);

    private const uint CallbackFunction = 0x30000;
    private const uint WimData = 0x3C0;

    private static IntPtr _handle;
    private static IntPtr _headerA;
    private static IntPtr _headerB;
    private static IntPtr _dataA;
    private static IntPtr _dataB;
    private const int MicBufBytes = 32000;
    private static GCHandle _procHandle;

    public static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        try
        {
            _proc = OnWaveIn;
            _procHandle = GCHandle.Alloc(_proc);
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1,
                nChannels = 1,
                nSamplesPerSec = 16000,
                nAvgBytesPerSec = 32000,
                nBlockAlign = 2,
                wBitsPerSample = 16,
                cbSize = 0,
            };
            var hr = waveInOpen(out _handle, 0xFFFFFFFFu, ref fmt, _proc, IntPtr.Zero, CallbackFunction);
            if (hr != 0)
            {
                Log.Warning($"[MIC] waveInOpen 失败 hr=0x{hr:X8}");
                return;
            }
            _dataA = Marshal.AllocHGlobal(MicBufBytes);
            _dataB = Marshal.AllocHGlobal(MicBufBytes);
            _headerA = AllocHeader(_dataA);
            _headerB = AllocHeader(_dataB);
            waveInAddBuffer(_handle, _headerA, (uint)Marshal.SizeOf<WAVEHDR>());
            waveInAddBuffer(_handle, _headerB, (uint)Marshal.SizeOf<WAVEHDR>());
            waveInStart(_handle);
            Log.Info("[MIC] 麦克风采样已启动");
        }
        catch (Exception e)
        {
            Log.Warning($"[MIC] 麦克风采样启动失败: {e.Message}");
        }
    }

    // 数据缓冲必须是非托管内存: 托管数组会被 GC 搬移 winmm 线程仍写旧地址 -> 堆损坏 -> 随机闪退
    private static IntPtr AllocHeader(IntPtr data)
    {
        var h = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
        var hdr = new WAVEHDR { lpData = data, dwBufferLength = MicBufBytes };
        Marshal.StructureToPtr(hdr, h, false);
        waveInPrepareHeader(_handle, h, (uint)Marshal.SizeOf<WAVEHDR>());
        return h;
    }

    private static void OnWaveIn(IntPtr h, uint msg, IntPtr inst, IntPtr wavhdr, IntPtr p2)
    {
        if (msg != WimData || wavhdr == IntPtr.Zero)
        {
            return;
        }
        try
        {
            var hdr = Marshal.PtrToStructure<WAVEHDR>(wavhdr);
            var recorded = (int)hdr.dwBytesRecorded;
            if (recorded > 0 && hdr.lpData != IntPtr.Zero)
            {
                var bytes = new byte[recorded];
                Marshal.Copy(hdr.lpData, bytes, 0, recorded);
                double sum = 0;
                var samples = recorded / 2;
                for (var i = 0; i + 1 < recorded; i += 2)
                {
                    var s = BitConverter.ToInt16(bytes, i);
                    sum += (double)s * s;
                }
                var rms = samples > 0 ? Math.Sqrt(sum / samples) : 0;
                var db = rms > 0 ? Math.Clamp(90 + 20 * Math.Log10(rms / 32768.0), 0, 120) : 0;
                Dispatcher.UIThread.Post(() => DbReady?.Invoke(db));
            }
            waveInAddBuffer(_handle, wavhdr, (uint)Marshal.SizeOf<WAVEHDR>());
        }
        catch (Exception e)
        {

            Log.Debug($"[PERF] 采样异常: {e.Message}");
            // 采样异常忽略
        }
    }
}
