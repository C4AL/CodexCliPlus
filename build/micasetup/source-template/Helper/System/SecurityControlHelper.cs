using MicaSetup.Attributes;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MicaSetup.Helper;

[Auth(Auth.Admin)]
public static class SecurityControlHelper
{
    private static readonly SecurityIdentifier EveryoneSid = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier UsersSid = new(WellKnownSidType.BuiltinUsersSid, null);

    [Auth(Auth.Admin)]
    public static void AllowFullFileSecurity(string filePath)
    {
        if (!RuntimeHelper.IsElevated)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Logger.Warning("[Security] file path is empty, skip ACL update.");
            return;
        }

        try
        {
            FileInfo fileInfo = new(filePath);
            if (!fileInfo.Exists)
            {
                Logger.Warning($"[Security] file does not exist, skip ACL update: {filePath}");
                return;
            }

            FileSecurity fileSecurity = fileInfo.GetAccessControl();
            fileSecurity.AddAccessRule(new FileSystemAccessRule(EveryoneSid, FileSystemRights.FullControl, AccessControlType.Allow));
            fileSecurity.AddAccessRule(new FileSystemAccessRule(UsersSid, FileSystemRights.FullControl, AccessControlType.Allow));
            fileInfo.SetAccessControl(fileSecurity);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
    }

    [Auth(Auth.Admin)]
    public static void AllowFullFolderSecurity(string dirPath)
    {
        if (!RuntimeHelper.IsElevated)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dirPath))
        {
            Logger.Warning("[Security] directory path is empty, skip ACL update.");
            return;
        }

        try
        {
            DirectoryInfo dir = new(dirPath);
            if (!dir.Exists)
            {
                Logger.Warning($"[Security] directory does not exist, skip ACL update: {dirPath}");
                return;
            }

            DirectorySecurity dirSecurity = dir.GetAccessControl(AccessControlSections.All);
            InheritanceFlags inherits = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            FileSystemAccessRule everyoneFileSystemAccessRule = new(EveryoneSid, FileSystemRights.FullControl, inherits, PropagationFlags.None, AccessControlType.Allow);
            FileSystemAccessRule usersFileSystemAccessRule = new(UsersSid, FileSystemRights.FullControl, inherits, PropagationFlags.None, AccessControlType.Allow);
            dirSecurity.ModifyAccessRule(AccessControlModification.Add, everyoneFileSystemAccessRule, out _);
            dirSecurity.ModifyAccessRule(AccessControlModification.Add, usersFileSystemAccessRule, out _);
            dir.SetAccessControl(dirSecurity);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
    }
}
