// AudioDeviceSync: keeps the "default" (Console + Multimedia) and "communications"
// audio devices identical, separately for playback and recording.
// Whichever role the user changes last wins; the others follow.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AudioDeviceSync
{
    enum EDataFlow { eRender = 0, eCapture = 1 }
    enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public int pid; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    // Undocumented but stable since Windows 7; only SetDefaultEndpoint is used.
    // The earlier slots exist purely to keep the vtable layout correct.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility();
    }

    // Notifications only wake the worker; all decisions are made by comparing real state.
    class Notifier : IMMNotificationClient
    {
        readonly AutoResetEvent wake;
        public Notifier(AutoResetEvent wake) { this.wake = wake; }
        public int OnDeviceStateChanged(string id, int newState) { wake.Set(); return 0; }
        public int OnDeviceAdded(string id) { wake.Set(); return 0; }
        public int OnDeviceRemoved(string id) { wake.Set(); return 0; }
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string id) { wake.Set(); return 0; }
        public int OnPropertyValueChanged(string id, PropertyKey key) { return 0; }
    }

    static class Program
    {
        const int PollMs = 5000;      // safety net, e.g. if the audio service restarts and drops callbacks
        const int SettleMs = 250;     // let Windows finish changing several roles at once
        const long MaxLogBytes = 256 * 1024;

        static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioDeviceSync", "log.txt");

        static IMMDeviceEnumerator enumerator;
        static IPolicyConfig policy;
        // Last observed device id per flow and role, used to tell which role the user just changed.
        static readonly string[][] last = new string[2][];

        [MTAThread]
        static int Main()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\AudioDeviceSync", out created))
            {
                if (!created) return 0;

                var wake = new AutoResetEvent(true);
                var notifier = new Notifier(wake);
                Log("started");
                while (true)
                {
                    try
                    {
                        if (enumerator == null) Connect(notifier);
                        if (wake.WaitOne(PollMs)) Thread.Sleep(SettleMs);
                        Reconcile(EDataFlow.eRender);
                        Reconcile(EDataFlow.eCapture);
                    }
                    catch (Exception e)
                    {
                        Log("error: " + e.Message);
                        enumerator = null; policy = null;   // reconnect on next iteration
                        Thread.Sleep(PollMs);
                    }
                }
            }
        }

        static void Connect(IMMNotificationClient notifier)
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
            policy = (IPolicyConfig)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")));
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(notifier));
        }

        static string GetDefaultId(EDataFlow flow, ERole role)
        {
            IMMDevice dev;
            if (enumerator.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) return null;
            string id;
            return dev.GetId(out id) == 0 ? id : null;
        }

        static void Reconcile(EDataFlow flow)
        {
            int f = (int)flow;
            var cur = new string[3];
            for (int r = 0; r < 3; r++) cur[r] = GetDefaultId(flow, (ERole)r);

            if (cur[0] != cur[1] || cur[1] != cur[2])
            {
                string winner = null;
                var prev = last[f];
                if (prev != null)
                    for (int r = 0; r < 3 && winner == null; r++)   // Console > Multimedia > Communications
                        if (cur[r] != prev[r]) winner = cur[r];
                if (winner == null) winner = cur[0] ?? cur[1] ?? cur[2];

                if (winner != null)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        if (cur[r] == winner) continue;
                        int hr = policy.SetDefaultEndpoint(winner, (ERole)r);
                        Log(string.Format("{0} {1}: {2} -> {3} (hr=0x{4:X})", flow, (ERole)r, cur[r], winner, hr));
                    }
                    for (int r = 0; r < 3; r++) cur[r] = GetDefaultId(flow, (ERole)r);
                }
            }
            last[f] = cur;
        }

        static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxLogBytes) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
