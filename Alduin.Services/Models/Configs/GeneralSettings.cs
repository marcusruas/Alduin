using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alduin.Core.Models.Configs
{
    public class GeneralSettings
    {
        public string AudiosFolder { get; set; }
        public string TwillioToken { get; set; }
        public string GreetingsAudio { get; set; }
        public OpenAISettings OpenAISettings { get; set; }
    }
}
