using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TermLens {
// A dedicated message-loop thread keeps UI rendering out of the hook's deadline.
// The callback should only queue UI work: a low-level hook must return promptly.
internal sealed class ReleaseMouseShortcut : IDisposable {
    delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    readonly int key,modifiers;
    readonly Action<IntPtr> callback;
    readonly HookProc hookProc;
    readonly object startupGate=new object();
    Thread worker;
    volatile uint workerId;
    bool startupFinished;
    Exception startupError,shutdownError;
    IntPtr hook;
    bool pressed;
    volatile bool disposed;
    IntPtr source;
    internal ReleaseMouseShortcut(int key,int modifiers,Action<IntPtr> callback)
        : this(key,modifiers,callback,true) { }
    internal ReleaseMouseShortcut(int key,int modifiers,Action<IntPtr> callback,bool installHook) {
        if(key!=0 && key!=(int)Keys.XButton1 && key!=(int)Keys.XButton2) throw new ArgumentOutOfRangeException("key");
        if((modifiers & ~15)!=0) throw new ArgumentOutOfRangeException("modifiers");
        if(callback==null) throw new ArgumentNullException("callback");
        this.key=key; this.modifiers=modifiers; this.callback=callback;
        hookProc=OnMouse;
        if(key!=0 && installHook) {
            worker=new Thread(HookThread) { IsBackground=true,Name="TermLens mouse shortcut" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            var watch=Stopwatch.StartNew();
            lock(startupGate) {
                while(!startupFinished && watch.ElapsedMilliseconds<4000)
                    Monitor.Wait(startupGate,(int)Math.Max(1,4000-watch.ElapsedMilliseconds));
                if(!startupFinished) startupError=new TimeoutException("鼠标侧键快捷键启动超时。");
            }
            if(startupError!=null) {
                disposed=true; RequestStop(); worker.Join(1000);
                throw new InvalidOperationException("无法启动鼠标侧键快捷键。",startupError);
            }
        }
    }
    void HookThread() {
        try {
            workerId=GetCurrentThreadId();
            // Create the native queue before publishing readiness or accepting WM_QUIT.
            NativeMessage ignored;
            PeekMessage(out ignored,IntPtr.Zero,0,0,0);
            if(disposed) return;
            IntPtr installed=SetWindowsHookEx(14,hookProc,GetModuleHandle(null),0);
            if(installed==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法注册鼠标侧键快捷键。");
            Interlocked.Exchange(ref hook,installed);
            lock(startupGate) { startupFinished=true; Monitor.PulseAll(startupGate); }
            if(!disposed) Application.Run();
        } catch(Exception e) {
            lock(startupGate) {
                if(!startupFinished) startupError=e;
                else shutdownError=e;
            }
        } finally {
            IntPtr installed=Interlocked.Exchange(ref hook,IntPtr.Zero);
            if(installed!=IntPtr.Zero && !UnhookWindowsHookEx(installed))
                shutdownError=new Win32Exception(Marshal.GetLastWin32Error(),"无法移除鼠标侧键快捷键。");
            pressed=false; source=IntPtr.Zero;
            lock(startupGate) { startupFinished=true; Monitor.PulseAll(startupGate); }
            GC.KeepAlive(hookProc);
        }
    }
    void RequestStop() {
        uint id=workerId;
        if(id!=0) PostThreadMessage(id,0x12,UIntPtr.Zero,IntPtr.Zero);
    }
    internal bool HookInstalled { get { return Interlocked.CompareExchange(ref hook,IntPtr.Zero,IntPtr.Zero)!=IntPtr.Zero; } }
    internal bool ProcessButton(int button,bool down,int currentModifiers,bool injected,IntPtr foreground) {
        if(disposed || key==0 || injected || button!=key) return false;
        if(down) {
            if(pressed) return true;
            if(currentModifiers!=modifiers) return false;
            pressed=true; source=foreground; return true;
        }
        if(!pressed) return false;
        pressed=false;
        IntPtr captured=source; source=IntPtr.Zero;
        // Errors in a queued UI dispatch must never cross the native hook boundary.
        try { callback(captured); } catch(Exception) { }
        return true;
    }
    IntPtr OnMouse(int code,IntPtr message,IntPtr data) {
        try {
            int msg=message.ToInt32();
            if(code>=0 && (msg==0x20B || msg==0x20C)) {
                MouseHookData value=(MouseHookData)Marshal.PtrToStructure(data,typeof(MouseHookData));
                int nativeButton=(int)((value.mouseData>>16)&0xffff);
                int button=nativeButton==1?(int)Keys.XButton1:nativeButton==2?(int)Keys.XButton2:0;
                if(ProcessButton(button,msg==0x20B,CurrentModifiers(),(value.flags&3)!=0,GetForegroundWindow())) return new IntPtr(1);
            }
        } catch(Exception) { }
        return CallNextHookEx(hook,code,message,data);
    }
    internal static int CurrentModifiers() {
        int value=0;
        if(IsDown(0x12)) value|=1;
        if(IsDown(0x11)) value|=2;
        if(IsDown(0x10)) value|=4;
        if(IsDown(0x5B) || IsDown(0x5C)) value|=8;
        return value;
    }
    static bool IsDown(int virtualKey) { return (GetAsyncKeyState(virtualKey)&0x8000)!=0; }
    public void Dispose() {
        disposed=true;
        if(worker!=null) {
            RequestStop();
            if(Thread.CurrentThread!=worker) {
                if(!worker.Join(2000)) throw new TimeoutException("鼠标侧键快捷键停止超时。");
                if(shutdownError!=null) throw new InvalidOperationException("鼠标侧键快捷键停止失败。",shutdownError);
            }
        } else {
            pressed=false; source=IntPtr.Zero;
        }
        GC.KeepAlive(hookProc);
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x,y; }
    [StructLayout(LayoutKind.Sequential)] struct MouseHookData {
        public Point point;
        public uint mouseData,flags,time;
        public UIntPtr extraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeMessage {
        public IntPtr window;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point point;
        public uint privateData;
    }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool PostThreadMessage(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool PeekMessage(out NativeMessage message,IntPtr window,uint min,uint max,uint remove);
}
}
