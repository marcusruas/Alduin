# 🐉 Alduin — AI Customer Service Middleware

**Alduin** is a WebSocket-based middleware for real-time AI-powered customer service, integrating OpenAI's real-time voice API (e.g., `gpt-4o-realtime-preview`) with external platforms like Twilio. It allows developers to define custom backend functions that are dynamically invoked by the assistant during conversations.

---

## ✨ Features

- 📞 Seamless integration with OpenAI and Twilio via WebSocket
- 🧠 Real-time voice-based conversations powered by GPT models
- 🧩 Developer-defined functions that the assistant can invoke via `function_call`
- 🔧 Minimal configuration with flexible extension points
- 📦 Designed to be consumed as a NuGet package

---

## 📦 Installation

Coming soon to NuGet...

For local testing:

```bash
dotnet add package Alduin.CustomerService --version 1.0.0
```

``` c#
//When configuring services
builder.services.AddMemoryCache();
builder.Services.AddAlduin(options =>
{
    options.OpenAIApiKey = "sk-...";
    options.OperatorInstructions = "You are a helpful customer service assistant that helps the customer with his purchases.";
},
functions =>
{
    functions.Register<CepArgs>("search_zip_code", async args =>
    {
        return new { result = "street example", zipCode = args.zipCode };
    });

    functions.Register<PedidoArgs>("check_purchase_status", async args =>
    {
        return new { status = "Delivered", purchaseId = args.purchaseId };
    });
});

//When configuring the middlewares
app.UseWebSockets();
app.UseAlduin();
```

## 🧠 How Function Calls Work

When the assistant sends a function_call with a name and JSON arguments, Alduin:

* Looks up the function by name via the AlduinFunctionRegistry;
* Deserializes the arguments into a strongly typed object;
* Invokes your handler and returns the result to the assistant.

##🛠 Technologies Used

* .NET 8;
* ASP.NET Core Minimal API;
* OpenAI WebSocket Realtime API;
* Twilio Media Streams;
* System.Text.Json;
* IMemoryCache;

