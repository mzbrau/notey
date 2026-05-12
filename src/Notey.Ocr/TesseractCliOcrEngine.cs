using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Notey.Ocr;

public sealed class TesseractCliOcrEngine : ITesseractOcrEngine
{
    public async ValueTask<OcrResult> RecognizeAsync(
        TesseractOcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Language);

        if (!File.Exists(request.ImagePath))
        {
            throw new FileNotFoundException("OCR input image was not found.", request.ImagePath);
        }

        using var process = CreateProcess(request);
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Tesseract executable '{request.ExecutablePath}' could not be started. Install Tesseract or update Notey:Ocr:TesseractExecutablePath.",
                ex);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            KillIfRunning((Process)state!);
        }, process);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillIfRunning(process);
            await DrainOutputAsync(outputTask, errorTask);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Tesseract exited with code {process.ExitCode}: {TrimProcessMessage(error)}");
        }

        var parsed = TesseractTsvParser.Parse(output);
        var warnings = string.IsNullOrWhiteSpace(error)
            ? Array.Empty<string>()
            : [TrimProcessMessage(error)];

        return new OcrResult(
            parsed.Text,
            request.Language,
            parsed.Confidence,
            warnings);
    }

    private static Process CreateProcess(TesseractOcrRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(request.ImagePath);
        startInfo.ArgumentList.Add("stdout");
        if (!string.IsNullOrWhiteSpace(request.DataPath))
        {
            startInfo.ArgumentList.Add("--tessdata-dir");
            startInfo.ArgumentList.Add(request.DataPath);
        }

        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(request.Language);
        startInfo.ArgumentList.Add("tsv");

        return new Process { StartInfo = startInfo };
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DrainOutputAsync(Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string TrimProcessMessage(string message)
    {
        const int maxLength = 500;
        var trimmed = message.Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..maxLength]}...";
    }
}
