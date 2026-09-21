using DoggyDrop.Services;

namespace DoggyDrop.Tests;

internal sealed class TestGamificationCalendar : IGamificationCalendar
{
    private DateTime _utcNow;
    private DateTime? _nextUtcNow;

    public TestGamificationCalendar(DateTime? utcNow = null)
    {
        _utcNow = utcNow ?? DateTime.UtcNow;
    }

    public DateOnly Today => ToLocalDate(_utcNow);

    public DateTimeOffset UtcNow
    {
        get
        {
            var current = _utcNow;
            if (_nextUtcNow.HasValue)
            {
                _utcNow = _nextUtcNow.Value;
                _nextUtcNow = null;
            }
            return new DateTimeOffset(DateTime.SpecifyKind(current, DateTimeKind.Utc));
        }
    }

    public DateOnly ToLocalDate(DateTime utcInstant) =>
        new GamificationCalendar(new FixedTimeProvider(utcInstant)).ToLocalDate(utcInstant);

    public void SetUtcNow(DateTime utcNow) => _utcNow = utcNow;

    public void AdvanceAfterNextRead(DateTime utcNow) => _nextUtcNow = utcNow;

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTime utcNow) => _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
