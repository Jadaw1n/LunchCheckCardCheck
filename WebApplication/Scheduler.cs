using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace WebApplication
{
    public class Scheduler
    {
        private readonly CrontabSchedule _schedule;
        private Timer _timer;

        public Scheduler(string schedule, Action action)
        {
            _schedule = CrontabSchedule.Parse(schedule);

            _timer = new Timer();
            _timer.Elapsed += (object sender, ElapsedEventArgs eea) =>
            {
                try
                {
                    action();
                }
                finally
                {
                    Start();
                }
            };
        }

        public void Start()
        {
            var now = DateTime.Now;
            var nextDt = _schedule.GetNextOccurrence(now);

            _timer.Interval = (nextDt - now).TotalMilliseconds;
            _timer.Start();

        }

        public void Stop()
        {
            _timer.Stop();
        }
    }
}
