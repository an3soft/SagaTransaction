using Microsoft.Extensions.Logging;
using SagaTransaction.Abstractions;

namespace SagaTransaction
{
    /// <summary>
    /// Простой оркестратор саги
    /// </summary>
    /// <param name="logger"></param>
    public class SagaOrchestrator(ILogger<SagaOrchestrator> logger) : ISagaOrchestrator
    {
        private readonly ILogger _logger = logger;
        private ISagaStage[] _stages = [];
        private SagaState _state = SagaState.None;
        private RollbackState _rollbacked = RollbackState.None;
        private readonly Guid _transactionId = Guid.NewGuid();
        private readonly object lockObj = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private bool _complete = false;

        public Guid TransactionId => _transactionId;
        public SagaState State {
            get
            {
                SagaState ret = SagaState.None;
                lock (lockObj)
                {
                    ret = _state;
                }
                return ret;
            } }
        public ISagaStage[] Stages => _stages;
        public RollbackState Rollbacked
        {
            get
            {
                RollbackState ret = RollbackState.None;
                lock (lockObj)
                {
                    ret = _rollbacked;
                }
                return ret;
            }

            private set
            {
                lock (lockObj)
                {
                    _rollbacked = value;
                }
            }
        }

        /// <summary>
        /// Добавление этапов обработки
        /// </summary>
        /// <param name="stages">Этапы для обработки</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void AddStages(IEnumerable<ISagaStage> stages)
        {
            if (_state != SagaState.None)
                throw new InvalidOperationException("Этапы можно добавлять только до начала процесса обработки!");

            if (_stages.Length == 0)
                _stages = [.. stages];
            else
                _stages = [.. _stages, .. stages];
        }

        /// <summary>
        /// Выполнение обработки этапов
        /// </summary>
        /// <param name="processType"></param>
        /// <returns>Статус обработки</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<SagaState> Process(SagaProcessType processType = SagaProcessType.Sequentional, CancellationToken cancellationToken = default)
        {
            // Процесс выполняется однократно и не возобновляется
            if (State != SagaState.None)
                throw new InvalidOperationException("Процесс обработки уже был запущен!");

            // Статус = в обработке
            lock (lockObj)
            {
                _state = SagaState.InProcess;
            }

            switch (processType)
            {
                // Последовательное выполнение
                case SagaProcessType.Sequentional:

                    try
                    {
                        foreach (var stage in _stages)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (State != SagaState.InProcess)
                                break;

                            await ProcessStage(stage, cancellationToken);
                        }
                    }
                    finally
                    {
                        _complete = true;
                    }
                    break;

                // Параллельное выполнение
                case SagaProcessType.Parallel:

                    try
                    {
                        await Parallel.ForEachAsync(_stages, cancellationToken, async (stage, cancellationToken) =>
                        {
                            if (State == SagaState.InProcess)
                            {
                                await ProcessStage(stage, cancellationToken);
                            }
                        });
                    }
                    finally
                    {
                        _complete = true;
                    }
                    break;
            }

            if (State == SagaState.InProcess)
            {
                lock (lockObj)
                {
                    _state = SagaState.Completed;
                }
            }

            return State;
        }

        /// <summary>
        /// Обработка этапа
        /// </summary>
        /// <param name="stage"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask<SagaState> ProcessStage(ISagaStage stage, CancellationToken cancellationToken = default)
        {
            var stageState = SagaState.None;

            try
            {
                stageState = await stage.Process(_transactionId, cancellationToken);

                if (stageState != SagaState.Completed)
                {
                    lock (lockObj)
                    {
                        _state = SagaState.Faulted;
                    }
                }

                if (stage.State != stageState
                    || stageState == SagaState.None
                    || stageState == SagaState.InProcess)
                {
                    throw new InvalidOperationException("Не верный статус этапа после выполнения!");
                }
            }
            catch
            {
                lock (lockObj)
                {
                    _state = SagaState.Faulted;
                }
                throw;
            }
            finally
            {
                if (State == SagaState.Faulted && Rollbacked == RollbackState.None)
                    await Rollback(cancellationToken);
            }

            return stageState;
        }

        /// <summary>
        /// Функция отката обработки
        /// </summary>
        /// <returns></returns>
        private async ValueTask Rollback(CancellationToken cancellationToken = default)
        {
            await semaphore.WaitAsync(cancellationToken);

            // Если общая обработка прошла неуспешно
            if (State == SagaState.Faulted && Rollbacked == RollbackState.None)
            {
                // Если есть незавершённые этапы, то пробуем подождать, хотя такого быть не должно
                while (!_complete && _stages.Any(s => s.State == SagaState.InProcess))
                {
                    await Task.Delay(100, cancellationToken);
                }

                // Откатываем только успешно завершённые этапы, так как неуспешные или
                // не начатые нет смысла откатывать, как и те что в процессе
                foreach (var stage in _stages.Where(s => s.State == SagaState.Completed))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var state = await stage.Rollback(_transactionId, cancellationToken);
                        if (state != SagaState.Completed)
                        {
                            _logger.LogError("Ошибка отмены этапа \"{StageInfo}\" со статусом \"{State}\"!", stage.StageInfo, state);
                            Rollbacked = RollbackState.Faulted;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка отмены этапа \"{StageInfo}\"!", stage.StageInfo);
                        Rollbacked = RollbackState.Faulted;
                    }
                }

                // Если остались этапы в обработке, то это скорее всего ошибка реализации этих этапов
                if (_stages.Any(s => s.State == SagaState.InProcess))
                {
                    foreach (var stage in _stages.Where(s => s.State == SagaState.InProcess))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _logger.LogError("Ошибка отмены этапа \"{StageInfo}\": этап находиться в статусе обработки!", stage.StageInfo);
                    }
                    Rollbacked = RollbackState.Faulted;
                }

                if (Rollbacked == RollbackState.None)
                    Rollbacked = RollbackState.Completed;
            }

            semaphore.Release();
        }
    }
}
