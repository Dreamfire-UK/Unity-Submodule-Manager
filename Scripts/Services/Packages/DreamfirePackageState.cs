using DreamfireSubmodules.Scripts.DreamfireManifest;

namespace DreamfireSubmodules.Scripts.Services.Packages
{
    public enum DreamfirePackageStatus
    {
        Unknown = 0,
        Installed = 1,
        Missing = 2,
        Outdated = 3,
        Invalid = 4
    }

    public sealed class DreamfirePackageState
    {
        public DreamfirePackageRequirement Requirement { get; }
        public string InstalledVersion { get; }
        public DreamfirePackageStatus Status { get; }

        public DreamfirePackageState(
            DreamfirePackageRequirement requirement,
            string installedVersion,
            DreamfirePackageStatus status)
        {
            Requirement = requirement;
            InstalledVersion = installedVersion ?? string.Empty;
            Status = status;
        }
    }
}
