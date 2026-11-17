using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Veyro;

internal static class OutputPolicy
{
    public static void Select(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            policy.SetDefaultEndpoint(deviceId, Role.Multimedia);
            policy.SetDefaultEndpoint(deviceId, Role.Console);
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Keep the ten preceding COM slots in their original order.
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);
    }
}
