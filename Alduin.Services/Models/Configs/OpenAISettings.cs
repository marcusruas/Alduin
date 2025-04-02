using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alduin.Core.Models.Configs
{
    public class OpenAISettings
    {
        public string ApiKey { get; set; }
        public OpenAIModels Models { get; set; }
        public OpenAIPrompts Prompts { get; set; }
    }

    public class OpenAIModels
    {
        public string AudioTranscribeModel { get; set; }
        public string SpeechGeneratorModel { get; set; }
        public string ChatModel { get; set; }
    }

    public class OpenAIPrompts
    {
        public string AnalyzeAudioSystemPrompt { get; set; }
        public string AnalyzeAudioRequestPrompt { get; set; }
        public string GreetingsPrompt { get; set; }
    }
}
