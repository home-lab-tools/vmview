using System.Runtime.InteropServices;
using VmView.Models;

namespace VmView.Services;

/// <summary>
/// "Start with Windows" as a Task Scheduler logon task. The app's manifest asks for the highest
/// available privilege (VM consoles need a Hyper-V administrator), and Windows silently skips
/// HKCU\...\Run entries that would need elevation, so a Run key would never fire. A logon task with
/// RunLevel=Highest starts elevated without a UAC prompt, in the interactive session, for this user only.
/// Task Scheduler is driven through its COM automation object (Schedule.Service) via IDispatch.
/// </summary>
public static class Autostart
{
    public const string TaskName = "VmView";
    public const string TrayArgument = "--tray";

    // Schedule.Service enums
    const int TaskActionExec = 0;
    const int TaskTriggerLogon = 9;
    const int TaskCreateOrUpdate = 6;
    const int TaskLogonInteractiveToken = 3;
    const int TaskRunLevelHighest = 1;
    const int TaskInstancesIgnoreNew = 2;

    static string UserId => $@"{Environment.UserDomainName}\{Environment.UserName}";

    /// <summary>True when the task exists, is enabled, and points at this very exe.</summary>
    public static bool IsEnabled()
    {
        try
        {
            dynamic folder = RootFolder();
            dynamic task = folder.GetTask(TaskName);
            if (!(bool)task.Enabled) return false;
            dynamic actions = task.Definition.Actions;
            for (var i = 1; i <= (int)actions.Count; i++)
            {
                dynamic a = actions.Item(i);
                if ((int)a.Type != TaskActionExec) continue;
                string path = (string)a.Path;
                if (string.Equals(path.Trim('"'), Options.ExePath, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch (COMException) { return false; }   // no such task
        catch (FileNotFoundException) { return false; }
    }

    public static void Enable()
    {
        dynamic service = Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic def = service.NewTask(0);

        def.RegistrationInfo.Description = "VmView — Hyper-V console browser, started into the tray at logon.";
        def.RegistrationInfo.Author = UserId;

        dynamic principal = def.Principal;
        principal.UserId = UserId;
        principal.LogonType = TaskLogonInteractiveToken;
        principal.RunLevel = TaskRunLevelHighest;

        dynamic settings = def.Settings;
        settings.Enabled = true;
        settings.StartWhenAvailable = true;
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.ExecutionTimeLimit = "PT0S";          // the default of 72 h would kill a resident tray app
        settings.MultipleInstances = TaskInstancesIgnoreNew;
        settings.Priority = 5;                         // normal, not the below-normal default for tasks

        dynamic trigger = def.Triggers.Create(TaskTriggerLogon);
        trigger.UserId = UserId;
        trigger.Delay = "PT5S";

        dynamic action = def.Actions.Create(TaskActionExec);
        action.Path = Options.ExePath;
        action.Arguments = TrayArgument;
        action.WorkingDirectory = Options.ExeDirectory;

        folder.RegisterTaskDefinition(TaskName, def, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
    }

    public static void Disable()
    {
        try { RootFolder().DeleteTask(TaskName, 0); }
        catch (COMException) { /* already gone */ }
        catch (FileNotFoundException) { }
    }

    static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    static dynamic RootFolder() => Connect().GetFolder("\\");
}
