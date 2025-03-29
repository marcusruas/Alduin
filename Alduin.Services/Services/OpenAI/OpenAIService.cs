using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alduin.Core.Models.Configs;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;

namespace Alduin.Core.Services.OpenAI
{
    public class OpenAIService : IOpenAIService
    {
        public OpenAIService(GeneralSettings settings)
        {
            _settings = settings;
        }

        private readonly GeneralSettings _settings;

        public Task<string> GenerateSpeech(string speech)
        {
            throw new NotImplementedException();
        }

    }
}
