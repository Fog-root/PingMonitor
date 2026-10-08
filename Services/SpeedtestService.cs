using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Services;

public class SpeedtestService : IDisposable
{
    private static readonly HttpClient HttpClient;

    static SpeedtestService()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20,
            EnableMultipleHttp2Connections = true
        };
        HttpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private bool _isRunning;
    private bool _isDisposed;

    public bool IsRunning => _isRunning;

    public event Action<SpeedtestProgressReport>? ProgressChanged;
    public event Action<double>? LiveSpeedSampled; // Current Mbps for graph/gauge
    public event Action<SpeedtestResult>? Completed;
    public event Action<string>? ErrorOccurred;

    public void Cancel()
    {
        lock (_lock)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
    }

    public async Task StartTestAsync()
    {
        lock (_lock)
        {
            if (_isRunning || _isDisposed) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();
        }

        var token = _cts.Token;

        try
        {
            var result = new SpeedtestResult();
            var report = new SpeedtestProgressReport
            {
                Phase = SpeedtestPhase.Connecting,
                StatusMessage = "Подключение к тестовому серверу..."
            };
            ReportProgress(report);

            // =========================================================
            // ФАЗА 1: Определение метаданных клиента, сервера и пинга
            // =========================================================
            report.Phase = SpeedtestPhase.Latency;
            report.StatusMessage = "Замер пинга и джиттера...";
            ReportProgress(report);

            await FetchMetadataAndLatencyAsync(result, report, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                ReportProgress(new SpeedtestProgressReport { Phase = SpeedtestPhase.Cancelled, StatusMessage = "Тест отменен пользователем" });
                return;
            }

            // =========================================================
            // ФАЗА 2: Замер входящей скорости (Download)
            // =========================================================
            report.Phase = SpeedtestPhase.Download;
            report.StatusMessage = "Тестирование входящей скорости (Download)...";
            ReportProgress(report);

            await MeasureDownloadSpeedAsync(result, report, token, durationSeconds: 8.5).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                ReportProgress(new SpeedtestProgressReport { Phase = SpeedtestPhase.Cancelled, StatusMessage = "Тест отменен пользователем" });
                return;
            }

            // Небольшая пауза между фазами для сброса буферов
            await Task.Delay(400, token).ConfigureAwait(false);

            // =========================================================
            // ФАЗА 3: Замер исходящей скорости (Upload)
            // =========================================================
            report.Phase = SpeedtestPhase.Upload;
            report.StatusMessage = "Тестирование исходящей скорости (Upload)...";
            ReportProgress(report);

            await MeasureUploadSpeedAsync(result, report, token, durationSeconds: 7.5).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                ReportProgress(new SpeedtestProgressReport { Phase = SpeedtestPhase.Cancelled, StatusMessage = "Тест отменен пользователем" });
                return;
            }

            // =========================================================
            // ФАЗА 4: Завершение и формирование отчета
            // =========================================================
            double maxLoaded = Math.Max(result.LoadedPingDownloadMs, result.LoadedPingUploadMs);
            if (maxLoaded > 0 && result.PingMs > 0)
            {
                result.BufferbloatDeltaMs = Math.Max(0.0, Math.Round(maxLoaded - result.PingMs, 1));
            }
            else
            {
                result.BufferbloatDeltaMs = 0.0;
            }

            result.BufferbloatGrade = result.BufferbloatDeltaMs switch
            {
                <= 5 => "A+",
                <= 15 => "A",
                <= 30 => "B",
                <= 60 => "C",
                <= 100 => "D",
                _ => "F"
            };

            report.BufferbloatDeltaMs = result.BufferbloatDeltaMs;
            report.BufferbloatGrade = result.BufferbloatGrade;
            report.Phase = SpeedtestPhase.Completed;
            report.CurrentSpeedMbps = 0;
            report.TotalProgress = 1.0;
            report.PhaseProgress = 1.0;
            report.StatusMessage = "Тестирование успешно завершено";
            ReportProgress(report);

            Completed?.Invoke(result);
        }
        catch (OperationCanceledException)
        {
            ReportProgress(new SpeedtestProgressReport
            {
                Phase = SpeedtestPhase.Cancelled,
                StatusMessage = "Тест отменен пользователем"
            });
        }
        catch (Exception ex)
        {
            ReportProgress(new SpeedtestProgressReport
            {
                Phase = SpeedtestPhase.Failed,
                StatusMessage = $"Ошибка замера: {ex.Message}"
            });
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            lock (_lock)
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private void ReportProgress(SpeedtestProgressReport report)
    {
        ProgressChanged?.Invoke(report);
    }

    private async Task FetchMetadataAndLatencyAsync(
        SpeedtestResult result,
        SpeedtestProgressReport report,
        CancellationToken token)
    {
        // 1. Быстрый запрос метаданных Cloudflare
        try
        {
            using var initRequest = new HttpRequestMessage(HttpMethod.Head, "https://speed.cloudflare.com/__down?bytes=0");
            using var initResponse = await HttpClient.SendAsync(initRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

            if (initResponse.Headers.TryGetValues("cf-meta-ip", out var ipVals))
                result.ClientIp = ipVals.FirstOrDefault() ?? string.Empty;

            if (initResponse.Headers.TryGetValues("city", out var cityVals))
                result.ServerLocation = cityVals.FirstOrDefault() ?? string.Empty;

            if (initResponse.Headers.TryGetValues("colo", out var coloVals))
                result.ServerColo = coloVals.FirstOrDefault() ?? string.Empty;
        }
        catch { }

        // 2. Фоновый запрос провайдера через ip-api (не блокирующий)
        try
        {
            using var ctsIp = CancellationTokenSource.CreateLinkedTokenSource(token);
            ctsIp.CancelAfter(2000);
            string ipJson = await HttpClient.GetStringAsync("http://ip-api.com/json/", ctsIp.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(ipJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("isp", out var ispProp))
                result.IspName = ispProp.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(result.ClientIp) && root.TryGetProperty("query", out var queryProp))
                result.ClientIp = queryProp.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(result.ServerLocation) && root.TryGetProperty("city", out var cityProp))
                result.ServerLocation = cityProp.GetString() ?? string.Empty;
        }
        catch
        {
            if (string.IsNullOrEmpty(result.IspName))
                result.IspName = "Интернет-провайдер";
        }

        report.ClientIp = result.ClientIp;
        report.IspName = result.IspName;
        report.ServerLocation = result.ServerLocation;
        report.ServerColo = result.ServerColo;
        ReportProgress(report);

        // 3. Серия из 12 замеров задержки (RTT / Latency & Jitter)
        var latencies = new List<double>();
        const int pingCount = 12;

        for (int i = 0; i < pingCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=0");
                using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch
            {
                // Игнорируем единичные сбои
            }

            report.PhaseProgress = (double)(i + 1) / pingCount;
            report.TotalProgress = 0.05 + (0.10 * report.PhaseProgress);
            ReportProgress(report);

            await Task.Delay(40, token).ConfigureAwait(false);
        }

        if (latencies.Count > 0)
        {
            latencies.Sort();
            // Минимальный пинг отбрасывает сетевые помехи
            result.PingMs = Math.Round(latencies[0], 1);

            // Расчет джиттера (вариация задержки)
            double jitterSum = 0;
            int jitterPairs = 0;
            for (int i = 1; i < latencies.Count; i++)
            {
                jitterSum += Math.Abs(latencies[i] - latencies[i - 1]);
                jitterPairs++;
            }
            result.JitterMs = jitterPairs > 0 ? Math.Round(jitterSum / jitterPairs, 1) : 0.5;

            report.PingMs = result.PingMs;
            report.JitterMs = result.JitterMs;
            ReportProgress(report);
        }
    }

    private async Task MeasureDownloadSpeedAsync(
        SpeedtestResult result,
        SpeedtestProgressReport report,
        CancellationToken token,
        double durationSeconds)
    {
        long totalDownloadedBytes = 0;
        int activeStreams = 4;
        var streamTasks = new List<Task>();
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        phaseCts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
        var phaseToken = phaseCts.Token;

        var speedSamples = new List<double>();
        double currentSmoothedMbps = 0;
        double peakMbps = 0;

        var loadedPingSamples = new List<double>();

        // Запуск параллельных потоков загрузки
        for (int i = 0; i < activeStreams; i++)
        {
            streamTasks.Add(Task.Run(async () =>
            {
                byte[] buffer = new byte[65536]; // 64 КБ буфер
                while (!phaseToken.IsCancellationRequested)
                {
                    try
                    {
                        // Загрузка чанка 25 МБ
                        using var stream = await HttpClient.GetStreamAsync("https://speed.cloudflare.com/__down?bytes=25000000", phaseToken).ConfigureAwait(false);
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), phaseToken).ConfigureAwait(false)) > 0)
                        {
                            Interlocked.Add(ref totalDownloadedBytes, bytesRead);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // При ошибке сокета делаем короткую паузу и продолжаем цикл
                        await Task.Delay(100, phaseToken).ConfigureAwait(false);
                    }
                }
            }, phaseToken));
        }

        // Фоновый замер задержки под нагрузкой (Loaded Latency / Bufferbloat)
        streamTasks.Add(Task.Run(async () =>
        {
            while (!phaseToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(600, phaseToken).ConfigureAwait(false);
                    var sw = Stopwatch.StartNew();
                    using var pingReq = new HttpRequestMessage(HttpMethod.Head, "https://speed.cloudflare.com/__down?bytes=0");
                    using var pingResp = await HttpClient.SendAsync(pingReq, HttpCompletionOption.ResponseHeadersRead, phaseToken).ConfigureAwait(false);
                    sw.Stop();
                    if (pingResp.IsSuccessStatusCode)
                    {
                        lock (loadedPingSamples)
                        {
                            loadedPingSamples.Add(sw.Elapsed.TotalMilliseconds);
                        }
                    }
                }
                catch { }
            }
        }, phaseToken));

        // Поток дискретного замера скорости (каждые 120 мс)
        var swTotal = Stopwatch.StartNew();
        long prevBytes = 0;
        var swSample = Stopwatch.StartNew();

        while (!phaseToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(120, phaseToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            double dt = swSample.Elapsed.TotalSeconds;
            if (dt <= 0.001) continue;
            swSample.Restart();

            long currentTotalBytes = Interlocked.Read(ref totalDownloadedBytes);
            long bytesInInterval = currentTotalBytes - prevBytes;
            prevBytes = currentTotalBytes;

            double instantMbps = (bytesInInterval * 8.0) / (dt * 1_000_000.0);

            // Экспоненциальное сглаживание для плавной анимации спидометра
            if (currentSmoothedMbps <= 0.01)
            {
                currentSmoothedMbps = instantMbps;
            }
            else
            {
                currentSmoothedMbps = (0.70 * instantMbps) + (0.30 * currentSmoothedMbps);
            }

            if (currentSmoothedMbps > peakMbps && swTotal.Elapsed.TotalSeconds > 1.0)
            {
                peakMbps = currentSmoothedMbps;
            }

            if (swTotal.Elapsed.TotalSeconds > 1.2)
            {
                speedSamples.Add(instantMbps);
            }

            double progress01 = Math.Min(1.0, swTotal.Elapsed.TotalSeconds / durationSeconds);
            report.PhaseProgress = progress01;
            report.TotalProgress = 0.15 + (0.45 * progress01);
            report.CurrentSpeedMbps = Math.Round(currentSmoothedMbps, 1);
            report.PeakSpeedMbps = Math.Round(peakMbps, 1);
            report.DownloadSpeedMbps = report.CurrentSpeedMbps;
            report.DownloadPeakMbps = report.PeakSpeedMbps;

            ReportProgress(report);
            LiveSpeedSampled?.Invoke(report.CurrentSpeedMbps);
        }

        try
        {
            await Task.WhenAll(streamTasks).ConfigureAwait(false);
        }
        catch { }

        // Расчет итоговой средней скорости (без учета стартового разгона первого 1.2 сек)
        double finalAverage = speedSamples.Count > 0 
            ? speedSamples.Average() 
            : (swTotal.Elapsed.TotalSeconds > 0 ? (totalDownloadedBytes * 8.0) / (swTotal.Elapsed.TotalSeconds * 1_000_000.0) : 0);

        result.DownloadSpeedMbps = Math.Round(finalAverage, 1);
        result.DownloadPeakMbps = Math.Round(peakMbps > 0 ? peakMbps : finalAverage, 1);

        lock (loadedPingSamples)
        {
            result.LoadedPingDownloadMs = loadedPingSamples.Count > 0 
                ? Math.Round(loadedPingSamples.Average(), 1) 
                : result.PingMs;
        }

        report.DownloadSpeedMbps = result.DownloadSpeedMbps;
        report.DownloadPeakMbps = result.DownloadPeakMbps;
        ReportProgress(report);
    }

    private async Task MeasureUploadSpeedAsync(
        SpeedtestResult result,
        SpeedtestProgressReport report,
        CancellationToken token,
        double durationSeconds)
    {
        long totalUploadedBytes = 0;
        int activeStreams = 4;
        var streamTasks = new List<Task>();
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        phaseCts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
        var phaseToken = phaseCts.Token;

        // Создаем готовый буфер полезной нагрузки для POST-запросов (1 МБ)
        byte[] uploadPayload = new byte[1048576];
        new Random().NextBytes(uploadPayload);

        var speedSamples = new List<double>();
        double currentSmoothedMbps = 0;
        double peakMbps = 0;

        var loadedPingSamples = new List<double>();

        for (int i = 0; i < activeStreams; i++)
        {
            streamTasks.Add(Task.Run(async () =>
            {
                while (!phaseToken.IsCancellationRequested)
                {
                    try
                    {
                        var content = new ProgressUploadContent(uploadPayload, bytesSent =>
                        {
                            Interlocked.Add(ref totalUploadedBytes, bytesSent);
                        });

                        using var resp = await HttpClient.PostAsync("https://speed.cloudflare.com/__up", content, phaseToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(100, phaseToken).ConfigureAwait(false);
                    }
                }
            }, phaseToken));
        }

        // Фоновый замер задержки под нагрузкой Upload
        streamTasks.Add(Task.Run(async () =>
        {
            while (!phaseToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(600, phaseToken).ConfigureAwait(false);
                    var sw = Stopwatch.StartNew();
                    using var pingReq = new HttpRequestMessage(HttpMethod.Head, "https://speed.cloudflare.com/__down?bytes=0");
                    using var pingResp = await HttpClient.SendAsync(pingReq, HttpCompletionOption.ResponseHeadersRead, phaseToken).ConfigureAwait(false);
                    sw.Stop();
                    if (pingResp.IsSuccessStatusCode)
                    {
                        lock (loadedPingSamples)
                        {
                            loadedPingSamples.Add(sw.Elapsed.TotalMilliseconds);
                        }
                    }
                }
                catch { }
            }
        }, phaseToken));

        var swTotal = Stopwatch.StartNew();
        long prevBytes = 0;
        var swSample = Stopwatch.StartNew();

        while (!phaseToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(120, phaseToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            double dt = swSample.Elapsed.TotalSeconds;
            if (dt <= 0.001) continue;
            swSample.Restart();

            long currentTotalBytes = Interlocked.Read(ref totalUploadedBytes);
            long bytesInInterval = currentTotalBytes - prevBytes;
            prevBytes = currentTotalBytes;

            double instantMbps = (bytesInInterval * 8.0) / (dt * 1_000_000.0);

            if (currentSmoothedMbps <= 0.01)
            {
                currentSmoothedMbps = instantMbps;
            }
            else
            {
                currentSmoothedMbps = (0.70 * instantMbps) + (0.30 * currentSmoothedMbps);
            }

            if (currentSmoothedMbps > peakMbps && swTotal.Elapsed.TotalSeconds > 1.0)
            {
                peakMbps = currentSmoothedMbps;
            }

            if (swTotal.Elapsed.TotalSeconds > 1.0)
            {
                speedSamples.Add(instantMbps);
            }

            double progress01 = Math.Min(1.0, swTotal.Elapsed.TotalSeconds / durationSeconds);
            report.PhaseProgress = progress01;
            report.TotalProgress = 0.60 + (0.40 * progress01);
            report.CurrentSpeedMbps = Math.Round(currentSmoothedMbps, 1);
            report.UploadSpeedMbps = report.CurrentSpeedMbps;

            ReportProgress(report);
            LiveSpeedSampled?.Invoke(report.CurrentSpeedMbps);
        }

        try
        {
            await Task.WhenAll(streamTasks).ConfigureAwait(false);
        }
        catch { }

        double finalAverage = speedSamples.Count > 0 
            ? speedSamples.Average() 
            : (swTotal.Elapsed.TotalSeconds > 0 ? (totalUploadedBytes * 8.0) / (swTotal.Elapsed.TotalSeconds * 1_000_000.0) : 0);

        result.UploadSpeedMbps = Math.Round(finalAverage, 1);

        lock (loadedPingSamples)
        {
            result.LoadedPingUploadMs = loadedPingSamples.Count > 0 
                ? Math.Round(loadedPingSamples.Average(), 1) 
                : result.PingMs;
        }

        report.UploadSpeedMbps = result.UploadSpeedMbps;
        ReportProgress(report);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Cancel();
        }
    }

    /// <summary>
    /// Специальный контент потоковой передачи с подсчетом отправленных байт в реальном времени.
    /// </summary>
    private class ProgressUploadContent : HttpContent
    {
        private readonly byte[] _buffer;
        private readonly Action<int> _onBytesSent;

        public ProgressUploadContent(byte[] buffer, Action<int> onBytesSent)
        {
            _buffer = buffer;
            _onBytesSent = onBytesSent;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            const int chunkSize = 65536;
            int offset = 0;
            while (offset < _buffer.Length)
            {
                int count = Math.Min(chunkSize, _buffer.Length - offset);
                await stream.WriteAsync(_buffer.AsMemory(offset, count)).ConfigureAwait(false);
                _onBytesSent(count);
                offset += count;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _buffer.Length;
            return true;
        }
    }
}
