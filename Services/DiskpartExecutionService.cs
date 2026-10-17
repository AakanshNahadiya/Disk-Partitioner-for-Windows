using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DiskPartitioner.Helpers;

namespace DiskPartitioner.Services
{
    public class DiskOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string RawOutput { get; set; } = string.Empty;
    }

    public interface IDiskpartExecutionService
    {
        Task<DiskOperationResult> ExecuteScriptAsync(string scriptContent, string operationName, IProgress<int>? progressPercentage = null, IProgress<string>? progressStatus = null);
        Task<DiskOperationResult> CleanDiskAsync(int diskIndex, IProgress<int>? progressPercentage = null, IProgress<string>? progressStatus = null);
        Task<DiskOperationResult> ConvertStyleAsync(int diskIndex, string style);
        Task<DiskOperationResult> SetDiskReadOnlyAsync(int diskIndex, bool readOnly);
        Task<DiskOperationResult> SetDiskOfflineAsync(int diskIndex, bool offline);
        Task<DiskOperationResult> CreatePartitionAsync(int diskIndex, long sizeMb, string? driveLetter, string? fileSystem, string? label, bool quick = true, IProgress<int>? progressPercentage = null, IProgress<string>? progressStatus = null);
        Task<DiskOperationResult> FormatPartitionAsync(int diskIndex, uint partitionNumber, string fileSystem, string label, bool quick, IProgress<int>? progressPercentage = null, IProgress<string>? progressStatus = null);
        Task<DiskOperationResult> AssignDriveLetterAsync(int diskIndex, uint partitionNumber, char letter);
        Task<DiskOperationResult> RemoveDriveLetterAsync(int diskIndex, uint partitionNumber, char letter);
        Task<DiskOperationResult> ExtendPartitionAsync(int diskIndex, uint partitionNumber, long sizeMb, string? driveLetter = null);
        Task<DiskOperationResult> ShrinkPartitionAsync(int diskIndex, uint partitionNumber, long sizeMb, string? driveLetter = null);
        Task<DiskOperationResult> DeletePartitionAsync(int diskIndex, uint partitionNumber, bool overrideFlag);
    }

    public class DiskpartExecutionService : IDiskpartExecutionService
    {
        public async Task<DiskOperationResult> ExecuteScriptAsync(
            string scriptContent, 
            string operationName, 
            IProgress<int>? progressPercentage = null, 
            IProgress<string>? progressStatus = null)
        {
            string tempScript = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.txt");

            try
            {
                await File.WriteAllTextAsync(tempScript, scriptContent, Encoding.ASCII);

                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "diskpart.exe"),
                    Arguments = $"/s \"{tempScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.ASCII
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        string sanitized = TextSanitizer.StripAnsiAndControl(e.Data);
                        outputBuilder.AppendLine(sanitized);
                        string line = sanitized.Trim();
                        if (!string.IsNullOrEmpty(line))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+percent completed", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                            {
                                progressPercentage?.Report(pct);
                                progressStatus?.Report($"{pct}% completed");
                            }
                            else if (line.StartsWith("DiskPart succeeded", StringComparison.OrdinalIgnoreCase) ||
                                     line.StartsWith("DiskPart successfully", StringComparison.OrdinalIgnoreCase) ||
                                     line.StartsWith("Formatting", StringComparison.OrdinalIgnoreCase))
                            {
                                progressStatus?.Report(line);
                            }
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(TextSanitizer.StripAnsiAndControl(e.Data));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                string fullOutput = outputBuilder.ToString();
                string errors = errorBuilder.ToString();

                bool hasErrorKeyword = fullOutput.Contains("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase) ||
                                       fullOutput.Contains("Virtual Disk Service error", StringComparison.OrdinalIgnoreCase) ||
                                       fullOutput.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                                       fullOutput.Contains("There is no disk selected", StringComparison.OrdinalIgnoreCase) ||
                                       fullOutput.Contains("There is no partition selected", StringComparison.OrdinalIgnoreCase) ||
                                       fullOutput.Contains("The disk is write-protected", StringComparison.OrdinalIgnoreCase) ||
                                       !string.IsNullOrWhiteSpace(errors);

                bool success = (process.ExitCode == 0) && !hasErrorKeyword;

                string summary = success
                    ? $"{operationName} completed successfully."
                    : ExtractRelevantError(fullOutput, errors, operationName);

                return new DiskOperationResult
                {
                    Success = success,
                    Message = summary,
                    RawOutput = fullOutput + "\n" + errors
                };
            }
            catch (Exception ex)
            {
                return new DiskOperationResult
                {
                    Success = false,
                    Message = $"{operationName} failed: {ex.Message}",
                    RawOutput = ex.ToString()
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(tempScript))
                    {
                        File.Delete(tempScript);
                    }
                }
                catch { }
            }
        }

        private static string ExtractRelevantError(string output, string errors, string operationName)
        {
            if (!string.IsNullOrWhiteSpace(errors))
            {
                return TextSanitizer.SanitizeSpeech(errors);
            }

            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => TextSanitizer.StripAnsiAndControl(l).Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                if (line.Contains("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Virtual Disk Service error", StringComparison.OrdinalIgnoreCase))
                {
                    string details = (i + 1 < lines.Count && !lines[i + 1].StartsWith("DISKPART", StringComparison.OrdinalIgnoreCase))
                        ? lines[i + 1]
                        : line;
                    return TextSanitizer.SanitizeSpeech($"{operationName} error: {details}");
                }

                if (line.StartsWith("There is no", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("The volume cannot", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("The specified shrink", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("The volume's file system", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("The file system is", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("The disk is write-protected", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                {
                    return TextSanitizer.SanitizeSpeech($"{operationName} error: {line}");
                }
            }

            // Fallback: Find the last non-command, non-header line from DiskPart
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (!line.StartsWith("DISKPART>", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Microsoft DiskPart", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Copyright (C)", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("On computer:", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("is now the selected", StringComparison.OrdinalIgnoreCase))
                {
                    return TextSanitizer.SanitizeSpeech($"{operationName} error: {line}");
                }
            }

            return $"{operationName} encountered an unexpected error.";
        }

        public static string BuildCleanScript(int diskIndex) =>
            $"select disk {diskIndex}\nclean\n";

        public static string BuildConvertScript(int diskIndex, string style) =>
            $"select disk {diskIndex}\nconvert {style.Trim().ToLowerInvariant()}\n";

        public static string BuildReadOnlyScript(int diskIndex, bool readOnly) =>
            $"select disk {diskIndex}\nattributes disk {(readOnly ? "set" : "clear")} readonly\n";

        public static string BuildOfflineScript(int diskIndex, bool offline) =>
            $"select disk {diskIndex}\n{(offline ? "offline" : "online")} disk\n";

        public static string BuildCreatePartitionScript(int diskIndex, long sizeMb, string? driveLetter, string? fileSystem, string? label, bool quick = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"select disk {diskIndex}");

            if (sizeMb > 0)
            {
                sb.AppendLine($"create partition primary size={sizeMb}");
            }
            else
            {
                sb.AppendLine("create partition primary");
            }

            if (!string.IsNullOrWhiteSpace(driveLetter))
            {
                char letter = driveLetter.Trim().ToUpperInvariant()[0];
                sb.AppendLine($"assign letter={letter}");
            }

            if (!string.IsNullOrWhiteSpace(fileSystem) && !fileSystem.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                string quickArg = quick ? " quick" : string.Empty;
                string labelArg = !string.IsNullOrWhiteSpace(label) ? $" label=\"{label}\"" : string.Empty;
                sb.AppendLine($"format fs={fileSystem.ToLowerInvariant()}{quickArg}{labelArg}");
            }

            return sb.ToString();
        }

        public static string BuildFormatScript(int diskIndex, uint partitionNumber, string fileSystem, string label, bool quick)
        {
            string quickArg = quick ? " quick" : string.Empty;
            string labelArg = !string.IsNullOrWhiteSpace(label) ? $" label=\"{label}\"" : string.Empty;
            return $"select disk {diskIndex}\nselect partition {partitionNumber}\nformat fs={fileSystem.ToLowerInvariant()}{quickArg}{labelArg}\n";
        }

        public static string BuildAssignLetterScript(int diskIndex, uint partitionNumber, char letter) =>
            $"select disk {diskIndex}\nselect partition {partitionNumber}\nassign letter={char.ToUpperInvariant(letter)}\n";

        public static string BuildRemoveLetterScript(int diskIndex, uint partitionNumber, char letter) =>
            $"select disk {diskIndex}\nselect partition {partitionNumber}\nremove letter={char.ToUpperInvariant(letter)}\n";

        public static string BuildExtendScript(int diskIndex, uint partitionNumber, long sizeMb, string? driveLetter = null)
        {
            if (!string.IsNullOrWhiteSpace(driveLetter))
            {
                char letter = char.ToUpperInvariant(driveLetter.Trim()[0]);
                return $"select volume {letter}\nextend{(sizeMb > 0 ? $" size={sizeMb}" : "")}\n";
            }
            return $"select disk {diskIndex}\nselect partition {partitionNumber}\nextend{(sizeMb > 0 ? $" size={sizeMb}" : "")}\n";
        }

        public static string BuildShrinkScript(int diskIndex, uint partitionNumber, long sizeMb, string? driveLetter = null)
        {
            if (!string.IsNullOrWhiteSpace(driveLetter))
            {
                char letter = char.ToUpperInvariant(driveLetter.Trim()[0]);
                return $"select volume {letter}\nshrink desired={sizeMb}\n";
            }
            return $"select disk {diskIndex}\nselect partition {partitionNumber}\nshrink desired={sizeMb}\n";
        }

        public static string BuildDeletePartitionScript(int diskIndex, uint partitionNumber, bool overrideFlag) =>
            $"select disk {diskIndex}\nselect partition {partitionNumber}\ndelete partition{(overrideFlag ? " override" : "")}\n";

        public Task<DiskOperationResult> CleanDiskAsync(
            int diskIndex, 
            IProgress<int>? progressPercentage = null, 
            IProgress<string>? progressStatus = null)
        {
            return ExecuteScriptAsync(BuildCleanScript(diskIndex), $"Clean Disk {diskIndex}", progressPercentage, progressStatus);
        }

        public Task<DiskOperationResult> ConvertStyleAsync(int diskIndex, string style)
        {
            return ExecuteScriptAsync(BuildConvertScript(diskIndex, style), $"Convert Disk {diskIndex} to {style.ToUpperInvariant()}");
        }

        public Task<DiskOperationResult> SetDiskReadOnlyAsync(int diskIndex, bool readOnly)
        {
            string name = readOnly ? $"Set Disk {diskIndex} Read-Only" : $"Clear Disk {diskIndex} Read-Only";
            return ExecuteScriptAsync(BuildReadOnlyScript(diskIndex, readOnly), name);
        }

        public Task<DiskOperationResult> SetDiskOfflineAsync(int diskIndex, bool offline)
        {
            string name = offline ? $"Take Disk {diskIndex} Offline" : $"Bring Disk {diskIndex} Online";
            return ExecuteScriptAsync(BuildOfflineScript(diskIndex, offline), name);
        }

        public Task<DiskOperationResult> CreatePartitionAsync(
            int diskIndex, 
            long sizeMb, 
            string? driveLetter, 
            string? fileSystem, 
            string? label, 
            bool quick = true, 
            IProgress<int>? progressPercentage = null, 
            IProgress<string>? progressStatus = null)
        {
            return ExecuteScriptAsync(BuildCreatePartitionScript(diskIndex, sizeMb, driveLetter, fileSystem, label, quick), $"Create Primary Partition on Disk {diskIndex}", progressPercentage, progressStatus);
        }

        public Task<DiskOperationResult> FormatPartitionAsync(
            int diskIndex, 
            uint partitionNumber, 
            string fileSystem, 
            string label, 
            bool quick, 
            IProgress<int>? progressPercentage = null, 
            IProgress<string>? progressStatus = null)
        {
            return ExecuteScriptAsync(BuildFormatScript(diskIndex, partitionNumber, fileSystem, label, quick), $"Format Partition {partitionNumber} on Disk {diskIndex}", progressPercentage, progressStatus);
        }

        public Task<DiskOperationResult> AssignDriveLetterAsync(int diskIndex, uint partitionNumber, char letter)
        {
            return ExecuteScriptAsync(BuildAssignLetterScript(diskIndex, partitionNumber, letter), $"Assign Drive Letter {char.ToUpperInvariant(letter)}: to Partition {partitionNumber}");
        }

        public Task<DiskOperationResult> RemoveDriveLetterAsync(int diskIndex, uint partitionNumber, char letter)
        {
            return ExecuteScriptAsync(BuildRemoveLetterScript(diskIndex, partitionNumber, letter), $"Remove Drive Letter {char.ToUpperInvariant(letter)}: from Partition {partitionNumber}");
        }

        public Task<DiskOperationResult> ExtendPartitionAsync(int diskIndex, uint partitionNumber, long sizeMb, string? driveLetter = null)
        {
            return ExecuteScriptAsync(BuildExtendScript(diskIndex, partitionNumber, sizeMb, driveLetter), $"Extend Partition {partitionNumber} on Disk {diskIndex}");
        }

        public Task<DiskOperationResult> ShrinkPartitionAsync(int diskIndex, uint partitionNumber, long sizeMb, string? driveLetter = null)
        {
            return ExecuteScriptAsync(BuildShrinkScript(diskIndex, partitionNumber, sizeMb, driveLetter), $"Shrink Partition {partitionNumber} on Disk {diskIndex}");
        }

        public Task<DiskOperationResult> DeletePartitionAsync(int diskIndex, uint partitionNumber, bool overrideFlag)
        {
            string name = overrideFlag
                ? $"Force Delete Partition {partitionNumber} on Disk {diskIndex} (Override)"
                : $"Delete Partition {partitionNumber} on Disk {diskIndex}";
            return ExecuteScriptAsync(BuildDeletePartitionScript(diskIndex, partitionNumber, overrideFlag), name);
        }
    }
}
