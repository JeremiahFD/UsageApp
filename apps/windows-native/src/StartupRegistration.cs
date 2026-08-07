using System;
using System.IO;
using Microsoft.Win32;

namespace UsageApp.Native
{
    internal interface IStartupRegistry
    {
        string ReadValue();
        void WriteValue(string command);
        void DeleteValue();
    }

    internal sealed class CurrentUserStartupRegistry : IStartupRegistry
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "UsageAppNative";

        public string ReadValue()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                false))
            {
                return key == null
                    ? null
                    : key.GetValue(ValueName, null) as string;
            }
        }

        public void WriteValue(string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "Windows did not open the current-user startup key.");
                }
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }

        public void DeleteValue()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                true))
            {
                if (key != null)
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }

    internal sealed class StartupRegistration
    {
        private readonly IStartupRegistry registry;

        public StartupRegistration()
            : this(new CurrentUserStartupRegistry())
        {
        }

        internal StartupRegistration(IStartupRegistry injectedRegistry)
        {
            if (injectedRegistry == null)
            {
                throw new ArgumentNullException("injectedRegistry");
            }
            registry = injectedRegistry;
        }

        public bool TryIsEnabled(
            string executablePath,
            out bool enabled,
            out string error)
        {
            enabled = false;
            error = null;
            string desired;
            if (!TryBuildCommand(executablePath, out desired, out error))
            {
                return false;
            }
            try
            {
                string current = registry.ReadValue();
                if (CommandsEqual(current, desired))
                {
                    enabled = true;
                    return true;
                }
                if (IsUsageAppNativeCommand(current))
                {
                    // An older install or portable copy can leave this app's
                    // Run value pointing at an executable that no longer
                    // exists. Repair only the value UsageApp owns; foreign
                    // commands remain untouched.
                    registry.WriteValue(desired);
                    enabled = true;
                    return true;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = FriendlyFailure(exception);
                return false;
            }
        }

        public bool TrySetEnabled(
            string executablePath,
            bool enabled,
            out string error)
        {
            error = null;
            string desired;
            if (!TryBuildCommand(executablePath, out desired, out error))
            {
                return false;
            }
            try
            {
                string current = registry.ReadValue();
                if (enabled)
                {
                    if (!string.IsNullOrWhiteSpace(current)
                        && !CommandsEqual(current, desired)
                        && !IsUsageAppNativeCommand(current))
                    {
                        error =
                            "The UsageAppNative startup entry belongs to another command and was left unchanged.";
                        return false;
                    }
                    registry.WriteValue(desired);
                    return true;
                }

                if (string.IsNullOrWhiteSpace(current))
                {
                    return true;
                }
                if (!CommandsEqual(current, desired)
                    && !IsUsageAppNativeCommand(current))
                {
                    error =
                        "The UsageAppNative startup entry belongs to another command and was left unchanged.";
                    return false;
                }
                registry.DeleteValue();
                return true;
            }
            catch (Exception exception)
            {
                error = FriendlyFailure(exception);
                return false;
            }
        }

        internal static bool TryBuildCommand(
            string executablePath,
            out string command,
            out string error)
        {
            command = null;
            error = null;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                error = "The UsageApp executable path is unavailable.";
                return false;
            }
            if (executablePath.IndexOf('"') >= 0)
            {
                error = "The UsageApp executable path contains an unsupported quote character.";
                return false;
            }
            string trimmedPath = executablePath.Trim();
            if (!Path.IsPathRooted(trimmedPath))
            {
                error = "The UsageApp executable path must be absolute.";
                return false;
            }
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(trimmedPath);
            }
            catch (Exception)
            {
                error = "The UsageApp executable path is invalid.";
                return false;
            }
            command = "\"" + fullPath + "\" --background";
            return true;
        }

        internal static bool CommandsEqual(string left, string right)
        {
            return string.Equals(
                string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
                string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsUsageAppNativeCommand(string command)
        {
            string executable = ExecutableFromCommand(command);
            return !string.IsNullOrEmpty(executable)
                && string.Equals(
                    Path.GetFileName(executable),
                    "UsageApp.Native.exe",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string ExecutableFromCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }
            string trimmed = command.Trim();
            if (trimmed[0] == '"')
            {
                int end = trimmed.IndexOf('"', 1);
                return end > 1 ? trimmed.Substring(1, end - 1) : null;
            }
            int separator = trimmed.IndexOf(' ');
            return separator < 0 ? trimmed : trimmed.Substring(0, separator);
        }

        private static string FriendlyFailure(Exception exception)
        {
            if (exception is UnauthorizedAccessException
                || exception is System.Security.SecurityException)
            {
                return "Windows did not allow UsageApp to change your current-user startup setting.";
            }
            return "UsageApp could not update the current-user startup setting.";
        }
    }
}
