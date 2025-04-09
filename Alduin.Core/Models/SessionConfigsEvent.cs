using System.Dynamic;

namespace Alduin.Models
{
    public record SessionConfigsEvent(string type, SessionContent session);
    public record SessionContent(IEnumerable<ExpandoObject>? tools, TurnDetection turn_detection, string input_audio_format, string output_audio_format, string voice, string instructions, List<string> modalities, double temperature);
    public record TurnDetection(string type);
}