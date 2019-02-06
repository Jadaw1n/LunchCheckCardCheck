using NCrontab;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebApplication
{
    public class ScheduledTask : ScheduledProcessor
    {
        private readonly Func<Task> _process;
        private readonly string _schedule;

        public ScheduledTask(string schedule, Func<Task> process) : base(schedule)
        {
            _schedule = schedule;
            _process = process;

            StartAsync();
        }

        protected override string Schedule => _schedule;
        protected override Func<Task> Process => _process;
    }

    public abstract class ScheduledProcessor : BackgroundService
    {
        private CrontabSchedule _schedule;
        protected abstract string Schedule { get; }

        public ScheduledProcessor(string schedule)
        {
            _schedule = CrontabSchedule.Parse(schedule);
        }

        public ScheduledProcessor()
        {
            _schedule = CrontabSchedule.Parse(Schedule);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var nextRun = _schedule.GetNextOccurrence(DateTime.Now);
            if (DateTime.Now < nextRun)
            {
                await Task.Delay(nextRun - DateTime.Now, stoppingToken);
            }

            do
            {
                await Process();
                nextRun = _schedule.GetNextOccurrence(DateTime.Now);

                await Task.Delay(nextRun - DateTime.Now, stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
    }

    public abstract class BackgroundService
    {
        private Task _executingTask;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        public virtual Task StartAsync()
        {
            // Store the task we're executing
            _executingTask = ExecuteAsync(_stoppingCts.Token);

            // If the task is completed then return it,
            // this will bubble cancellation and failure to the caller
            if (_executingTask.IsCompleted)
            {
                return _executingTask;
            }

            // Otherwise it's running
            return Task.CompletedTask;
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            // Stop called without start
            if (_executingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                _stoppingCts.Cancel();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite,
                                                          cancellationToken));
            }
        }

        protected virtual async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //stoppingToken.Register(() =>
            //        _logger.LogDebug($" GracePeriod background task is stopping."));

            do
            {
                await Process();

                await Task.Delay(5000, stoppingToken); //5 seconds delay
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        protected abstract Func<Task> Process { get; }
    }

}
