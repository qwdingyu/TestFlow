using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Actions
{
    public class ShellActionDevice : IDevice
    {
        private readonly DeviceConfig _cfg;
        public ShellActionDevice(DeviceConfig cfg) { _cfg = cfg; }

        public DeviceExecResult Execute(StepConfig step, StepContext ctx)
        {
            var outputs = new Dictionary<string, object>();
            var token = ctx.Cancellation;
            try
            {
                var p = step.Parameters ?? new Dictionary<string, object>();
                string cmd = GetStr(p, "cmd", null);
                string args = GetStr(p, "args", null);
                string shell = GetStr(p, "shell", null); // "bash" | "cmd" | "powershell"
                string workdir = GetStr(p, "working_dir", null);
                bool captureStdout = GetBool(p, "capture_stdout", true);
                bool captureStderr = GetBool(p, "capture_stderr", true);
                if (string.IsNullOrWhiteSpace(cmd)) throw new Exception("Shell 参数缺失: cmd");

                ProcessStartInfo psi;
                if (!string.IsNullOrWhiteSpace(shell))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && shell.Equals("powershell", StringComparison.OrdinalIgnoreCase))
                        psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{cmd} {args}\"");
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        psi = new ProcessStartInfo("cmd.exe", $"/C {cmd} {args}");
                    else if (shell.Equals("sh", StringComparison.OrdinalIgnoreCase))
                        psi = new ProcessStartInfo("/bin/sh", $"-lc \"{cmd} {args}\"");
                    else
                        psi = new ProcessStartInfo("/bin/bash", $"-lc \"{cmd} {args}\"");
                }
                else
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        psi = new ProcessStartInfo("cmd.exe", $"/C {cmd} {args}");
                    else
                        psi = new ProcessStartInfo("/bin/bash", $"-lc \"{cmd} {args}\"");
                }

                psi.RedirectStandardOutput = captureStdout;
                psi.RedirectStandardError = captureStderr;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                if (!string.IsNullOrWhiteSpace(workdir) && Directory.Exists(workdir)) psi.WorkingDirectory = workdir;

                var proc = new Process();
                proc.StartInfo = psi;

                var sbOut = new StringBuilder();
                var sbErr = new StringBuilder();
                if (captureStdout) proc.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                if (captureStderr) proc.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

                using (token.Register(() => { try { if (!proc.HasExited) proc.Kill(); } catch { } }))
                {
                    proc.Start();
                    if (captureStdout) proc.BeginOutputReadLine();
                    if (captureStderr) proc.BeginErrorReadLine();
                    proc.WaitForExit();
                }

                outputs["exit_code"] = proc.ExitCode;
                if (captureStdout) outputs["stdout"] = sbOut.ToString();
                if (captureStderr) outputs["stderr"] = sbErr.ToString();
                return new DeviceExecResult { Success = proc.ExitCode == 0, Message = "exit " + proc.ExitCode, Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new DeviceExecResult { Success = false, Message = "cancelled", Outputs = outputs }; }
            catch (Exception ex)
            { return new DeviceExecResult { Success = false, Message = "Shell Exception: " + ex.Message, Outputs = outputs }; }
        }

        private static string GetStr(Dictionary<string, object> dict, string key, string defv)
            => (dict != null && dict.TryGetValue(key, out var v) && v != null) ? Convert.ToString(v) : defv;
        private static bool GetBool(Dictionary<string, object> dict, string key, bool defv)
            => (dict != null && dict.TryGetValue(key, out var v) && v != null) ? Convert.ToBoolean(v) : defv;
    }
}

