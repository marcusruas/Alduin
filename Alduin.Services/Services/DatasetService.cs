//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using OpenAI.Chat;
//using OpenAI;
//using System.Text.Json;
//using System.Text.Json.Serialization.Metadata;
//using System.Text.Json.Serialization;
//using System.Text.Encodings.Web;
//using Alduin.Core.Models.Configs;
//using Alduin.Core.Models;

//namespace Alduin.Core.Services
//{
//    public class DatasetService
//    {
//        public DatasetService(GeneralSettings settings)
//        {
//            _settings = settings;
//        }

//        private readonly GeneralSettings _settings;

//        public async Task GenerateDatasetEntry(int reservationId)
//        {
//            try
//            {
//                var audioTranscriptPath = Path.Combine(_settings.BrainFolder, "ProcessedReservations", reservationId.ToString(), $"{reservationId}.txt");

//                if (!File.Exists(audioTranscriptPath))
//                    throw new ArgumentException($"Audio transcript for reservation {reservationId} not found");

//                var datasetEntry = new List<DatasetEntry>() 
//                { 
//                    new DatasetEntry("system", null, _settings.OpenAISettings.Prompts.OperatorPrompt) 
//                };
//                var transcriptLines = await File.ReadAllLinesAsync(audioTranscriptPath);

//                foreach(var transcriptLine in transcriptLines)
//                {
//                    string? formattedLine = transcriptLine?.Trim();
//                    if (transcriptLine.StartsWith("Speaker 1: "))
//                    {
//                        formattedLine = formattedLine.Replace("Speaker 1: ", string.Empty);
//                        datasetEntry.Add(new DatasetEntry("assistant", null, formattedLine));
//                    }
//                    else if (transcriptLine.StartsWith("Speaker 2: "))
//                    {
//                        formattedLine = formattedLine.Replace("Speaker 2: ", string.Empty);
//                        datasetEntry.Add(new DatasetEntry("user", null, formattedLine));
//                    }
//                    else
//                    {
//                        datasetEntry.Add(new DatasetEntry("function", "functionName", formattedLine));
//                        datasetEntry.Add(new DatasetEntry("function", "functionName", formattedLine));
//                    }
//                }

//                string datasetEntryFilePath = Path.Combine(_settings.BrainFolder, "Dataset", $"{reservationId}.jsonl");
//                var serializerOptions = new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, };

//                await File.WriteAllLinesAsync(datasetEntryFilePath, datasetEntry.Select(x => JsonSerializer.Serialize(x, serializerOptions)));
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Falha ao gerar dataset. Detalhes: {ex}");
//            }
//        }
//    }
//}
