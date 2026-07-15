using NAudio.Wave;

namespace HorizonGuide.Playback;

/// <summary>
/// 放一个 WAV，等它放完。
///
/// 只做这一件事：不认识内容、不认识字幕、不认识地点。
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _device;
    private AudioFileReader? _reader;
    private readonly object _lock = new();

    public bool IsPlaying { get; private set; }

    /// <summary>0..1。</summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// 放完返回 true；被 <see cref="Stop"/> 打断返回 false。
    /// 文件缺失或损坏时抛异常，由上层决定怎么处理（设计文档 §5.5：跳过并记日志）。
    /// </summary>
    public async Task<bool> PlayAsync(string path, CancellationToken cancellationToken)
    {
        Stop();

        var finished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _reader = new AudioFileReader(path) { Volume = Volume };
            _device = new WaveOutEvent();
            _device.Init(_reader);

            // 正常放完和被 Stop() 打断都会走这里。用 stoppedByUs 区分。
            _device.PlaybackStopped += (_, _) => finished.TrySetResult(!_stoppedByUs);

            _stoppedByUs = false;
            IsPlaying = true;
            _device.Play();
        }

        await using var _ = cancellationToken.Register(Stop);

        try
        {
            return await finished.Task;
        }
        finally
        {
            IsPlaying = false;
            Cleanup();
        }
    }

    private bool _stoppedByUs;

    public void Stop()
    {
        lock (_lock)
        {
            if (_device is null)
                return;

            _stoppedByUs = true;
            _device.Stop();
        }
    }

    private void Cleanup()
    {
        lock (_lock)
        {
            _device?.Dispose();
            _device = null;
            _reader?.Dispose();
            _reader = null;
        }
    }

    public void Dispose()
    {
        Stop();
        Cleanup();
    }
}
