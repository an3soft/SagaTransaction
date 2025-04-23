using SagaTransaction.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TestSagaTransaction.Stages
{
    public abstract class BaseStage : ISagaStage, IDisposable
    {
        protected SagaState _state = SagaState.None;
        private readonly Uri _baseUri = new("https://jsonplaceholder.typicode.com/");
        protected static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        protected readonly HttpClient client;

        public abstract string StageInfo { get; }
        public SagaState State { get => _state; init => _state = value; }

        public BaseStage()
        {
            client = new()
            {
                BaseAddress = _baseUri
            };
        }

        protected static HttpRequestMessage GetHttpRequestMessage(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? headers = null)
        {
            var httpRequestMessage = new HttpRequestMessage(method, url);
            if (headers != null)
            {
                foreach (var header in headers)
                    httpRequestMessage.Headers.Add(header.Key, header.Value);
            }
            return httpRequestMessage;
        }

        public abstract ValueTask<SagaState> Process(Guid transactionId, CancellationToken cancellationToken = default);

        public abstract ValueTask<SagaState> Rollback(Guid transactionId, CancellationToken cancellationToken = default);

        public virtual void Dispose()
        {
            client.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
