using System;
using System.IO;
using Microsoft.Win32;

namespace CCCC
{
    internal interface IStartupStore
    {
        string Read();
        void Write(string command);
        void Delete();
    }

    internal sealed class WindowsStartupStore : IStartupStore
    {
        internal const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        internal const string EntryName = "CCCC";
        private readonly string subKey;
        internal WindowsStartupStore() { subKey = RunKey; }
#if TESTS
        internal WindowsStartupStore(string isolatedKey)
        {
            if (!isolatedKey.StartsWith("Software\\CCCC\\StartupTests\\", StringComparison.Ordinal))
                throw new ArgumentException("Only an isolated startup test key is allowed.");
            subKey = isolatedKey;
        }
#endif
        public string Read()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                return key == null ? null : key.GetValue(EntryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        public void Write(string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
            {
                if (key == null) throw new IOException("Cannot open the startup registration.");
                key.SetValue(EntryName, command, RegistryValueKind.String);
            }
        }
        public void Delete()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true))
                if (key != null) key.DeleteValue(EntryName, false);
        }
    }

    internal sealed class StartupState
    {
        internal bool Enabled, CurrentLocation;
    }

    internal sealed class StartupRegistration
    {
        private readonly IStartupStore store;
        private readonly string command;
        internal StartupRegistration(IStartupStore store, string executable)
        { this.store = store; command = CommandFor(executable); }
        internal static string CommandFor(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) ||
                executable.IndexOfAny(new char[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("An absolute executable path is required.");
            return "\"" + Path.GetFullPath(executable) + "\"";
        }
        internal StartupState Read()
        {
            string value = store.Read();
            return new StartupState { Enabled = !String.IsNullOrWhiteSpace(value),
                CurrentLocation = String.Equals(value, command, StringComparison.OrdinalIgnoreCase) };
        }
        internal StartupState SetEnabled(bool enabled)
        {
            if (enabled)
            {
                // Run values have a documented 260-character command-line limit.
                if (command.Length > 260) throw new IOException("The startup path is too long.");
                store.Write(command);
            }
            else store.Delete();
            StartupState result = Read();
            if (result.Enabled != enabled || (enabled && !result.CurrentLocation))
                throw new IOException("The startup setting was not saved.");
            return result;
        }
    }
}
