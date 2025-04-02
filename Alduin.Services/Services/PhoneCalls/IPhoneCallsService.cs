using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alduin.Core.Models.AI;

namespace Alduin.Core.Services.PhoneCalls
{
    public interface IPhoneCallsService
    {
        Task StartPhoneCall(string callSid);
        Task<AIResponse> GenerateResponseFromAssistant(string callSid, string userRecordingUrl);
    }
}
