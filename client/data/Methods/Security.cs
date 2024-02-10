using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace specify_client.data;

public static partial class Cache
{
    public static async System.Threading.Tasks.Task MakeSecurityData()
    {
        try
        {
            DebugLog.Region region = DebugLog.Region.Security;
            DebugLog.StartRegion(region);
            AvList = AVList();
            FwList = Utils.GetWmi("FirewallProduct", "displayName", @"root\SecurityCenter2")
                .Select(x => (string)x["displayName"]).ToList();
            DebugLog.LogEvent("Security WMI Information Retrieved.", region);

            if (Environment.GetEnvironmentVariable("firmware_type")!.Equals("UEFI"))
            {
                var secBootEnabled = Utils.GetRegistryValue<int?>(
                    Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State",
                    "UEFISecureBootEnabled");

                if (secBootEnabled == null) DebugLog.LogEvent($"Could not get UEFISecureBootEnabled value", region, DebugLog.EventType.ERROR);
                else SecureBootEnabled = secBootEnabled == 1;
            }
            DebugLog.LogEvent("SecureBoot Information Retrieved.", region);

            try
            {
                Tpm = Utils.GetWmi("Win32_Tpm", "*", @"Root\CIMV2\Security\MicrosoftTpm").First();
                Tpm["IsPresent"] = true;
            }
            catch (InvalidOperationException)
            {
                // No TPM
                Tpm = new Dictionary<string, object>() { { "IsPresent", false } };
            }
            catch (ManagementException)
            {
                Tpm = null;
                DebugLog.LogEvent("Security Data: could not get TPM. This is probably because specify was not run as administrator.", region, DebugLog.EventType.WARNING);
            }
            DebugLog.LogEvent("TPM Information Retrieved.", region);

            UacLevel = Utils.GetRegistryValue<int?>(
                Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                "ConsentPromptBehaviorUser");
            var enableLua = Utils.GetRegistryValue<int?>(Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
            if (enableLua == null) DebugLog.LogEvent($"Security data: could not get EnableLUA value", region, DebugLog.EventType.ERROR);
            else UacEnabled = enableLua == 1;
            DebugLog.LogEvent("UAC Information retrieved.", region);

            DebugLog.EndRegion(DebugLog.Region.Security);
        }
        catch (Exception ex)
        {
            DebugLog.LogFatalError($"{ex}", DebugLog.Region.Security);
        }
        SecurityWriteSuccess = true;

        await Task.CompletedTask;
    }

    public static List<string> AVList()
    {
        var antiviruses = Utils.GetWmi("AntivirusProduct", "displayName", @"root\SecurityCenter2")
                            .Select(x => (string)x["displayName"]).ToList();

        // Checks for registry items
        int PassiveMode = Utils.GetRegistryValue<int?>(
                Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender",
                "PassiveMode") ?? 0;

        int DisableAV = Utils.GetRegistryValue<int?>(
                Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender",
                "DisableAntiVirus") ?? 0;

        int DisableASW = Utils.GetRegistryValue<int?>(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender",
                "DisableAntiSpyware") ?? 0;

        int PassiveModePolicies = Utils.GetRegistryValue<int?>(
                Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender",
                "PassiveMode") ?? 0;

        int DisableAVPolicies = Utils.GetRegistryValue<int?>(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender",
                "DisableAntiVirus") ?? 0;

        int DisableASWPolicies = Utils.GetRegistryValue<int?>(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender",
                "DisableAntiSpyware") ?? 0;

        // Move to end of list
        // Check if Defender is disabled in any way
        if (PassiveMode != 0 || DisableAV != 0 || DisableASW != 0)
        {
            antiviruses.RemoveAll(x => x == "Windows Defender");
            antiviruses.Add("Windows Defender (Disabled)");
        }

        // Same, but checks in policies
        else if (PassiveModePolicies != 0 || DisableAVPolicies != 0 || DisableASWPolicies != 0)
        {
            antiviruses.RemoveAll(x => x == "Windows Defender");
            antiviruses.Add("Windows Defender (Disabled)");
        }

        // Check if Defender is not the only entry in list
        else if (antiviruses.Count > 1 && antiviruses.All(a => a == "Windows Defender"))
        {
            antiviruses.RemoveAll(x => x == "Windows Defender");
            antiviruses.Add("Windows Defender");
        }

        return antiviruses;
    }
}