using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alduin.Core.Models.AI;
using OpenAI.Chat;

namespace Alduin.Core.Services.OpenAI
{
    public interface IOpenAIService
    {
        Task<string> GenerateSpeech(string speech);
        Task<string> GenerateTranscriptFromRecording(Stream recording);
        Task<AIResponse> GenerateResponseFromCall(List<ChatMessage> chatHistory);
    }
}
