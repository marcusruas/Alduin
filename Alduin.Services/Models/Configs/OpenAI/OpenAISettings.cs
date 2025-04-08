using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alduin.Core.Models.Configs.OpenAI
{
    public class OpenAISettings
    {
        public string RealtimeWebSocketUrl { get; set; }
        public string ApiKey { get; set; }
        public OpenAIModels Models { get; set; }
        public OpenAIEvents Events { get; set; }
    }

    public class OpenAIModels
    {
        public string AudioTranscribeModel { get; set; }
        public string SpeechGeneratorModel { get; set; }
        public string ChatModel { get; set; }
    }

    public class OpenAIEvents
    {
        public string SessionUpdate { get; set; }
        public string StartConversation { get; set; }
    }
}
