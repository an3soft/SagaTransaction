using SagaTransaction.Abstractions;
using System.Text;
using System.Text.Json;
using TestSagaTransaction.Models;
using static System.Net.Mime.MediaTypeNames;

namespace TestSagaTransaction.Stages
{
    public class TestStage2(int id = 102) : BaseStage
    {
        private readonly int _id = id;
        public override string StageInfo => "TestStage2";

        public override async ValueTask<SagaState> Process(Guid transactionId, CancellationToken cancellationToken = default)
        {
            Post data = new()
            {
                Id = _id,
                UserId = 1,
                Title = "Test",
                Body = $"{transactionId}",
            };

            try
            {
                _state = SagaState.InProcess;

                using var request = GetHttpRequestMessage(HttpMethod.Post, "posts", [new("X-TransactionId", $"{transactionId}")]);
                request.Content = new StringContent(JsonSerializer.Serialize(data, _jsonOptions), Encoding.UTF8, Application.Json);

                using var responce = await client.SendAsync(request, cancellationToken);

                if (responce?.IsSuccessStatusCode ?? false)
                {
                    string answer = await responce.Content.ReadAsStringAsync(cancellationToken);
                    // Эмулируем проблему
                    throw new Exception(answer);
                    //Console.WriteLine(answer);
                    //_state = SagaState.Completed;
                }
                else
                {
                    _state = SagaState.Faulted;
                }
            }
            catch
            {
                _state = SagaState.Faulted;
            }

            return _state;
        }

        public override async ValueTask<SagaState> Rollback(Guid transactionId, CancellationToken cancellationToken = default)
        {
            SagaState rollbackState = SagaState.Faulted;
            // Эмуляция отката
            try
            {
                using var request = GetHttpRequestMessage(HttpMethod.Delete, $"posts/{_id}", [new("X-TransactionId", "transactionId")]);

                var responce = await client.SendAsync(request, cancellationToken);

                if (responce?.IsSuccessStatusCode ?? false)
                {
                    string answer = await responce.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine(answer);
                    rollbackState = SagaState.Completed;
                }
            }
            catch { }

            return rollbackState;
        }
    }
}
