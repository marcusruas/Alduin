namespace Alduin.Models
{
    public class CustomerServiceCallSettings
    {
        public string? StreamId { get; set; }
        public string? CallSid { get; set; }
        public DateTime LastClientSpeech { get; set; } = DateTime.UtcNow;

        public double SecondsSinceLastSpeech => (DateTime.UtcNow - LastClientSpeech).TotalSeconds;
    }
}
