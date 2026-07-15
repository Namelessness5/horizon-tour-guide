namespace HorizonGuide.App;

public sealed record PlaybackSettingsSnapshot(
    string AudioLanguage,
    string SubtitleLanguage,
    float Volume,
    double SubtitleFontSize,
    double SubtitleBottomMargin,
    double LabelLift);

public sealed class PlaybackSettings
{
    private readonly Lock _gate = new();
    private PlaybackSettingsSnapshot _current;

    public PlaybackSettings(PlaybackSettingsSnapshot initial) => _current = initial;

    public PlaybackSettingsSnapshot Snapshot()
    {
        lock (_gate)
            return _current;
    }

    public void Update(
        string? audioLanguage = null,
        string? subtitleLanguage = null,
        float? volume = null,
        double? subtitleFontSize = null,
        double? subtitleBottomMargin = null,
        double? labelLift = null)
    {
        lock (_gate)
        {
            _current = _current with
            {
                AudioLanguage = audioLanguage ?? _current.AudioLanguage,
                SubtitleLanguage = subtitleLanguage ?? _current.SubtitleLanguage,
                Volume = Math.Clamp(volume ?? _current.Volume, 0f, 2f),
                SubtitleFontSize = Math.Clamp(subtitleFontSize ?? _current.SubtitleFontSize, 16, 48),
                SubtitleBottomMargin = Math.Clamp(subtitleBottomMargin ?? _current.SubtitleBottomMargin, 30, 320),
                LabelLift = Math.Clamp(labelLift ?? _current.LabelLift, 40, 180),
            };
        }
    }
}
