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

public class UnitOfWork : IUnitOfWork, IDisposable
{
    public UnitOfWork(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string do Unit of Work não informada.");

        _connectionString = connectionString;
    }

    private string _connectionString { get; set; }
    private SqlConnection _connection { get; set; }
    
    public SqlTransaction? Transaction { get; private set; }
    public SqlConnection Connection
    {
        get
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqlConnection(_connectionString);
                _connection.Open();
            }

            return _connection;
        }
    }

    public void BeginTransaction()
    {
        Transaction = Connection.BeginTransaction();
    }

    public void Commit()
    {
        if (Transaction != null)
        {
            Transaction.Commit();
            Transaction.Dispose();
            Transaction = null;
        }
    }

    public void Rollback()
    {
        if (Transaction != null)
        {
            Transaction.Rollback();
            Transaction.Dispose();
            Transaction = null;
        }
    }

    public void Dispose()
    {
        Transaction?.Dispose();
        Connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}

    public abstract class BaseSqlRepository : IBaseSqlRepository
    {
        public BaseSqlRepository(INotifications notifications, ILoggingService logger, IUnitOfWork unitOfWork)
        {
            Notifications = notifications;
            Logger = logger;
            _unitOfWork = unitOfWork;

            SetSqlPath();
        }

        protected SqlConnection Connection => _unitOfWork.Connection;
        protected SqlTransaction Transaction => _unitOfWork.Transaction;
        protected readonly INotifications Notifications;
        protected ILoggingService Logger;

        private readonly IUnitOfWork _unitOfWork;
        private string? _sqlFolderPath { get; set; }

        /// <summary>
        /// Inicia uma SqlTransaction no banco de dados. Essa transação é usada em todas as operações que envolva conexão ao banco de dados até que a mesma seja finalizada.
        /// </summary>
        public void BeginTransaction()
        {
            _unitOfWork.BeginTransaction();
        }

        /// <summary>
        /// Realiza o commit da SqlTransaction aberta, se tiver uma.
        /// </summary>
        public void Commit()
        {
            _unitOfWork.Commit();
        }

        /// <summary>
        /// Realize o rollback da SqlTransaction aberta, se tiver uma.
        /// </summary>
        public void Rollback()
        {
            _unitOfWork.Rollback();
        }

        protected async Task<PaginatedList<T>> ConvertGridReaderToPaginatedList<T>(GridReader gridReader, int pageNumber, int pageSize)
        {
            var resultSet = await gridReader.ReadAsync<T>();
            var totalAmountOfRecords = await gridReader.ReadFirstOrDefaultAsync<int>();

            return new PaginatedList<T>(resultSet, pageNumber, totalAmountOfRecords, pageSize);
        }

        /// <summary>
        /// Executa um script no banco de dados e quantas linhas foram afetadas como resultado. Ele utiliza o método <see cref="ExecuteAsync{T}(string, string, object?)"/> do dapper para realizar a consulta
        /// 
        /// Caso ocorra algum erro (exception) na função, será adicionado uma mensagem do tipo <see cref="NotificationType.Error"/> no objeto 
        /// de mensageria e a mesma será retornada.
        /// </summary>
        /// <param name="commandName">O nome do arquivo que está armazenado a query. O conteúdo do arquivo deverá conter a query a ser executada.</param>
        /// <param name="parameters">Os parameters para a query</param>
        /// <returns></returns>
        protected async Task<int> ExecuteAsync(string commandName, object? parameters = null)
        {
            try
            {
                var command = GetSqlFileContentByName(commandName);
                return await Connection.ExecuteAsync(command, parameters, Transaction);
            }
            catch (Exception ex)
            {
                Logger.LogError(new { commandName, parameters }, ex, "Ocorreu uma falha ao executar uma query SQL");
                Notifications.ReturnErrorNotification($"Ocorreu um erro ao acessar nossa base de dados. Contate o setor de suporte.");
                return -1;
            }
        }

        /// <summary>
        /// Executa uma consulta no banco de dados e retorna uma lista do tipo <see cref="T"/> como resultado. Ele utiliza o método <see cref="QueryAsync{T}(string, string, object?)"/> do dapper para realizar a consulta
        /// 
        /// Caso ocorra algum erro (exception) na função, será adicionado uma mensagem do tipo <see cref="NotificationType.Error"/> no objeto 
        /// de mensageria e a mesma será retornada.
        /// </summary>
        /// <param name="commandName">O nome do arquivo que está armazenado a query. O conteúdo do arquivo deverá conter a query a ser executada.</param>
        /// <param name="parameters">Os parameters para a query</param>
        /// <returns></returns>
        protected async Task<IEnumerable<T>> QueryAsync<T>(string commandName, object? parameters = null)
        {
            try
            {
                var command = GetSqlFileContentByName(commandName);
                return await Connection.QueryAsync<T>(command, parameters, Transaction);
            }
            catch (Exception ex)
            {
                Logger.LogError(new { commandName, parameters }, ex, "Ocorreu uma falha ao executar uma query SQL");
                Notifications.ReturnErrorNotification($"Ocorreu um erro ao acessar nossa base de dados. Contate o setor de suporte.");
                return null;
            }
        }

        /// <summary>
        /// Executa uma consulta no banco de dados e retorna um objeto do tipo <see cref="T"/> como resultado. Ele utiliza o método <see cref="QueryAsync{T}(string, string, object?)"/> do dapper para realizar a consulta
        /// Caso ocorra algum erro (exception) na função, será adicionado uma mensagem do tipo <see cref="NotificationType.Error"/> no objeto 
        /// de mensageria e a mesma será retornada.
        /// </summary>
        /// <param name="commandName">O nome do arquivo que está armazenado a query. O conteúdo do arquivo deverá conter a query a ser executada.</param>
        /// <param name="parameters">Os parameters para a query</param>
        /// <returns></returns>
        protected async Task<T> QueryFirstOrDefaultAsync<T>(string commandName, object? parameters = null)
        {
            try
            {
                var command = GetSqlFileContentByName(commandName);
                return await Connection.QueryFirstOrDefaultAsync<T>(command, parameters, Transaction);
            }
            catch (Exception ex)
            {
                Logger.LogError(new { commandName, parameters }, ex, "Ocorreu uma falha ao executar uma query SQL");
                Notifications.ReturnErrorNotification($"Ocorreu um erro ao acessar nossa base de dados. Contate o setor de suporte.");
                return default;
            }
        }

        /// <summary>
        /// Executa uma consulta no banco de dados e retorna multiplas queries como resultado. Ele utiliza o método <see cref="QueryMultipleAsync{T}(string, string, object?)"/> do dapper para realizar a consulta
        /// 
        /// Caso ocorra algum erro (exception) na função, será adicionado uma mensagem do tipo <see cref="NotificationType.Error"/> no objeto 
        /// de mensageria e a mesma será retornada.
        /// </summary>
        /// <param name="commandName">O nome do arquivo que está armazenado a query. O conteúdo do arquivo deverá conter a query a ser executada.</param>
        /// <param name="parameters">Os parameters para a query</param>
        /// <returns></returns>
        protected async Task<GridReader> QueryMultipleAsync(string commandName, object? parameters = null)
        {
            try
            {
                var command = GetSqlFileContentByName(commandName);
                return await Connection.QueryMultipleAsync(command, parameters, Transaction, commandTimeout: 600);
            }
            catch (Exception ex)
            {
                Logger.LogError(new { commandName, parameters }, ex, "Ocorreu uma falha ao executar uma query SQL");
                Notifications.ReturnErrorNotification($"Ocorreu um erro ao acessar nossa base de dados. Contate o setor de suporte.");
                return null;
            }
        }

        /// <summary>
        /// Executa uma consulta no banco de dados e retorna se a consulta traz ao menos um resultado. Ele utiliza o método <see cref="QueryFirstOrDefaultAsync{bool}(string, string, object?)"/> do dapper para realizar a consulta
        /// 
        /// Caso ocorra algum erro (exception) na função, será adicionado uma mensagem do tipo <see cref="NotificationType.Error"/> no objeto 
        /// de mensageria e a mesma será retornada.
        /// </summary>
        /// <param name="commandName">O nome do arquivo que está armazenado a query. O conteúdo do arquivo deverá conter a query a ser executada.</param>
        /// <param name="parameters">Os parameters para a query</param>
        /// <returns></returns>
        protected async Task<bool> ExistsAsync(string commandName, object? parameters = null)
        {
            try
            {
                var command = GetSqlFileContentByName(commandName);
                return await Connection.QueryFirstOrDefaultAsync<bool>(command, parameters, Transaction);
            }
            catch (Exception ex)
            {
                Logger.LogError(new { commandName, parameters }, ex, "Ocorreu uma falha ao executar uma query SQL");
                Notifications.ReturnErrorNotification($"Ocorreu um erro ao acessar nossa base de dados. Contate o setor de suporte.");
                return default;
            }
        }

        /// <summary>
        /// Esse método adiciona uma cláusula de "Order by" em um <see cref="Dapper.SqlBuilder"/>. Esse método usa o StringValueOf do enum de ordenação, dessa forma, se ele tiver valor, ele adiciona um order by como: "ORDER BY CAMPO ASC/DESC"
        /// </summary>
        /// <param name="fieldName">O nome do campo a ser adicionado no order by</param>
        /// <param name="option">A opção de ordenação. Se ela estiver null não será adicionado nenhuma.</param>

        protected void AddOrderByClause(SqlBuilder builder, string fieldName, OrderingOption? option)
        {
            if (!option.HasValue)
                return;

            builder.OrderBy($"{fieldName} {option.StringValueOf()}");
        }

        /// <summary>
        /// Obtém o conteudo (query SQL) do arquivo cujo nome é informado 
        /// 
        /// Caso o arquivo não possa ser encontrado, será adicionado uma mensagem do tipo <see cref="NotificationType.Error"/> no objeto de mensageria e será retornado null.
        /// Caso haja qualquer tipo de erro ao tentar ler o arquivo, será adicionado uma mensagem de erro como no exemplo acima informando o que ocorreu.
        /// </summary>
        /// <param name="fileName">Nome do arquivo SQL que deseja ser obtido (não é necessário o .sql no final)</param>
        protected string GetSqlFileContentByName(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_sqlFolderPath))
                    return null;

                string fixedFileName = fileName.EndsWith(".sql") ? fileName : $"{fileName}.sql";
                string[] lines;
                string fileContent = string.Empty;

                string filePath = Path.Combine(_sqlFolderPath, fixedFileName);
                filePath = filePath.Replace("file:\\", "");

                if (!File.Exists(filePath))
                    throw new InvalidOperationException("Arquivo informado não existe");

                lines = File.ReadAllLines(filePath);

                return string.Join(' ', lines);
            }
            catch (ArgumentNullException ex)
            {
                Logger.LogError(fileName, ex, "O arquivo de consulta ao banco de dados {fileName} está vazio.", fileName);
                Notifications.ReturnErrorNotification($"O arquivo de consulta ao banco de dados {fileName} está vazio.");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError(fileName, ex, "Ocorreu uma falha ao ler o arquivo {fileName}", fileName);
                Notifications.ReturnErrorNotification($"Ocorreu uma falha ao ler o arquivo {fileName}");
                return null;
            }
        }

        private void SetSqlPath()
        {
            string rootProject = Path.GetDirectoryName(GetType().Assembly.Location) ?? "";
            string[] namespaces = GetType().Namespace?.Split(".") ?? [];
            var indexRepositoryLayer = Array.IndexOf(namespaces, "Infrastructure");
            _sqlFolderPath = rootProject;

            if (namespaces.Length > indexRepositoryLayer + 1)
                for (int index = indexRepositoryLayer + 1; index < namespaces.Length; index++)
                    _sqlFolderPath = Path.Combine(_sqlFolderPath, namespaces[index]);

            _sqlFolderPath = Path.Combine(_sqlFolderPath, "SQL");
            _sqlFolderPath = _sqlFolderPath.Replace("file:\\", "");
            _sqlFolderPath = _sqlFolderPath.Replace("\\", "/");
        }
    }
}
