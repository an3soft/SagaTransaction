using Microsoft.Extensions.Logging;
using SagaTransaction.Abstractions;

namespace SagaTransaction
{
    /// <summary>
    /// Простой оркестратор саги
    /// </summary>
    /// <param name="logger"></param>
    public sealed class SagaOrchestrator(ILogger<SagaOrchestrator> logger) : ISagaOrchestrator
    {
        private readonly ILogger _logger = logger;
        private ISagaStage[] _stages = [];
        private SagaState _state = SagaState.None;
        private SagaState _rollbackState = SagaState.None;
        private SagaProcessType _processType;
        private readonly Guid _transactionId = Guid.NewGuid();
        private readonly object lockObj = new();

        public Guid TransactionId => _transactionId;
        public SagaState State {
            get
            {
                SagaState ret;
                lock (lockObj)
                {
                    ret = _state;
                }
                return ret;
            }
            private set
            {
                lock (lockObj)
                {
                    _state = value;
                }
            }
        }
        public ISagaStage[] Stages => _stages;
        public SagaState RollbackState
        {
            get
            {
                SagaState ret;
                lock (lockObj)
                {
                    ret = _rollbackState;
                }
                return ret;
            }
            private set
            {
                lock (lockObj)
                {
                    _rollbackState = value;
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
            if (State != SagaState.None)
                throw new InvalidOperationException("Этапы можно добавлять только до начала процесса обработки!");

            if (_stages.Length == 0)
                _stages = [.. stages];
            else
                _stages = [.. _stages, .. stages];
        }

        /// <summary>
        /// Выполнение обработки этапов
        /// </summary>
        /// <param name="processType">Тип обработки</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Статус обработки</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<SagaState> Process(SagaProcessType processType = SagaProcessType.Sequentional, CancellationToken cancellationToken = default)
        {
            // Процесс выполняется однократно и не возобновляется
            if (State != SagaState.None)
                throw new InvalidOperationException("Процесс обработки уже был запущен!");

            // Статус = в обработке
            State = SagaState.InProcess;
            _processType = processType;

            try
            {
                switch (processType)
                {
                    // Последовательное выполнение
                    case SagaProcessType.Sequentional:

                        foreach (var stage in _stages)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (State != SagaState.InProcess)
                                break;

                            await ProcessStage(stage, cancellationToken);
                        }
                        break;

                    // Параллельное выполнение
                    case SagaProcessType.Parallel:

                        await Parallel.ForEachAsync(_stages, cancellationToken, async (stage, cancellationToken) =>
                        {
                            if (State == SagaState.InProcess)
                            {
                                await ProcessStage(stage, cancellationToken);
                            }
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                State = SagaState.Faulted;
                _logger.LogError(ex, "Ошибка обработки этапов!");
            }
            finally
            {
                if (State == SagaState.Faulted && RollbackState == SagaState.None)
                {
                    await Rollback(cancellationToken);
                }
                else if (State == SagaState.InProcess)
                {
                    State = SagaState.Completed;
                }
            }

            return State;
        }

        /// <summary>
        /// Обработка этапа
        /// </summary>
        /// <param name="stage">Этап</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns></returns>
        private async ValueTask<SagaState> ProcessStage(ISagaStage stage, CancellationToken cancellationToken = default)
        {
            SagaState stageState;

            try
            {
                stageState = await stage.Process(_transactionId, cancellationToken);

                if (stageState != SagaState.Completed)
                {
                    State = SagaState.Faulted;
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
                State = SagaState.Faulted;
                throw;
            }

            return stageState;
        }

        /// <summary>
        /// Функция отката обработки
        /// </summary>
        /// <returns></returns>
        private async ValueTask Rollback(CancellationToken cancellationToken = default)
        {
            // Если общая обработка прошла неуспешно
            if (State == SagaState.Faulted && RollbackState == SagaState.None)
            {
                // Если есть незавершённые этапы, то пробуем подождать, хотя такого быть не должно
                if (_stages.Any(s => s.State == SagaState.InProcess))
                {
                    await Task.Delay(250, cancellationToken);
                }

                // Откатываем только успешно завершённые этапы, так как неуспешные или
                // не начатые нет смысла откатывать, как и те что в процессе
                switch (_processType)
                {
                    // Последовательный откат
                    case SagaProcessType.Sequentional:

                        for (int i = _stages.Length - 1; i >= 0; i--)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (_stages[i].State == SagaState.Completed)
                            {
                                await RollbackStage(_stages[i], cancellationToken);
                            }
                        }
                        break;

                    // Параллельный откат
                    case SagaProcessType.Parallel:

                        await Parallel.ForEachAsync(_stages.Where(s => s.State == SagaState.Completed),
                                                    cancellationToken,
                                                    async (stage, cancellationToken) => await RollbackStage(stage, cancellationToken));
                        break;
                }

                // Если остались этапы в обработке, то это скорее всего ошибка реализации этих этапов
                if (_stages.Any(s => s.State == SagaState.InProcess))
                {
                    foreach (var stage in _stages.Where(s => s.State == SagaState.InProcess))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _logger.LogError("Ошибка отмены этапа \"{StageInfo}\": этап находиться в статусе обработки!", stage.StageInfo);
                    }
                    RollbackState = SagaState.Faulted;
                }

                if (RollbackState == SagaState.None)
                    RollbackState = SagaState.Completed;
            }
        }

        /// <summary>
        /// Откат этапа
        /// </summary>
        /// <param name="stage">Этап</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns></returns>
        private async ValueTask RollbackStage(ISagaStage stage, CancellationToken cancellationToken = default)
        {
            try
            {
                var state = await stage.Rollback(_transactionId, cancellationToken);
                if (state != SagaState.Completed)
                {
                    _logger.LogError("Ошибка отмены этапа \"{StageInfo}\" со статусом \"{State}\"!", stage.StageInfo, state);
                    RollbackState = SagaState.Faulted;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отмены этапа \"{StageInfo}\"!", stage.StageInfo);
                RollbackState = SagaState.Faulted;
            }
        }
    }
}
