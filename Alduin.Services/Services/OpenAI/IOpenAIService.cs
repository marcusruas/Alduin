using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alduin.Core.Services.OpenAI
{
    public interface IOpenAIService
    {
        Task<string> GenerateSpeech(string speech);
    }
}
