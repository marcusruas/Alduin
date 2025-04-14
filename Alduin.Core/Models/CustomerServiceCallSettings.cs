namespace Alduin.Models
{
    public class CustomerServiceCallSettings
    {
        public string? StreamSid { get; set; }
        public string? CallSid { get; set; }
        public string? ItemId { get; set; }
        public DateTime LastClientSpeech { get; set; } = DateTime.UtcNow;

        public double SecondsSinceLastSpeech => (DateTime.UtcNow - LastClientSpeech).TotalSeconds;
    }
}
