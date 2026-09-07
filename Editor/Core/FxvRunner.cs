using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace FlexVault.VCS.Editor.Core
{
    public class FxvResult<T>
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public string RawStdout { get; set; }
        public string RawStderr { get; set; }
    }

    public static class FxvRunner
    {
        private static readonly SemaphoreSlim s_processSemaphore = new SemaphoreSlim(1, 1);

        public static async Task<FxvResult<T>> RunCommandAsync<T>(
            IEnumerable<string> args,
            CancellationToken cancellationToken = default,
            int timeoutMs = 60000)
        {
            var result = new FxvResult<T>();
            string binaryPath = FlexVaultSettings.GetEffectiveBinaryPath();
            string workingDir = FlexVaultSettings.GetRepositoryRoot();

            if (string.IsNullOrEmpty(binaryPath))
            {
                result.Success = false;
                result.ErrorMessage = "FlexVault CLI executable (fxv) could not be located. Check Project Settings > Version Control > FlexVault.";
                return result;
            }

            var fullArgs = new List<string>(args);
            string primaryCommand = fullArgs.Count > 0 ? fullArgs[0].ToLowerInvariant() : string.Empty;
            bool isJsonCommand = primaryCommand != "snapshot" && primaryCommand != "publish";

            if (isJsonCommand && !fullArgs.Contains("--format"))
            {
                fullArgs.Add("--format");
                fullArgs.Add("json");
            }
            if (!fullArgs.Contains("--unattended"))
            {
                fullArgs.Add("--unattended");
            }
            if (!fullArgs.Contains("--no-color"))
            {
                fullArgs.Add("--no-color");
            }

            string commandLineArgs = FormatArguments(fullArgs);

            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = commandLineArgs,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            await s_processSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    var stdoutBuilder = new StringBuilder();
                    var stderrBuilder = new StringBuilder();

                    using (var process = new Process { StartInfo = startInfo })
                    {
                        process.OutputDataReceived += (_, e) =>
                        {
                            if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
                        };
                        process.ErrorDataReceived += (_, e) =>
                        {
                            if (e.Data != null) stderrBuilder.AppendLine(e.Data);
                        };

                        try
                        {
                            process.Start();
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.ErrorMessage = $"Failed to start fxv process ({binaryPath}): {ex.Message}";
                            return result;
                        }

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        bool exited = false;
                        var sw = Stopwatch.StartNew();

                        while (!exited)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                try { process.Kill(); } catch { /* Ignore if already exited */ }
                                result.Success = false;
                                result.ErrorMessage = "Operation was canceled.";
                                return result;
                            }

                            if (sw.ElapsedMilliseconds > timeoutMs)
                            {
                                try { process.Kill(); } catch { /* Ignore */ }
                                result.Success = false;
                                result.ErrorMessage = $"Operation timed out after {timeoutMs / 1000} seconds.";
                                return result;
                            }

                            exited = process.WaitForExit(100);
                        }

                        process.WaitForExit();

                        result.ExitCode = process.ExitCode;
                        result.RawStdout = stdoutBuilder.ToString().Trim();
                        result.RawStderr = stderrBuilder.ToString().Trim();

                        if (!string.IsNullOrWhiteSpace(result.RawStdout))
                        {
                            if (result.RawStdout.StartsWith("{"))
                            {
                                try
                                {
                                    var envelope = JsonConvert.DeserializeObject<OutputEnvelope<T>>(result.RawStdout);
                                    if (envelope != null && envelope.Message != null)
                                    {
                                        if (envelope.Message.Kind == "error")
                                        {
                                            var errorEnvelope = JsonConvert.DeserializeObject<OutputEnvelope<ErrorPayload>>(result.RawStdout);
                                            result.Success = false;
                                            result.ErrorMessage = errorEnvelope?.Message?.Payload?.Message ?? "FlexVault CLI returned an error.";
                                            return result;
                                        }

                                        result.Success = (result.ExitCode == 0);
                                        result.Data = envelope.Message.Payload;
                                        return result;
                                    }
                                }
                                catch (Exception parseEx)
                                {
                                    if (result.ExitCode != 0)
                                    {
                                        result.Success = false;
                                        result.ErrorMessage = !string.IsNullOrEmpty(result.RawStderr) ? result.RawStderr : $"Exit code {result.ExitCode}: {result.RawStdout}";
                                        return result;
                                    }

                                    if (typeof(T) != typeof(object) && typeof(T) != typeof(string))
                                    {
                                        result.Success = false;
                                        result.ErrorMessage = $"Failed to parse CLI JSON response: {parseEx.Message}\nRaw: {result.RawStdout}";
                                        return result;
                                    }
                                }
                            }
                        }

                        if (result.ExitCode == 0)
                        {
                            result.Success = true;
                        }
                        else
                        {
                            result.Success = false;
                            result.ErrorMessage = !string.IsNullOrEmpty(result.RawStderr) ? result.RawStderr : $"Command failed with exit code {result.ExitCode}: {result.RawStdout}";
                        }

                        return result;
                    }
                });
            }
            finally
            {
                s_processSemaphore.Release();
            }
        }

        public static async Task<FxvResult<StatusPayload>> GetStatusAsync(bool skipScan = false, CancellationToken ct = default)
        {
            var args = new List<string> { "status" };
            if (skipScan)
            {
                args.Add("--skip-scan");
                args.Add("--skip-remote-update");
            }

            return await RunCommandAsync<StatusPayload>(args, ct);
        }

        public static async Task<bool> CheckLoggedInAsync(CancellationToken ct = default)
        {
            var result = await GetStatusAsync(skipScan: true, ct: ct);
            return result.Success && !string.IsNullOrEmpty(result.Data?.CurrentUser);
        }

        public static async Task<FxvResult<object>> LoginAsync(string username, CancellationToken ct = default)
        {
            var args = new List<string> { "login", username };
            return await RunCommandAsync<object>(args, ct);
        }

        public static async Task<FxvResult<object>> LogoutAsync(CancellationToken ct = default)
        {
            var args = new List<string> { "logout" };
            return await RunCommandAsync<object>(args, ct);
        }

        public static async Task<FxvResult<object>> SnapshotAsync(string description, CancellationToken ct = default)
        {
            var args = new List<string> { "snapshot" };
            if (!string.IsNullOrEmpty(description))
            {
                args.Add("-d");
                args.Add(description);
            }

            return await RunCommandAsync<object>(args, ct);
        }

        public static async Task<FxvResult<object>> PublishAsync(string description, CancellationToken ct = default)
        {
            var args = new List<string> { "publish" };
            if (!string.IsNullOrEmpty(description))
            {
                args.Add("-d");
                args.Add(description);
            }

            return await RunCommandAsync<object>(args, ct);
        }

        public static async Task<FxvResult<WorkspaceSyncPayload>> SyncAsync(string revision = null, CancellationToken ct = default)
        {
            var args = new List<string> { "sync" };
            if (!string.IsNullOrEmpty(revision))
            {
                args.Add(revision);
            }

            return await RunCommandAsync<WorkspaceSyncPayload>(args, ct);
        }

        public static async Task<FxvResult<WorkspaceSyncPayload>> GotoAsync(string revision, CancellationToken ct = default)
        {
            var args = new List<string> { "goto", revision };
            return await RunCommandAsync<WorkspaceSyncPayload>(args, ct);
        }

        public enum ResolveAction
        {
            Mine,
            Theirs,
            Undo
        }

        public static async Task<FxvResult<WorkspaceSyncPayload>> RevertAsync(IEnumerable<string> repoRelativePaths, CancellationToken ct = default)
        {
            var args = new List<string> { "revert" };
            foreach (string p in repoRelativePaths)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    args.Add(p);
                }
            }

            return await RunCommandAsync<WorkspaceSyncPayload>(args, ct);
        }

        public static async Task<FxvResult<WorkspaceSyncPayload>> ResolveAsync(
            ResolveAction action,
            IEnumerable<string> repoRelativePaths = null,
            CancellationToken ct = default)
        {
            var args = new List<string> { "resolve" };
            switch (action)
            {
                case ResolveAction.Mine:
                    args.Add("--mine");
                    break;
                case ResolveAction.Theirs:
                    args.Add("--theirs");
                    break;
                case ResolveAction.Undo:
                    args.Add("--undo");
                    break;
            }

            var pathList = new List<string>();
            if (repoRelativePaths != null)
            {
                foreach (string p in repoRelativePaths)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        pathList.Add(p);
                    }
                }
            }

            if (pathList.Count > 0)
            {
                args.AddRange(pathList);
            }
            else
            {
                args.Add("--all");
            }

            return await RunCommandAsync<WorkspaceSyncPayload>(args, ct);
        }

        public static async Task<FxvResult<HistoryPayload>> GetHistoryAsync(int? count = 30, string branch = null, CancellationToken ct = default)
        {
            var args = new List<string> { "history" };
            if (count.HasValue && count.Value > 0)
            {
                args.Add("-n");
                args.Add(count.Value.ToString());
            }
            if (!string.IsNullOrEmpty(branch))
            {
                args.Add("-b");
                args.Add(branch);
            }

            return await RunCommandAsync<HistoryPayload>(args, ct);
        }

        public static async Task<bool> CatToFileAsync(string repoRelativePath, string revision, string destinationFilePath, CancellationToken ct = default)
        {
            string binaryPath = FlexVaultSettings.GetEffectiveBinaryPath();
            string workingDir = FlexVaultSettings.GetRepositoryRoot();

            if (string.IsNullOrEmpty(binaryPath))
            {
                UnityEngine.Debug.LogError("[FlexVault] CatToFileAsync failed: fxv binary path could not be resolved.");
                return false;
            }

            var catArgs = new List<string> { "cat", repoRelativePath };
            if (!string.IsNullOrEmpty(revision))
            {
                catArgs.Add("-r");
                catArgs.Add(revision);
            }

            string commandLineArgs = FormatArguments(catArgs);

            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = commandLineArgs,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            await s_processSemaphore.WaitAsync(ct);
            try
            {
                return await Task.Run(() =>
                {
                    string tempFile = destinationFilePath + ".tmp_" + Guid.NewGuid().ToString("N");
                    try
                    {
                        string dir = Path.GetDirectoryName(destinationFilePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        using (var process = new Process { StartInfo = startInfo })
                        {
                            var stderrBuilder = new StringBuilder();
                            process.ErrorDataReceived += (_, e) =>
                            {
                                if (e.Data != null) stderrBuilder.AppendLine(e.Data);
                            };

                            if (!process.Start())
                            {
                                return false;
                            }

                            process.BeginErrorReadLine();

                            using (var outputStream = File.Create(tempFile))
                            {
                                process.StandardOutput.BaseStream.CopyTo(outputStream);
                            }

                            bool exited = process.WaitForExit(30000);
                            if (!exited)
                            {
                                try { process.Kill(); } catch { }
                                return false;
                            }

                            process.WaitForExit();

                            if (process.ExitCode == 0 && !ct.IsCancellationRequested)
                            {
                                if (File.Exists(destinationFilePath))
                                {
                                    File.Delete(destinationFilePath);
                                }
                                File.Move(tempFile, destinationFilePath);
                                return true;
                            }
                            else
                            {
                                if (stderrBuilder.Length > 0)
                                {
                                    UnityEngine.Debug.LogWarning($"[FlexVault] cat command exited with code {process.ExitCode}: {stderrBuilder}");
                                }
                                return false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[FlexVault] CatToFileAsync failed: {ex.Message}");
                        return false;
                    }
                    finally
                    {
                        if (File.Exists(tempFile))
                        {
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                });
            }
            finally
            {
                s_processSemaphore.Release();
            }
        }

        private static string FormatArguments(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (string arg in args)
            {
                if (sb.Length > 0) sb.Append(" ");

                if (string.IsNullOrEmpty(arg))
                {
                    sb.Append("\"\"");
                }
                else if (arg.Contains(" ") || arg.Contains("\t") || arg.Contains("\""))
                {
                    sb.Append("\"").Append(arg.Replace("\"", "\\\"")).Append("\"");
                }
                else
                {
                    sb.Append(arg);
                }
            }
            return sb.ToString();
        }
    }
}
