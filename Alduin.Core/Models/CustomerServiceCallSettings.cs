namespace Alduin.Models
{
    public class CustomerServiceCallSettings
    {
        public string? StreamSid { get; set; }
        public DateTime LastClientSpeech { get; set; } = DateTime.UtcNow;

        public string? LastAssistantId { get; set; }
        public int? FirstDeltaFromCurrentResponse { get; set; }
        public int LatestTimestamp { get; set; } = 0;

        public double SecondsSinceLastSpeech => (DateTime.UtcNow - LastClientSpeech).TotalSeconds;
    }
}
