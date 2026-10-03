#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DreamfireSubmodules.Editor
{
    internal sealed class GitSubmoduleDashboardService
    {
        private const int GitTimeoutMilliseconds = 120000;

        public async Task<GitOperationResult> SwitchBranchAsync(
            GitSubmoduleSnapshot module, GitBranchOption branch, CancellationToken cancellationToken)
        {
            if (!module.Initialised || !module.Branches.Contains(branch))
                return GitOperationResult.Fail("Refresh the submodule before selecting a branch.");

            GitProcessResult result;
            if (!branch.IsRemote)
            {
                result = await RunGitAsync(module.FullPath, cancellationToken,
                    "switch", "--no-guess", "--", branch.Name);
            }
            else
            {
                // Reuse only a branch that already tracks this exact remote ref.
                GitProcessResult locals = await RunGitAsync(module.FullPath, cancellationToken,
                    "for-each-ref", "--format=%(refname:strip=2)%09%(upstream)", "refs/heads/");
                if (!locals.Success)
                    return GitOperationResult.Fail("Could not read local branches.", FriendlyError(locals));
                string local = SplitLines(locals.Output)
                    .Select(line => line.Split('\t'))
                    .Where(parts => parts.Length == 2 && parts[1] == branch.Reference)
                    .Select(parts => parts[0])
                    .OrderBy(name => name == module.CurrentBranch ? 0 : 1)
                    .FirstOrDefault();
                result = local != null
                    ? await RunGitAsync(module.FullPath, cancellationToken,
                        "switch", "--no-guess", "--", local)
                    : await RunGitAsync(module.FullPath, cancellationToken,
                        "switch", "--track", "--", branch.Reference);
            }

            if (!result.Success)
                return GitOperationResult.Fail("Could not switch branches. Git has preserved your local work.",
                    FriendlyError(result));
            return await SaveBranchAsync(module, cancellationToken);
        }

        private async Task<GitOperationResult> SaveBranchAsync(
            GitSubmoduleSnapshot module, CancellationToken cancellationToken)
        {
            GitProcessResult current = await RunGitAsync(module.FullPath, cancellationToken,
                "branch", "--show-current");
            if (!current.Success || string.IsNullOrWhiteSpace(current.Output))
                return GitOperationResult.Fail("Could not save the current branch.", FriendlyError(current));
            GitProcessResult upstream = await RunGitAsync(module.FullPath, cancellationToken,
                "rev-parse", "--symbolic-full-name", "@{upstream}");
            string key = "submodule." + module.ConfigName;
            GitProcessResult saved = await RunGitAsync(module.RepositoryRoot, cancellationToken,
                "config", "--file", ".gitmodules", key + ".branch", current.Output.Trim());
            if (saved.Success)
                saved = await RunGitAsync(module.RepositoryRoot, cancellationToken,
                    "config", "--file", ".gitmodules", key + ".checkoutRemote",
                    upstream.Success ? upstream.Output.Trim() : string.Empty);
            return saved.Success
                ? GitOperationResult.Ok($"Switched {module.Name} to {current.Output.Trim()} and saved the branch in .gitmodules.")
                : GitOperationResult.Fail("Branch switched, but its setup preference could not be saved.", FriendlyError(saved));
        }

        public async Task<GitRepositorySnapshot> InspectAsync(
            string unityProjectRoot,
            bool checkRemote,
            bool checkAllRepositorySubmodules,
            CancellationToken cancellationToken,
            Action<string> reportProgress = null)
        {
            string projectRoot = Path.GetFullPath(unityProjectRoot);
            GitProcessResult rootResult = await RunGitAsync(
                projectRoot, cancellationToken, "rev-parse", "--show-toplevel");
            if (!rootResult.Success)
            {
                throw new InvalidOperationException(
                    "This Unity project is not inside a Git repository. " + FriendlyError(rootResult));
            }

            string repositoryRoot = Path.GetFullPath(rootResult.Output.Trim());
            string modulesFile = Path.Combine(repositoryRoot, ".gitmodules");
            List<GitSubmoduleSnapshot> modules = File.Exists(modulesFile)
                ? ParseGitModules(modulesFile, repositoryRoot, projectRoot)
                : new List<GitSubmoduleSnapshot>();
            GitRepositorySnapshot snapshot = new GitRepositorySnapshot
            {
                RepositoryRoot = repositoryRoot,
                UnityProjectRoot = projectRoot,
                ProjectRelativePath = ToGitPath(Path.GetRelativePath(repositoryRoot, projectRoot)),
                Submodules = modules
            };

            await InspectParentAsync(snapshot, cancellationToken);
            for (int index = 0; index < modules.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GitSubmoduleSnapshot module = modules[index];
                if (!module.IsInUnityProject && !checkAllRepositorySubmodules)
                {
                    continue;
                }

                bool checkThisRemote = checkRemote &&
                                       (module.IsInUnityProject || checkAllRepositorySubmodules);
                reportProgress?.Invoke(
                    (checkThisRemote ? "Checking" : "Reading") +
                    $" {module.Name} ({index + 1}/{modules.Count})...");
                await InspectModuleAsync(module, checkThisRemote, cancellationToken);
            }

            return snapshot;
        }

        public async Task<GitOperationResult> InitialiseAsync(
            string repositoryRoot,
            IEnumerable<GitSubmoduleSnapshot> modules,
            CancellationToken cancellationToken,
            Action<string> reportProgress = null)
        {
            List<GitSubmoduleSnapshot> targets = modules.Where(module => !module.Initialised).ToList();
            if (targets.Count == 0)
            {
                return GitOperationResult.Ok("Every selected submodule is already set up.");
            }

            reportProgress?.Invoke($"Setting up {targets.Count} submodule(s)...");
            GitProcessResult sync = await RunGitAsync(
                repositoryRoot, cancellationToken, "submodule", "sync", "--recursive");
            if (!sync.Success)
            {
                return GitOperationResult.Fail("Could not sync submodule settings.", FriendlyError(sync));
            }

            List<string> arguments = new List<string>
            {
                "submodule", "update", "--init", "--recursive", "--"
            };
            arguments.AddRange(targets.Select(module => module.RelativePath));
            GitProcessResult update = await RunGitAsync(
                repositoryRoot, cancellationToken, arguments.ToArray());
            return update.Success
                ? GitOperationResult.Ok($"Set up {targets.Count} submodule(s).")
                : GitOperationResult.Fail("Git could not set up every submodule.", FriendlyError(update));
        }

        public async Task<GitOperationResult> SetupProjectAsync(
            string repositoryRoot,
            IEnumerable<GitSubmoduleSnapshot> modules,
            CancellationToken cancellationToken,
            Action<string> reportProgress = null)
        {
            List<GitSubmoduleSnapshot> targets = modules.ToList();
            if (targets.Count == 0)
            {
                return GitOperationResult.Fail(
                    "This Unity project has no submodules configured in .gitmodules.");
            }

            // Capture attached branches before setup; never detach existing working copies.
            foreach (GitSubmoduleSnapshot module in targets.Where(item => item.Initialised))
            {
                GitProcessResult current = await RunGitAsync(module.FullPath, cancellationToken,
                    "branch", "--show-current");
                if (current.Success && !string.IsNullOrWhiteSpace(current.Output))
                {
                    GitOperationResult saved = await SaveBranchAsync(module, cancellationToken);
                    if (!saved.Success) return saved;
                }
            }

            GitOperationResult initialised = await InitialiseAsync(
                repositoryRoot, targets, cancellationToken, reportProgress);
            if (!initialised.Success) return initialised;

            List<string> failures = new List<string>();
            foreach (GitSubmoduleSnapshot module in targets)
            {
                reportProgress?.Invoke($"Restoring branch for {module.Name}...");
                string key = "submodule." + module.ConfigName;
                GitProcessResult saved = await RunGitAsync(repositoryRoot, cancellationToken,
                    "config", "--file", ".gitmodules", "--get", key + ".branch");
                if (!saved.Success || string.IsNullOrWhiteSpace(saved.Output))
                {
                    GitProcessResult nested = await RunGitAsync(module.FullPath, cancellationToken,
                        "submodule", "update", "--init", "--recursive");
                    if (!nested.Success) failures.Add(module.Name + ": " + FriendlyError(nested));
                    continue;
                }
                string branchName = saved.Output.Trim();
                if (branchName == ".")
                {
                    GitProcessResult parent = await RunGitAsync(repositoryRoot, cancellationToken,
                        "branch", "--show-current");
                    branchName = parent.Output.Trim();
                    if (!parent.Success || branchName.Length == 0)
                    {
                        failures.Add(module.Name + ": the parent repository has no current branch.");
                        continue;
                    }
                }
                GitProcessResult local = await RunGitAsync(module.FullPath, cancellationToken,
                    "show-ref", "--verify", "--quiet", "refs/heads/" + branchName);
                GitProcessResult switched;
                if (local.Success)
                    switched = await RunGitAsync(module.FullPath, cancellationToken,
                        "switch", "--no-guess", "--", branchName);
                else
                {
                    GitProcessResult remote = await RunGitAsync(repositoryRoot, cancellationToken,
                        "config", "--file", ".gitmodules", "--get", key + ".checkoutRemote");
                    string reference = remote.Success && !string.IsNullOrWhiteSpace(remote.Output)
                        ? remote.Output.Trim() : "refs/remotes/origin/" + branchName;
                    switched = await RunGitAsync(module.FullPath, cancellationToken,
                        "switch", "-c", branchName, "--track", "--", reference);
                }
                if (!switched.Success) failures.Add(module.Name + ": " + FriendlyError(switched));
                else
                {
                    GitProcessResult nested = await RunGitAsync(module.FullPath, cancellationToken,
                        "submodule", "update", "--init", "--recursive");
                    if (!nested.Success) failures.Add(module.Name + ": " + FriendlyError(nested));
                }
            }
            return failures.Count == 0
                ? GitOperationResult.Ok("Project setup complete. Saved branches restored.")
                : GitOperationResult.Fail("Some submodule branches could not be restored.",
                    string.Join(Environment.NewLine, failures));
        }

        public GitOperationResult OpenTerminal(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return GitOperationResult.Fail("The folder does not exist yet. Set up the submodule first.");
            }

            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    try
                    {
                        StartTerminalProcess(
                            "wt.exe",
                            "-d \"" + directory.Replace("\"", "\"\"") + "\"");
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        StartTerminalProcess(
                            "cmd.exe",
                            "/K cd /d \"" + directory.Replace("\"", "\"\"") + "\"");
                    }
                }
                else if (Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    StartTerminalProcess(
                        "open",
                        "-a Terminal \"" + directory.Replace("\"", "\\\"") + "\"");
                }
                else
                {
                    StartTerminalProcess(
                        "x-terminal-emulator",
                        "--working-directory=\"" + directory.Replace("\"", "\\\"") + "\"");
                }

                return GitOperationResult.Ok("Terminal opened in " + directory);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException ||
                      exception is System.ComponentModel.Win32Exception ||
                      exception is NotSupportedException)
            {
                return GitOperationResult.Fail(
                    "Could not open a terminal in this folder.",
                    exception.Message);
            }
        }

        public async Task<GitOperationResult> UpdateAsync(
            IEnumerable<GitSubmoduleSnapshot> modules,
            CancellationToken cancellationToken,
            Action<string> reportProgress = null)
        {
            int updated = 0;
            List<string> skipped = new List<string>();
            List<string> failures = new List<string>();

            foreach (GitSubmoduleSnapshot module in modules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reportProgress?.Invoke($"Updating {module.Name}...");
                if (!module.Initialised)
                {
                    skipped.Add(module.Name + " (not set up)");
                    continue;
                }

                GitProcessResult status = await RunGitAsync(
                    module.FullPath, cancellationToken, "status", "--porcelain");
                if (!status.Success)
                {
                    failures.Add(module.Name + ": " + FriendlyError(status));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status.Output))
                {
                    skipped.Add(module.Name + " (has local changes)");
                    continue;
                }

                string branch = NormaliseBranch(module.TrackedBranch);
                GitProcessResult fetch = await RunGitAsync(
                    module.FullPath, cancellationToken, "fetch", "--quiet", "--prune", module.TrackedRemote);
                if (!fetch.Success)
                {
                    failures.Add(module.Name + ": " + FriendlyError(fetch));
                    continue;
                }

                string targetReference = "refs/remotes/" + module.TrackedRemote + "/" + branch;
                GitProcessResult targetExists = await RunGitAsync(
                    module.FullPath, cancellationToken, "show-ref", "--verify", "--quiet", targetReference);
                if (!targetExists.Success)
                {
                    failures.Add(module.Name + $": {module.TrackedRemote}/{branch} was not found.");
                    continue;
                }

                GitProcessResult relation = await RunGitAsync(
                    module.FullPath, cancellationToken, "rev-list", "--left-right", "--count",
                    "HEAD..." + targetReference);
                ParseAheadBehind(relation.Output, out int localAhead, out int remoteAhead);
                if (localAhead > 0)
                {
                    skipped.Add(module.Name + " (has unpublished commits)");
                    continue;
                }

                if (remoteAhead == 0)
                {
                    continue;
                }

                GitProcessResult checkout = string.IsNullOrWhiteSpace(module.CurrentBranch)
                    ? await RunGitAsync(module.FullPath, cancellationToken,
                        "checkout", "--quiet", "--detach", targetReference)
                    : await RunGitAsync(module.FullPath, cancellationToken,
                        "merge", "--ff-only", targetReference);
                if (checkout.Success) updated++;
                else failures.Add(module.Name + ": " + FriendlyError(checkout));
            }

            StringBuilder details = new StringBuilder();
            if (skipped.Count > 0)
            {
                details.AppendLine("Skipped to protect your work:");
                foreach (string item in skipped) details.AppendLine("- " + item);
            }

            if (failures.Count > 0)
            {
                if (details.Length > 0) details.AppendLine();
                details.AppendLine("Problems:");
                foreach (string failure in failures) details.AppendLine("- " + failure);
                return GitOperationResult.Fail(
                    $"Updated {updated} submodule(s), but {failures.Count} failed.",
                    details.ToString().Trim());
            }

            string message = updated == 0
                ? "No selected submodules needed a safe update."
                : $"Updated {updated} submodule(s). Save & Push All when you are ready to publish the new versions.";
            return GitOperationResult.Ok(message, details.ToString().Trim());
        }

        public async Task<GitOperationResult> SaveAndPushAsync(
            string repositoryRoot,
            IEnumerable<GitSubmoduleSnapshot> modules,
            string commitMessage,
            CancellationToken cancellationToken,
            Action<string> reportProgress = null)
        {
            string message = (commitMessage ?? string.Empty).Trim();
            if (message.Length == 0)
                return GitOperationResult.Fail("Enter a short description of your changes first.");

            List<GitSubmoduleSnapshot> targets = modules.Where(module => module.Initialised).ToList();
            List<string> published = new List<string>();
            List<string> failures = new List<string>();

            foreach (GitSubmoduleSnapshot module in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reportProgress?.Invoke($"Saving {module.Name}...");
                GitProcessResult status = await RunGitAsync(
                    module.FullPath, cancellationToken, "status", "--porcelain");
                if (!status.Success)
                {
                    failures.Add(module.Name + ": " + FriendlyError(status));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status.Output))
                {
                    GitProcessResult add = await RunGitAsync(
                        module.FullPath, cancellationToken, "add", "--all");
                    if (!add.Success)
                    {
                        failures.Add(module.Name + ": " + FriendlyError(add));
                        continue;
                    }

                    GitProcessResult commit = await RunGitAsync(
                        module.FullPath, cancellationToken, "commit", "-m", message);
                    if (!commit.Success)
                    {
                        failures.Add(module.Name + ": " + FriendlyError(commit));
                        continue;
                    }
                }

                string branch = NormaliseBranch(module.TrackedBranch);
                GitProcessResult aheadResult = await RunGitAsync(
                    module.FullPath, cancellationToken, "rev-list", "--count",
                    "refs/remotes/" + module.TrackedRemote + "/" + branch + "..HEAD");
                int ahead = Math.Max(ParseCount(aheadResult.Output), module.LocalAhead);
                if (ahead == 0 && string.IsNullOrWhiteSpace(status.Output)) continue;

                reportProgress?.Invoke($"Publishing {module.Name}...");
                GitProcessResult push = await RunGitAsync(
                    module.FullPath, cancellationToken, "push", module.TrackedRemote, "HEAD:refs/heads/" + branch);
                if (push.Success) published.Add(module.Name);
                else failures.Add(module.Name + ": " + FriendlyError(push));
            }

            if (failures.Count == 0)
            {
                reportProgress?.Invoke("Saving submodule versions in the parent repository...");
                List<string> paths = targets.Select(module => module.RelativePath).ToList();
                paths.Insert(0, ".gitmodules");
                List<string> statusArguments = new List<string> { "status", "--porcelain", "--" };
                statusArguments.AddRange(paths);
                GitProcessResult parentStatus = await RunGitAsync(
                    repositoryRoot, cancellationToken, statusArguments.ToArray());
                if (!parentStatus.Success)
                {
                    failures.Add("Parent repository: " + FriendlyError(parentStatus));
                }
                else if (!string.IsNullOrWhiteSpace(parentStatus.Output))
                {
                    List<string> commitArguments = new List<string>
                    {
                        "commit", "-m", message, "--only", "--"
                    };
                    commitArguments.AddRange(paths);
                    GitProcessResult parentCommit = await RunGitAsync(
                        repositoryRoot, cancellationToken, commitArguments.ToArray());
                    if (!parentCommit.Success)
                        failures.Add("Parent repository: " + FriendlyError(parentCommit));
                }
            }

            if (failures.Count == 0)
            {
                reportProgress?.Invoke("Publishing the parent repository...");
                GitProcessResult upstream = await RunGitAsync(
                    repositoryRoot, cancellationToken, "rev-parse", "--verify", "@{upstream}");
                GitProcessResult parentPush = upstream.Success
                    ? await RunGitAsync(repositoryRoot, cancellationToken, "push")
                    : await RunGitAsync(repositoryRoot, cancellationToken, "push", "-u", "origin", "HEAD");
                if (!parentPush.Success)
                    failures.Add("Parent repository: " + FriendlyError(parentPush));
            }

            if (failures.Count > 0)
            {
                return GitOperationResult.Fail(
                    "Some changes could not be published. Nothing was force-pushed or discarded.",
                    string.Join(Environment.NewLine, failures.Select(failure => "- " + failure)));
            }

            string summary = published.Count == 0
                ? "Submodule versions and the parent repository are published."
                : $"Saved and published {published.Count} submodule(s), then published the parent repository.";
            return GitOperationResult.Ok(summary);
        }

        public async Task<GitOperationResult> AddAsync(
            string repositoryRoot,
            string unityProjectRoot,
            string repositoryUrl,
            string projectRelativePath,
            string branch,
            CancellationToken cancellationToken,
            Action<string> reportProgress = null)
        {
            string url = (repositoryUrl ?? string.Empty).Trim();
            string relativePath = ToGitPath((projectRelativePath ?? string.Empty).Trim());
            string targetBranch = NormaliseBranch(branch);
            bool isScpStyleSsh = Regex.IsMatch(url, "^[^\\s@]+@[^\\s:]+:.+$");
            bool isSupportedUri = Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUrl) &&
                                  (parsedUrl.Scheme == Uri.UriSchemeHttps ||
                                   parsedUrl.Scheme == Uri.UriSchemeHttp ||
                                   parsedUrl.Scheme == "ssh" ||
                                   parsedUrl.Scheme == "git");
            if (!isSupportedUri && !isScpStyleSsh)
                return GitOperationResult.Fail("Enter a valid HTTP, HTTPS, or SSH Git repository URL.");

            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                return GitOperationResult.Fail("Enter a folder relative to this Unity project, such as Assets/MyLibrary.");

            string fullTarget = Path.GetFullPath(Path.Combine(unityProjectRoot, relativePath));
            if (!IsInside(unityProjectRoot, fullTarget))
                return GitOperationResult.Fail("The submodule folder must stay inside this Unity project.");

            string rootRelativePath = ToGitPath(Path.GetRelativePath(repositoryRoot, fullTarget));
            reportProgress?.Invoke("Adding the new submodule...");
            GitProcessResult add = await RunGitAsync(
                repositoryRoot, cancellationToken, "submodule", "add", "-b", targetBranch,
                "--", url, rootRelativePath);
            return add.Success
                ? GitOperationResult.Ok("Submodule added. Use Save & Push All to share it with the team.")
                : GitOperationResult.Fail("Git could not add the submodule.", FriendlyError(add));
        }

        private async Task InspectParentAsync(
            GitRepositorySnapshot snapshot, CancellationToken cancellationToken)
        {
            GitProcessResult branch = await RunGitAsync(
                snapshot.RepositoryRoot, cancellationToken, "branch", "--show-current");
            snapshot.ParentBranch = branch.Output.Trim();
            GitProcessResult counts = await RunGitAsync(
                snapshot.RepositoryRoot, cancellationToken, "rev-list", "--left-right", "--count",
                "HEAD...@{upstream}");
            ParseAheadBehind(counts.Output, out int ahead, out int behind);
            snapshot.ParentAhead = ahead;
            snapshot.ParentBehind = behind;
            GitProcessResult config = await RunGitAsync(snapshot.RepositoryRoot, cancellationToken,
                "status", "--porcelain", "--", ".gitmodules");
            snapshot.BranchSettingsChanged = config.Success && !string.IsNullOrWhiteSpace(config.Output);
        }

        private async Task InspectModuleAsync(
            GitSubmoduleSnapshot module, bool checkRemote, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(module.FullPath))
            {
                module.Initialised = false;
                return;
            }

            GitProcessResult isRepository = await RunGitAsync(
                module.FullPath, cancellationToken, "rev-parse", "--is-inside-work-tree");
            module.Initialised = (File.Exists(Path.Combine(module.FullPath, ".git")) ||
                                  Directory.Exists(Path.Combine(module.FullPath, ".git"))) && isRepository.Success &&
                                 string.Equals(isRepository.Output.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            if (!module.Initialised) return;

            GitProcessResult branch = await RunGitAsync(
                module.FullPath, cancellationToken, "branch", "--show-current");
            module.CurrentBranch = branch.Output.Trim();
            if (!string.IsNullOrWhiteSpace(module.CurrentBranch))
            {
                // An attached branch takes precedence over the .gitmodules default.
                module.TrackedBranch = module.CurrentBranch;
                GitProcessResult upstream = await RunGitAsync(module.FullPath, cancellationToken,
                    "for-each-ref", "--format=%(upstream:remotename)%09%(upstream:remoteref)",
                    "refs/heads/" + module.CurrentBranch);
                string[] parts = upstream.Output.Trim().Split('\t');
                if (upstream.Success && parts.Length == 2 && parts[0] != "." &&
                    parts[1].StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    module.TrackedRemote = parts[0];
                    module.TrackedBranch = parts[1].Substring("refs/heads/".Length);
                }
            }
            GitProcessResult commit = await RunGitAsync(
                module.FullPath, cancellationToken, "rev-parse", "--short=8", "HEAD");
            module.Commit = commit.Output.Trim();
            GitProcessResult status = await RunGitAsync(
                module.FullPath, cancellationToken, "status", "--porcelain");
            if (!status.Success)
            {
                module.Error = FriendlyError(status);
                return;
            }

            module.ChangeCount = SplitLines(status.Output).Length;
            module.Dirty = module.ChangeCount > 0;
            string branchName = NormaliseBranch(module.TrackedBranch);
            if (checkRemote)
            {
                GitProcessResult fetch = await RunGitAsync(
                    module.FullPath, cancellationToken, "fetch", "--quiet", "--prune", "--all");
                if (!fetch.Success) module.Error = "Update check failed: " + FriendlyError(fetch);
            }

            GitProcessResult branches = await RunGitAsync(module.FullPath, cancellationToken,
                "for-each-ref", "--sort=refname", "--format=%(refname)%09%(symref)",
                "refs/heads/", "refs/remotes/");
            if (!branches.Success) module.Error = "Could not read branches: " + FriendlyError(branches);
            else foreach (string line in SplitLines(branches.Output))
            {
                string[] parts = line.Split('\t');
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1])) continue;
                bool remote = parts[0].StartsWith("refs/remotes/", StringComparison.Ordinal);
                module.Branches.Add(new GitBranchOption
                {
                    Reference = parts[0],
                    Name = parts[0].Substring(remote ? "refs/remotes/".Length : "refs/heads/".Length),
                    IsRemote = remote
                });
            }

            GitProcessResult counts = await RunGitAsync(
                module.FullPath, cancellationToken, "rev-list", "--left-right", "--count",
                "HEAD...refs/remotes/" + module.TrackedRemote + "/" + branchName);
            if (counts.Success)
            {
                ParseAheadBehind(counts.Output, out int ahead, out int behind);
                module.LocalAhead = ahead;
                module.RemoteAhead = behind;
            }
            else if (string.IsNullOrWhiteSpace(module.Error))
            {
                module.Error = $"Could not compare with {module.TrackedRemote}/{branchName}.";
            }

            GitProcessResult pointer = await RunGitAsync(
                module.RepositoryRoot, cancellationToken, "status", "--porcelain", "--", module.RelativePath);
            module.ParentPointerChanged = pointer.Success && !string.IsNullOrWhiteSpace(pointer.Output);
        }

        private static List<GitSubmoduleSnapshot> ParseGitModules(
            string modulesFile, string repositoryRoot, string projectRoot)
        {
            Regex sectionPattern = new Regex(
                "^\\s*\\[submodule\\s+\"(?<name>.*)\"\\]\\s*$", RegexOptions.Compiled);
            Regex valuePattern = new Regex(
                "^\\s*(?<key>path|url|branch)\\s*=\\s*(?<value>.*)\\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            List<GitSubmoduleSnapshot> modules = new List<GitSubmoduleSnapshot>();
            GitSubmoduleSnapshot current = null;
            foreach (string line in File.ReadAllLines(modulesFile))
            {
                Match section = sectionPattern.Match(line);
                if (section.Success)
                {
                    if (current != null && !string.IsNullOrWhiteSpace(current.RelativePath))
                    {
                        CompleteModule(current, repositoryRoot, projectRoot);
                        modules.Add(current);
                    }
                    current = new GitSubmoduleSnapshot
                    {
                        Name = section.Groups["name"].Value,
                        ConfigName = section.Groups["name"].Value
                    };
                    continue;
                }

                if (current == null) continue;
                Match value = valuePattern.Match(line);
                if (!value.Success) continue;
                string text = value.Groups["value"].Value.Trim();
                switch (value.Groups["key"].Value.ToLowerInvariant())
                {
                    case "path": current.RelativePath = ToGitPath(text); break;
                    case "url": current.Url = text; break;
                    case "branch": current.TrackedBranch = text; break;
                }
            }

            if (current != null && !string.IsNullOrWhiteSpace(current.RelativePath))
            {
                CompleteModule(current, repositoryRoot, projectRoot);
                modules.Add(current);
            }

            return modules
                .OrderByDescending(module => module.IsInUnityProject)
                .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void CompleteModule(
            GitSubmoduleSnapshot module, string repositoryRoot, string projectRoot)
        {
            module.RepositoryRoot = repositoryRoot;
            module.FullPath = Path.GetFullPath(Path.Combine(repositoryRoot, module.RelativePath));
            module.IsInUnityProject = IsInside(projectRoot, module.FullPath);
            module.ProjectRelativePath = module.IsInUnityProject
                ? ToGitPath(Path.GetRelativePath(projectRoot, module.FullPath))
                : module.RelativePath;
            module.TrackedBranch = NormaliseBranch(module.TrackedBranch);
            if (string.IsNullOrWhiteSpace(module.Name)) module.Name = Path.GetFileName(module.FullPath);
            else if (module.Name.Contains('/')) module.Name = Path.GetFileName(module.Name);
        }

        private static async Task<GitProcessResult> RunGitAsync(
            string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
        {
            return await Task.Run(
                () => RunGit(workingDirectory, cancellationToken, arguments), cancellationToken);
        }

        private static void StartTerminalProcess(string executable, string arguments)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true
            });
        }

        private static GitProcessResult RunGit(
            string workingDirectory, CancellationToken cancellationToken, string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            // Background status reads should not compete with repository writers.
            if (arguments.Length > 0 && arguments[0] == "status")
                startInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

            using Process process = new Process { StartInfo = startInfo };
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null) output.AppendLine(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null) error.AppendLine(eventArgs.Data);
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!process.Start()) return GitProcessResult.Failed("Git could not be started.");
                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Stopwatch timer = Stopwatch.StartNew();
                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested ||
                        timer.ElapsedMilliseconds > GitTimeoutMilliseconds)
                    {
                        TryKill(process);
                        return GitProcessResult.Failed(cancellationToken.IsCancellationRequested
                            ? "Git operation was cancelled."
                            : "Git did not finish within two minutes.");
                    }
                    process.WaitForExit(50);
                }
                process.WaitForExit();
                return new GitProcessResult(
                    process.ExitCode, output.ToString().Trim(), error.ToString().Trim());
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
            catch (Exception exception)
                when (exception is InvalidOperationException ||
                      exception is System.ComponentModel.Win32Exception ||
                      exception is IOException || exception is UnauthorizedAccessException ||
                      exception is NotSupportedException)
            {
                TryKill(process);
                return GitProcessResult.Failed(exception.Message);
            }
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch { }
        }

        private static void ParseAheadBehind(string output, out int ahead, out int behind)
        {
            ahead = 0;
            behind = 0;
            string[] values = (output ?? string.Empty).Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 2)
            {
                int.TryParse(values[0], out ahead);
                int.TryParse(values[1], out behind);
            }
        }

        private static int ParseCount(string output)
        {
            int.TryParse((output ?? string.Empty).Trim(), out int count);
            return count;
        }

        private static string[] SplitLines(string value)
        {
            return (value ?? string.Empty).Split(
                new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormaliseBranch(string branch)
        {
            string value = (branch ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) || value == "." ? "main" : value;
        }

        private static string FriendlyError(GitProcessResult result)
        {
            string message = !string.IsNullOrWhiteSpace(result.Error) ? result.Error : result.Output;
            return string.IsNullOrWhiteSpace(message)
                ? $"Git exited with code {result.ExitCode}."
                : message.Trim();
        }

        private static string ToGitPath(string path) => (path ?? string.Empty).Replace('\\', '/');

        private static bool IsInside(string parentPath, string candidatePath)
        {
            string parent = Path.GetFullPath(parentPath).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidatePath).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class GitRepositorySnapshot
    {
        public string RepositoryRoot = string.Empty;
        public string UnityProjectRoot = string.Empty;
        public string ProjectRelativePath = string.Empty;
        public string ParentBranch = string.Empty;
        public int ParentAhead;
        public int ParentBehind;
        public bool BranchSettingsChanged;
        public List<GitSubmoduleSnapshot> Submodules = new List<GitSubmoduleSnapshot>();
    }

    internal sealed class GitSubmoduleSnapshot
    {
        public string ConfigName = string.Empty;
        public string RepositoryRoot = string.Empty;
        public string Name = string.Empty;
        public string RelativePath = string.Empty;
        public string ProjectRelativePath = string.Empty;
        public string FullPath = string.Empty;
        public string Url = string.Empty;
        public string TrackedBranch = "main";
        public string TrackedRemote = "origin";
        public List<GitBranchOption> Branches = new List<GitBranchOption>();
        public string CurrentBranch = string.Empty;
        public string Commit = string.Empty;
        public string Error = string.Empty;
        public bool IsInUnityProject;
        public bool Initialised;
        public bool Dirty;
        public bool ParentPointerChanged;
        public int ChangeCount;
        public int LocalAhead;
        public int RemoteAhead;
        public bool NeedsPublishing => Dirty || LocalAhead > 0 || ParentPointerChanged;
    }

    internal sealed class GitBranchOption
    {
        public string Name;
        public string Reference;
        public bool IsRemote;
    }

    internal readonly struct GitOperationResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly string Details;
        private GitOperationResult(bool success, string message, string details)
        {
            Success = success;
            Message = message ?? string.Empty;
            Details = details ?? string.Empty;
        }
        public static GitOperationResult Ok(string message, string details = "") =>
            new GitOperationResult(true, message, details);
        public static GitOperationResult Fail(string message, string details = "") =>
            new GitOperationResult(false, message, details);
    }

    internal readonly struct GitProcessResult
    {
        public readonly int ExitCode;
        public readonly string Output;
        public readonly string Error;
        public bool Success => ExitCode == 0;
        public GitProcessResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
        }
        public static GitProcessResult Failed(string error) =>
            new GitProcessResult(-1, string.Empty, error);
    }
}

#endif
