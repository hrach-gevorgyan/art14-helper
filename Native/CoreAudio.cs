using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Art14Helper.Native;

/// <summary>
/// The parts of Windows Core Audio the mic key needs: the default capture endpoint,
/// its shared mute flag, and change notifications. The endpoint's mute (not an app
/// session's) is what every program and the Windows sound settings see.
/// </summary>
/// <remarks>
/// Interfaces use COM source generation (no runtime COM interop), so they work with
/// NativeAOT. Methods follow the vtable order; methods after the last one used are
/// left out, which doesn't change the layout of the ones before.
/// </remarks>
internal static unsafe partial class CoreAudio
{
    /// <summary>HRESULT for "element not found": there is no capture device.</summary>
    internal const int NoDevice = unchecked((int)0x80070490);

    private const int Capture = 1;          // eCapture
    private const int ConsoleRole = 0;      // eConsole: the "default device" in sound settings
    private const uint InProcess = 0x1;     // CLSCTX_INPROC_SERVER

    private static readonly Guid MMDeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>Access to the capture endpoints; keep one and dispose it when done.</summary>
    internal sealed class Microphones : IDisposable
    {
        private IMMDeviceEnumerator? _enumerator = Create<IMMDeviceEnumerator>(MMDeviceEnumeratorClassId);

        /// <summary>Id of the current default microphone; an HRESULT (<see cref="NoDevice"/> when none).</summary>
        internal int DefaultId(out string? id)
        {
            id = null;
            var result = Enumerator.GetDefaultAudioEndpoint(Capture, ConsoleRole, out var device);
            if (result != 0 || device is null)
                return result != 0 ? result : NoDevice;
            try
            {
                return device.GetId(out id);
            }
            finally
            {
                Release(device);
            }
        }

        /// <summary>The volume control (with the mute flag) of a microphone by id.</summary>
        internal int OpenVolume(string id, out IAudioEndpointVolume? volume)
        {
            volume = null;
            var result = Enumerator.GetDevice(id, out var device);
            if (result != 0 || device is null)
                return result != 0 ? result : NoDevice;
            try
            {
                var volumeId = typeof(IAudioEndpointVolume).GUID;
                result = device.Activate(in volumeId, InProcess, IntPtr.Zero, out var instance);
                if (result == 0)
                    volume = Wrap<IAudioEndpointVolume>(instance);
                return result;
            }
            finally
            {
                Release(device);
            }
        }

        private IMMDeviceEnumerator Enumerator => _enumerator ?? throw new ObjectDisposedException(nameof(Microphones));

        public void Dispose()
        {
            Release(_enumerator);
            _enumerator = null;
        }
    }

    /// <summary>The mute flag carried by a volume notification (AUDIO_VOLUME_NOTIFICATION_DATA.bMuted).</summary>
    internal static bool IsMutedIn(IntPtr notification)
        => ((VolumeNotification*)notification)->Muted != 0;

    /// <summary>Releases a COM object now instead of waiting for the garbage collector.</summary>
    internal static void Release(object? comObject)
    {
        if (comObject is ComObject wrapper)
            wrapper.FinalRelease();
    }

    private static T Create<T>(Guid classId)
    {
        var interfaceId = typeof(T).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in classId, IntPtr.Zero, InProcess, in interfaceId, out var instance));
        return Wrap<T>(instance);
    }

    /// <summary>Turns a raw interface pointer into a managed wrapper, taking over its reference.</summary>
    private static T Wrap<T>(IntPtr pointer)
    {
        try
        {
            return ComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)
                   ?? throw new InvalidOperationException($"{typeof(T).Name} is unavailable.");
        }
        finally
        {
            ComInterfaceMarshaller<T>.Free((void*)pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeNotification
    {
        public Guid EventContext;
        public int Muted;
    }

    [LibraryImport(Libraries.Ole32)]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
        [PreserveSig] int GetDevice(string id, out IMMDevice? device);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid interfaceId, uint context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId(out string? id);
    }

    [GeneratedComInterface]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    internal partial interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float decibels, in Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float scalar, in Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float decibels);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float scalar);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float decibels, in Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float scalar, in Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float decibels);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float scalar);
        [PreserveSig] int SetMute(int muted, IntPtr context);
        [PreserveSig] int GetMute(out int muted);
    }

    [GeneratedComInterface]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    internal partial interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notification);
    }
}
