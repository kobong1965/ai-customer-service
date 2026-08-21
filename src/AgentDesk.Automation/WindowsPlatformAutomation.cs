using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentDesk.Core;
using AgentDesk.Infrastructure;

namespace AgentDesk.Automation;

public sealed record PlatformWindowInfo(
    nint Handle,
    string Title,
    int Left,
    int Top,
    int Width,
    int Height)
{
    public string DisplayName => $"{Title}  ·  {Width}×{Height}";
}

public sealed record CapturedPlatformWindow(
    PlatformWindowInfo Window,
    string DataUrl,
    string ContentHash);

public sealed class WindowsPlatformAutomation
{
    private const int SwRestore = 9;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VkControl = 0x11;
    private const ushort VkA = 0x41;
    private const int Srccopy = 0x00CC0020;
    private const int PwRenderFullContent = 2;

    public IReadOnlyList<PlatformWindowInfo> GetVisibleWindows()
    {
        var windows = new List<PlatformWindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var length = GetWindowTextLength(handle);
            if (length <= 0 || rect.Width < 500 || rect.Height < 400)
            {
                return true;
            }

            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            var value = title.ToString().Trim();
            if (value.Length > 0)
            {
                windows.Add(new PlatformWindowInfo(
                    handle,
                    value,
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    rect.Height));
            }

            return true;
        }, nint.Zero);

        return windows.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public PlatformWindowInfo? FindWindow(string titleContains) =>
        GetVisibleWindows().FirstOrDefault(window =>
            window.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));

    public CapturedPlatformWindow Capture(string titleContains)
    {
        var window = FindWindow(titleContains)
            ?? throw new InvalidOperationException($"未找到标题包含“{titleContains}”的客服平台窗口。");
        return Capture(window);
    }

    public CapturedPlatformWindow Capture(PlatformWindowInfo window)
    {
        if (!GetWindowRect(window.Handle, out var rect) || rect.Width < 1 || rect.Height < 1)
        {
            throw new InvalidOperationException("客服平台窗口已关闭或尺寸无效。");
        }

        var windowDc = GetWindowDC(window.Handle);
        if (windowDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取客服平台窗口画面。");
        }

        var memoryDc = CreateCompatibleDC(windowDc);
        var bitmap = CreateCompatibleBitmap(windowDc, rect.Width, rect.Height);
        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            if (!PrintWindow(window.Handle, memoryDc, PwRenderFullContent)
                && !BitBlt(memoryDc, 0, 0, rect.Width, rect.Height, windowDc, 0, 0, Srccopy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "客服平台截图失败。");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var bytes = stream.ToArray();
            var current = new PlatformWindowInfo(
                window.Handle,
                GetTitle(window.Handle),
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height);
            return new CapturedPlatformWindow(
                current,
                "data:image/png;base64," + Convert.ToBase64String(bytes),
                ComputeObservationHash(source));
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(window.Handle, windowDc);
        }
    }

    public async Task ClickRelativeAsync(
        PlatformWindowInfo window,
        double relativeX,
        double relativeY,
        CancellationToken cancellationToken)
    {
        ValidateUnit(relativeX, nameof(relativeX));
        ValidateUnit(relativeY, nameof(relativeY));
        var current = RequireSameWindow(window);
        RestoreAndFocus(current.Handle);
        var x = current.Left + (int)Math.Round(current.Width * relativeX);
        var y = current.Top + (int)Math.Round(current.Height * relativeY);
        if (!SetCursorPos(x, y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法移动鼠标到校准位置。");
        }

        SendMouseClick();
        await Task.Delay(350, cancellationToken);
    }

    public async Task TypeAndSendAsync(
        PlatformWindowInfo window,
        PlatformCalibrationSettings calibration,
        string reply,
        CancellationToken cancellationToken)
    {
        if (!calibration.IsValid)
        {
            throw new InvalidOperationException("客服平台校准参数无效。");
        }

        if (string.IsNullOrWhiteSpace(reply) || reply.Length > 500)
        {
            throw new InvalidOperationException("待发送回复为空或超过 500 字，已拒绝发送。");
        }

        var current = RequireSameWindow(window);
        EnsureStableSize(current, calibration);
        await ClickRelativeAsync(current, calibration.InputX, calibration.InputY, cancellationToken);
        SendKeyboardChord(VkControl, VkA);
        SendUnicodeText(reply);
        await Task.Delay(200, cancellationToken);
        current = RequireSameWindow(current);
        EnsureStableSize(current, calibration);
        await ClickRelativeAsync(current, calibration.SendX, calibration.SendY, cancellationToken);
    }

    public static void EnsureStableSize(
        PlatformWindowInfo window,
        PlatformCalibrationSettings calibration)
    {
        if (!calibration.IsWindowSizeStable(window.Width, window.Height))
        {
            throw new InvalidOperationException(
                $"客服平台窗口尺寸已从 {calibration.CapturedWidth}×{calibration.CapturedHeight} 变化为 {window.Width}×{window.Height}，已停止并要求重新校准。");
        }
    }

    private static PlatformWindowInfo RequireSameWindow(PlatformWindowInfo expected)
    {
        if (!IsWindow(expected.Handle) || !GetWindowRect(expected.Handle, out var rect))
        {
            throw new InvalidOperationException("客服平台窗口在执行期间已关闭。");
        }

        var title = GetTitle(expected.Handle);
        if (!string.Equals(title, expected.Title, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("客服平台窗口标题在执行期间发生变化，已取消动作。");
        }

        return new PlatformWindowInfo(
            expected.Handle,
            title,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height);
    }

    private static void RestoreAndFocus(nint handle)
    {
        ShowWindow(handle, SwRestore);
        if (!SetForegroundWindow(handle))
        {
            throw new InvalidOperationException("无法将客服平台置于前台，已取消自动化动作。");
        }
    }

    private static void SendMouseClick()
    {
        var inputs = new[]
        {
            Input.Mouse(MouseEventLeftDown),
            Input.Mouse(MouseEventLeftUp)
        };
        EnsureSent(inputs);
    }

    private static void SendKeyboardChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            Input.Keyboard(modifier, 0),
            Input.Keyboard(key, 0),
            Input.Keyboard(key, KeyEventKeyUp),
            Input.Keyboard(modifier, KeyEventKeyUp)
        };
        EnsureSent(inputs);
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(Input.Unicode(character, 0));
            inputs.Add(Input.Unicode(character, KeyEventKeyUp));
        }

        EnsureSent(inputs.ToArray());
    }

    private static void EnsureSent(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 输入动作执行不完整。");
        }
    }

    private static string GetTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        var title = new StringBuilder(Math.Max(1, length + 1));
        GetWindowText(handle, title, title.Capacity);
        return title.ToString();
    }

    private static void ValidateUnit(double value, string name)
    {
        if (value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "相对坐标必须在 0 到 1 之间。");
        }
    }

    private static string ComputeObservationHash(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        using var sampled = new MemoryStream();
        var observationWidth = Math.Max(1, (int)(converted.PixelWidth * 0.66));
        for (var y = 0; y < converted.PixelHeight; y += 16)
        {
            for (var x = 0; x < observationWidth; x += 16)
            {
                var offset = (y * stride) + (x * 4);
                sampled.WriteByte((byte)(pixels[offset] >> 5));
                sampled.WriteByte((byte)(pixels[offset + 1] >> 5));
                sampled.WriteByte((byte)(pixels[offset + 2] >> 5));
            }
        }

        return Convert.ToHexString(SHA256.HashData(sampled.ToArray()));
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;

        public static Input Mouse(uint flags) => new()
        {
            Type = 0,
            Data = new InputUnion { Mouse = new MouseInput { Flags = flags } }
        };

        public static Input Keyboard(ushort key, uint flags) => new()
        {
            Type = 1,
            Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } }
        };

        public static Input Unicode(char character, uint flags) => new()
        {
            Type = 1,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = character,
                    Flags = flags | KeyEventUnicode
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder title, int maximumCount);
    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint window, nint deviceContext, int flags);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint objectHandle);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint objectHandle);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int operation);
}
